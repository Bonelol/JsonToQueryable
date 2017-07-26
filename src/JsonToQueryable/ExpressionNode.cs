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
        internal ExpressionNode Parent { get; set; }

        internal ExpressionNode Root => this.Parent == null ? this : this.Parent.Root;

        internal int Depth { get; set; }

        internal bool IsEnumerable { get; set; }

        internal Dictionary<string, ExpressionNode> Properties { get; set; }

        internal bool IsReferencedType { get; set; }

        internal string NodeName { get; set; }

        internal Type Type { get; }
        internal ParameterExpression ParameterExpression { get; }

        internal ExpressionNode(Type type, string name, ExpressionNode parent)
        {
            var isEnumerable = typeof(IEnumerable).IsAssignableFrom(type) && type.GenericTypeArguments.Length > 0;
            var t = isEnumerable ? type.GenericTypeArguments[0] : type;

            IsEnumerable = isEnumerable;
            Properties = new Dictionary<string, ExpressionNode>();
            Type = t;
            ParameterExpression = Expression.Parameter(t, t.Name);
            NodeName = name;
            Parent = parent;

            Depth = parent?.Depth + 1 ?? 0;

            if (parent != null)
            {
                var inverse = typeof(InversePropertyAttribute);
                var propertyInfo = parent.Type.GetProperty(name);
                IsReferencedType = propertyInfo.CustomAttributes.Any(c => c.AttributeType == inverse);
            }
        }

        internal Expression ComputedExpression { get; set; }

        /// <summary>
        /// For where clause
        /// </summary>
        internal Expression WherePredictExpression { get; set; }

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

        internal static MethodInfo CreateGenericAnyMethod(Type type)
        {
            var qInfo = typeof(Enumerable);
            var mInfos = qInfo.GetMethods().Where(m => m.Name == "Any");
            var mInfo = mInfos.First(m => m.GetGenericArguments().Length == 1 && m.GetParameters().Length == 2).MakeGenericMethod(type);
            return mInfo;
        }

        internal static MethodInfo CreateGenericWhereMethod(Type type)
        {
            var qInfo = typeof(Enumerable);
            var mInfos = qInfo.GetMethods().Where(m => m.Name == "Where");
            var mInfo = mInfos.First(m => m.GetGenericArguments().Length == 1 && m.GetParameters().Length == 2).MakeGenericMethod(type);
            return mInfo;
        }

        internal Expression CreateIncludeExpression(IQueryable queryable, bool hasncludeFilter)
        {
            var include = CreateGenericIncludeMethod(this.Parent, this);
            var includePropertyExpr = (Expression) Expression.Property(this.Parent.ParameterExpression, this.NodeName);

            if (hasncludeFilter && this.Properties.Count > 0)
            {
                var w = CreateGenericWhereMethod(this.Type);
                var list = this.Properties.Values.Where(p => !p.IsReferencedType && p.ComputedExpression != null).Select(p => p.ComputedExpression).ToList();

                if (list.Any())
                {
                    var whereExpr = list.Aggregate(Expression.AndAlso);
                    var l = Expression.Lambda(whereExpr, false, this.ParameterExpression);
                    var c = Expression.Property(this.Parent.ParameterExpression, this.NodeName);
                    includePropertyExpr = Expression.Call(null, w, new Expression[] {c, l});

                    include = CreateGenericIncludeWithFilterMethod(this.Parent, this);
                }
            }

            var lambda = Expression.Lambda(includePropertyExpr, false, this.Parent.ParameterExpression);
            var exp = Expression.Call(null, include, new[] {queryable.Expression, Expression.Quote(lambda)});

            return exp;
        }

        internal MethodCallExpression CreateIncludeExpression1(Expression pExpression, bool hasncludeFilter)
        {
            var include = this.Depth > 1 ? CreateGenericThenIncludeMethod(this.Parent, this) : CreateGenericIncludeMethod(this.Parent, this);
            var includePropertyExpr = (Expression) Expression.Property(this.Parent.ParameterExpression, this.NodeName);

            if (hasncludeFilter && this.Properties.Count > 0)
            {
                var w = CreateGenericWhereMethod(this.Type);
                var list = this.Properties.Values.Where(p => !p.IsReferencedType && p.ComputedExpression != null).Select(p => p.ComputedExpression).ToList();

                if (list.Any())
                {
                    var whereExpr = list.Aggregate(Expression.AndAlso);
                    var l = Expression.Lambda(whereExpr, false, this.ParameterExpression);
                    var c = Expression.Property(this.Parent.ParameterExpression, this.NodeName);
                    includePropertyExpr = Expression.Call(null, w, new Expression[] {c, l});

                    include = this.Depth > 1 ? CreateGenericThenIncludeWithFilterMethod(this.Parent, this) : CreateGenericIncludeWithFilterMethod(this.Parent, this);
                }
            }

            var lambda = Expression.Lambda(includePropertyExpr, false, this.Parent.ParameterExpression);
            var exp = Expression.Call(null, include, new[] {pExpression, Expression.Quote(lambda)});

            return this.Properties.Values.Where(v => v.IsReferencedType).Aggregate(exp, (expression, node) => node.CreateIncludeExpression1(expression, hasncludeFilter));
        }

        internal Expression CreateIncludeExpression2(Expression pExpression, bool hasncludeFilter)
        {
            //if this is the end of node chain
            if (!this.Properties.Values.Any(p => p.IsReferencedType))
            {
                var include = this.Depth > 1 ? CreateGenericThenIncludeMethod(this.Parent, this) : CreateGenericIncludeMethod(this.Parent, this);
                var includePropertyExpr = (Expression) Expression.Property(this.Parent.ParameterExpression, this.NodeName);

                var lambda = Expression.Lambda(includePropertyExpr, false, this.Parent.ParameterExpression);
                var exp = Expression.Call(null, include, new[] {pExpression, Expression.Quote(lambda)});

                return this.Properties.Values.Where(v => v.IsReferencedType).Aggregate(exp, (expression, node) => node.CreateIncludeExpression1(expression, hasncludeFilter));
            }

            Expression result = pExpression;

            foreach (var node in this.Properties.Values.Where(p => p.IsReferencedType))
            {
                var include = this.Depth > 1 ? CreateGenericThenIncludeMethod(this.Parent, this) : CreateGenericIncludeMethod(this.Parent, this);
                var includePropertyExpr = (Expression) Expression.Property(this.Parent.ParameterExpression, this.NodeName);

                var lambda = Expression.Lambda(includePropertyExpr, false, this.Parent.ParameterExpression);
                var exp = Expression.Call(null, include, new[] {result, Expression.Quote(lambda)});

                result = node.CreateIncludeExpression2(exp, hasncludeFilter);
            }

            return result;
        }

        internal static MethodInfo CreateGenericIncludeMethod(ExpressionNode parent, ExpressionNode node)
        {
            var propertyType = node.IsEnumerable ? typeof(ICollection<>).MakeGenericType(node.Type) : node.Type;
            var mInfo = typeof(EntityFrameworkQueryableExtensions).GetMethods()
                .Where(m => m.Name == "Include")
                .First(m => m.GetGenericArguments().Length == 2)
                .MakeGenericMethod(parent.Type, propertyType);
            return mInfo;
        }

        internal static MethodInfo CreateGenericThenIncludeMethod(ExpressionNode parent, ExpressionNode node)
        {
            var propertyType = node.IsEnumerable ? typeof(ICollection<>).MakeGenericType(node.Type) : node.Type;
            var previousPropertyType = node.IsEnumerable ? typeof(ICollection<>).MakeGenericType(node.Type) : node.Type;
            var mInfo = typeof(EntityFrameworkQueryableExtensions).GetMethods()
                .Where(m => m.Name == "ThenInclude").ElementAt(parent.IsEnumerable ? 0 : 1)
                .MakeGenericMethod(parent.Root.Type, parent.Type, propertyType);
            return mInfo;
        }

        internal static MethodInfo CreateGenericIncludeWithFilterMethod(ExpressionNode parent, ExpressionNode node)
        {
            var propertyType = node.IsEnumerable ? typeof(IEnumerable<>).MakeGenericType(node.Type) : node.Type;
            var mInfo = Type.GetType("EntityFrameworkCore.IncludeFilter.QueryableExtensions,EntityFrameworkCore.IncludeFilter").GetMethods()
                .Where(m => m.Name == "IncludeWithFilter")
                .First(m => m.GetGenericArguments().Length == 2)
                .MakeGenericMethod(parent.Type, propertyType);
            return mInfo;
        }

        internal static MethodInfo CreateGenericThenIncludeWithFilterMethod(ExpressionNode parent, ExpressionNode node)
        {
            var propertyType = node.IsEnumerable ? typeof(IEnumerable<>).MakeGenericType(node.Type) : node.Type;
            var mInfo = Type.GetType("EntityFrameworkCore.IncludeFilter.QueryableExtensions,EntityFrameworkCore.IncludeFilter").GetMethods()
                .Where(m => m.Name == "ThenIncludeWithFilter")
                .First(m => m.GetGenericArguments().Length == 2)
                .MakeGenericMethod(parent.Type, propertyType);
            return mInfo;
        }

        internal Expression CreateAnyExpression()
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