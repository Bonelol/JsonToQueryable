using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace JsonToQueryable
{
    public class ExpressionNode
    {
        private ExpressionNode Parent { get; }
        private ExpressionNode Root => this.Parent == null ? this : this.Parent.Root;
        private int Depth { get; }
        private string NodeName { get; }
        private bool IsEnumerable { get; }
        internal Dictionary<string, ExpressionNode> Properties { get; }
        internal bool IsReferencedType { get; }
        internal Type Type { get; }
        internal ParameterExpression ParameterExpression { get; }

        internal ExpressionNode(Type type, string name, ExpressionNode parent, Expression computedExpression = null)
        {
            var isEnumerable = typeof(IEnumerable).IsAssignableFrom(type) && type.GenericTypeArguments.Length > 0;
            var t = isEnumerable ? type.GenericTypeArguments[0] : type;

            IsEnumerable = isEnumerable;
            Properties = new Dictionary<string, ExpressionNode>();
            Type = t;
            ParameterExpression = Expression.Parameter(t, t.Name);
            NodeName = name;
            Parent = parent;
            ComputedExpression = computedExpression;
            Depth = parent?.Depth + 1 ?? 0;

            if (parent == null) return;

            var inverse = typeof(InversePropertyAttribute);
            var propertyInfo = parent.Type.GetProperty(name);
            IsReferencedType = propertyInfo.CustomAttributes.Any(c => c.AttributeType == inverse);
        }

        private Expression ComputedExpression { get; }

        /// <summary>
        /// For where clause
        /// </summary>
        internal Expression WherePredictExpression { get; private set; }

        internal Expression CreateWherePredictExpression()
        {
            foreach (var subNode in this.Properties.Values)
            {
                subNode.CreateWherePredictExpression();
                var expr = subNode.IsEnumerable ? subNode.CreateAnyExpression() : subNode.ComputedExpression;

                subNode.WherePredictExpression = expr;
            }

            var predicts = this.Properties.Values.Where(p => p.WherePredictExpression != null).Select(p => p.WherePredictExpression).ToList();
            return predicts.Any() ? this.WherePredictExpression = predicts.Aggregate(Expression.AndAlso) : null;
        }

        internal Expression CreateIncludeFilterPredictExpression()
        {
            var predicts = this.Properties.Values.Where(n => !n.IsReferencedType && n.ComputedExpression != null).Select(n => n.ComputedExpression).ToList();
            return predicts.Any() ? this.WherePredictExpression = predicts.Aggregate(Expression.AndAlso) : null;
        }

        private static MethodInfo CreateGenericAnyMethod(Type type)
        {
            var qInfo = typeof(Enumerable);
            var mInfos = qInfo.GetMethods().Where(m => m.Name == ExpressionGenerator.ANY);
            var mInfo = mInfos.First(m => m.GetGenericArguments().Length == 1 && m.GetParameters().Length == 2).MakeGenericMethod(type);
            return mInfo;
        }

        private static MethodInfo CreateGenericWhereMethod(Type type)
        {
            var qInfo = typeof(Enumerable);
            var mInfos = qInfo.GetMethods().Where(m => m.Name == ExpressionGenerator.WHERE);
            var mInfo = mInfos.First(m => m.GetGenericArguments().Length == 1 && m.GetParameters().Length == 2).MakeGenericMethod(type);
            return mInfo;
        }

        internal Expression CreateIncludeExpression(Expression pExpression, bool hasncludeFilter)
        {
            //if this is the end of node chain
            if (!this.Properties.Values.Any(p => p.IsReferencedType))
            {
                var exp = CreateIncludeMethodCallExpression(pExpression, hasncludeFilter);
                return this.Properties.Values.Where(v => v.IsReferencedType).Aggregate(exp, (expression, node) => (MethodCallExpression)node.CreateIncludeExpression(expression, hasncludeFilter));
            }

            var result = pExpression;

            foreach (var node in this.Properties.Values.Where(p => p.IsReferencedType))
            {
                var exp = CreateIncludeMethodCallExpression(pExpression, hasncludeFilter);
                result = node.CreateIncludeExpression(exp, hasncludeFilter);
            }

            return result;
        }

        private MethodCallExpression CreateIncludeMethodCallExpression(Expression pExpression, bool hasncludeFilter)
        {
            MethodInfo include;
            Expression includePropertyExpr;
            var list = this.Properties.Values.Where(p => !p.IsReferencedType && p.ComputedExpression != null).Select(p => p.ComputedExpression).ToList();

            if (hasncludeFilter && list.Any())
            {
                var w = CreateGenericWhereMethod(this.Type);
                var whereExpr = list.Aggregate(Expression.AndAlso);
                var l = Expression.Lambda(whereExpr, false, this.ParameterExpression);
                var c = Expression.Property(this.Parent.ParameterExpression, this.NodeName);
                includePropertyExpr = Expression.Call(null, w, new Expression[] { c, l });

                var icollection = typeof(ICollection<>).MakeGenericType(this.Type);
                includePropertyExpr = Expression.Convert(includePropertyExpr, icollection);
                include = this.Depth > 1 ? CreateGenericThenIncludeWithFilterMethod(this.Parent, this) : CreateGenericIncludeWithFilterMethod(this.Parent, this);
            }
            else
            {
                includePropertyExpr = Expression.Property(this.Parent.ParameterExpression, this.NodeName);
                include = this.Depth > 1 ? CreateGenericThenIncludeMethod(this.Parent, this) : CreateGenericIncludeMethod(this.Parent, this);
            }

            var lambda = Expression.Lambda(includePropertyExpr, false, this.Parent.ParameterExpression);
            var exp = Expression.Call(null, include, new[] { pExpression, Expression.Quote(lambda) });
            return exp;
        }

        private static MethodInfo CreateGenericIncludeMethod(ExpressionNode parent, ExpressionNode node)
        {
            var propertyType = node.IsEnumerable ? typeof(ICollection<>).MakeGenericType(node.Type) : node.Type;
            var mInfo = typeof(EntityFrameworkQueryableExtensions).GetMethods()
                .Where(m => m.Name == ExpressionGenerator.INCLUDE)
                .First(m => m.GetGenericArguments().Length == 2)
                .MakeGenericMethod(parent.Type, propertyType);
            return mInfo;
        }

        private static MethodInfo CreateGenericThenIncludeMethod(ExpressionNode parent, ExpressionNode node)
        {
            var propertyType = node.IsEnumerable ? typeof(ICollection<>).MakeGenericType(node.Type) : node.Type;
            var mInfo = typeof(EntityFrameworkQueryableExtensions).GetMethods()
                .Where(m => m.Name == ExpressionGenerator.THEN_INCLUDE).ElementAt(parent.IsEnumerable ? 0 : 1)
                .MakeGenericMethod(parent.Root.Type, parent.Type, propertyType);
            return mInfo;
        }

        private static MethodInfo CreateGenericIncludeWithFilterMethod(ExpressionNode parent, ExpressionNode node)
        {
            var propertyType = node.IsEnumerable ? typeof(ICollection<>).MakeGenericType(node.Type) : node.Type;
            var mInfo = Type.GetType("EntityFrameworkCore.IncludeFilter.QueryableExtensions,EntityFrameworkCore.IncludeFilter").GetMethods()
                .Where(m => m.Name == ExpressionGenerator.INCLUDE_WITH_FILTER)
                .First(m => m.GetGenericArguments().Length == 2)
                .MakeGenericMethod(parent.Type, propertyType);
            return mInfo;
        }

        private static MethodInfo CreateGenericThenIncludeWithFilterMethod(ExpressionNode parent, ExpressionNode node)
        {
            var propertyType = node.IsEnumerable ? typeof(ICollection<>).MakeGenericType(node.Type) : node.Type;
            var mInfo = Type.GetType("EntityFrameworkCore.IncludeFilter.QueryableExtensions,EntityFrameworkCore.IncludeFilter").GetMethods()
                .Where(m => m.Name == ExpressionGenerator.THEN_INCLUDE_WITH_FILTER)
                .First(m => m.GetGenericArguments().Length == 3)
                .MakeGenericMethod(parent.Root.Type, parent.Type, propertyType);
            return mInfo;
        }

        private Expression CreateAnyExpression()
        {
            if (this.WherePredictExpression == null)
                return null;

            var wInfo = CreateGenericAnyMethod(this.Type);
            var lambda = Expression.Lambda(this.WherePredictExpression, false, this.ParameterExpression);
            var c = Expression.Property(this.Parent.ParameterExpression, this.NodeName);
            var exp = Expression.Call(null, wInfo, new Expression[] {c, lambda});

            return exp;
        }
    }
}