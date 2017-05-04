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

        private int Level { get; }

        private bool IsArray { get; }

        internal Dictionary<string, ExpressionNode> Properties { get; }

        internal bool IsReferencedType { get; }

        private string NodeName { get; }

        internal Type Type { get; }
        internal ParameterExpression ParameterExpression { get; }

        internal ExpressionNode(Type type, string name, ExpressionNode parent)
        {
            var isArray = typeof(IEnumerable).IsAssignableFrom(type) && type.GenericTypeArguments.Length > 0;
            var t = isArray ? type.GenericTypeArguments[0] : type;

            IsArray = isArray;
            Properties = new Dictionary<string, ExpressionNode>();
            Type = t;
            ParameterExpression = Expression.Parameter(t, t.Name);
            NodeName = name;
            Parent = parent;

            Level = parent?.Level + 1 ?? 0;

            if (parent != null)
            {
                var inverse = typeof(InversePropertyAttribute);
                var propertyInfo = parent.Type.GetProperty(name);
                IsReferencedType = propertyInfo.CustomAttributes.Any(c => c.AttributeType == inverse);
            }
        }

        internal Expression ComputedExpression { private get; set; }

        internal Expression PredictExpression { get; private set; }

        internal Expression CreatePredictExpression()
        {
            if (this.Properties.Count > 0)
            {
                foreach (var subNode in this.Properties.Values)
                {
                    subNode.CreatePredictExpression();
                    var expr = subNode.IsArray ? subNode.CreateAnyExpression() : subNode.ComputedExpression;

                    subNode.PredictExpression = expr;
                }
            }

            var predicts = this.Properties.Values.Where(p => p.PredictExpression != null).Select(p => p.PredictExpression).ToList();
            return predicts.Any() ? this.PredictExpression = predicts.Aggregate(Expression.AndAlso) : null;
        }

        private static MethodInfo CreateGenericAnyMethod(Type type)
        {
            var qInfo = typeof(Enumerable);
            var mInfos = qInfo.GetMethods().Where(m => m.Name == "Any");
            var mInfo = mInfos.First(m => m.GetGenericArguments().Length == 1 && m.GetParameters().Length == 2).MakeGenericMethod(type);
            return mInfo;
        }

        private static MethodInfo CreateGenericWhereMethod(Type type)
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
                    includePropertyExpr = Expression.Call(null, w, new Expression[] { c, l });

                    include = CreateGenericIncludeWithFilterMethod(this.Parent, this);
                }
            }

            var lambda = Expression.Lambda(includePropertyExpr, false, this.Parent.ParameterExpression);
            var exp = Expression.Call(null, include, new[] {queryable.Expression, Expression.Quote(lambda)});

            return exp;
        }


        private static MethodInfo CreateGenericIncludeMethod(ExpressionNode parent, ExpressionNode node)
        {
            var propertyType = node.IsArray ? typeof(ICollection<>).MakeGenericType(node.Type) : node.Type;
            var mInfo = typeof(EntityFrameworkQueryableExtensions).GetMethods()
                .Where(m => m.Name == "Include")
                .First(m => m.GetGenericArguments().Length == 2)
                .MakeGenericMethod(parent.Type, propertyType);
            return mInfo;
        }

        private static MethodInfo CreateGenericIncludeWithFilterMethod(ExpressionNode parent, ExpressionNode node)
        {
            var propertyType = node.IsArray ? typeof(IEnumerable<>).MakeGenericType(node.Type) : node.Type;
            var mInfo = Type.GetType("EntityFrameworkCore.IncludeFilter.QueryableExtensions,EntityFrameworkCore.IncludeFilter").GetMethods()
                .Where(m => m.Name == "IncludeWithFilter")
                .First(m => m.GetGenericArguments().Length == 2)
                .MakeGenericMethod(parent.Type, propertyType);
            return mInfo;
        }

        private Expression CreateAnyExpression()
        {
            if (this.PredictExpression == null)
                return null;

            var wInfo = CreateGenericAnyMethod(this.Type);
            var lambda = Expression.Lambda(this.PredictExpression, false, this.ParameterExpression);
            var c = Expression.Property(this.Parent.ParameterExpression, this.NodeName);
            var exp = Expression.Call(null, wInfo, new Expression[] {c, lambda});

            return exp;
        }
    }
}