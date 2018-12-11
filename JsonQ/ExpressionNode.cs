using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace JsonQ
{
    public class ExpressionNode
    {
        internal ExpressionNode Parent { get; }
        internal ExpressionNode Root => this.Parent == null ? this : this.Parent.Root;
        internal int Depth { get; }
        internal string NodeName { get; }
        internal bool IsEnumerable { get; }
        internal Dictionary<string, ExpressionNode> Properties { get; }
        internal bool ShouldInclude { get; }
        internal Type Type { get; }
        internal ParameterExpression ParameterExpression { get; }
        internal Expression ComputedExpression { get; }
        /// <summary>
        /// For where clause
        /// </summary>
        internal Expression WherePredictExpression { get; private set; }

        internal ExpressionNode(Type type, string name, bool isEnumerable, bool include, ExpressionNode parent, Expression computedExpression = null)
        {
            Type = type;
            NodeName = name;
            ShouldInclude = include;
            Parent = parent;
            ComputedExpression = computedExpression;
            ParameterExpression = Expression.Parameter(type, type.Name);
            IsEnumerable = isEnumerable;
            Properties = new Dictionary<string, ExpressionNode>();
            Depth = parent?.Depth + 1 ?? 0;
        }

        internal Expression CreateWherePredictExpression()
        {
            //create where for object's properties first
            foreach (var subNode in this.Properties.Values)
            {
                if (subNode.ShouldInclude)
                {
                    var expr = subNode.CreateWherePredictExpression();
                    subNode.WherePredictExpression = subNode.IsEnumerable ? subNode.CreateAnyExpression() : expr;
                }
                else
                {
                    subNode.WherePredictExpression = subNode.ComputedExpression;
                }
            }

            var predicts = this.Properties.Values.Where(p => p.WherePredictExpression != null).Select(p => p.WherePredictExpression).ToList();
            return predicts.Any() ? this.WherePredictExpression = predicts.Aggregate(Expression.AndAlso) : null;
        }

        internal Expression CreateIncludeFilterPredictExpression()
        {
            var predicts = this.Properties.Values.Where(n => !n.ShouldInclude && n.ComputedExpression != null).Select(n => n.ComputedExpression).ToList();
            return predicts.Any() ? this.WherePredictExpression = predicts.Aggregate(Expression.AndAlso) : null;
        }

        internal Expression CreateIncludeExpression(Expression pExpression, bool hasncludeFilter)
        {
            //if this is the end of node chain
            if (!this.Properties.Values.Any(p => p.ShouldInclude))
            {
                var exp = CreateIncludeMethodCallExpression(pExpression, hasncludeFilter);
                return this.Properties.Values.Where(v => v.ShouldInclude).Aggregate(exp, (expression, node) => (MethodCallExpression)node.CreateIncludeExpression(expression, hasncludeFilter));
            }

            var result = pExpression;

            foreach (var node in this.Properties.Values.Where(p => p.ShouldInclude))
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
            var list = this.Properties.Values.Where(p => !p.ShouldInclude && p.ComputedExpression != null).Select(p => p.ComputedExpression).ToList();

            if (hasncludeFilter && list.Any())
            {
                var w = MethodCreator.CreateGenericEnumerableWhereMethod(this.Type);
                var whereExpr = list.Aggregate(Expression.AndAlso);
                var l = Expression.Lambda(whereExpr, false, this.ParameterExpression);
                var c = Expression.Property(this.Parent.ParameterExpression, this.NodeName);
                includePropertyExpr = Expression.Call(null, w, new Expression[] { c, l });

                var icollection = typeof(ICollection<>).MakeGenericType(this.Type);
                includePropertyExpr = Expression.Convert(includePropertyExpr, icollection);
                include = this.Depth > 1 ? MethodCreator.CreateGenericThenIncludeWithFilterMethod(this.Parent, this) : MethodCreator.CreateGenericIncludeWithFilterMethod(this.Parent, this);
            }
            else
            {
                includePropertyExpr = Expression.Property(this.Parent.ParameterExpression, this.NodeName);
                include = this.Depth > 1 ? MethodCreator.CreateGenericThenIncludeMethod(this.Parent, this) : MethodCreator.CreateGenericIncludeMethod(this.Parent, this);
            }

            var lambda = Expression.Lambda(includePropertyExpr, false, this.Parent.ParameterExpression);
            var exp = Expression.Call(null, include, new[] { pExpression, Expression.Quote(lambda) });
            return exp;
        }

        private Expression CreateAnyExpression()
        {
            if (this.WherePredictExpression == null)
                return null;

            var wInfo = MethodCreator.CreateGenericAnyMethod(this.Type);
            var lambda = Expression.Lambda(this.WherePredictExpression, false, this.ParameterExpression);
            var c = Expression.Property(this.Parent.ParameterExpression, this.NodeName);
            var exp = Expression.Call(null, wInfo, new Expression[] {c, lambda});

            return exp;
        }
    }
}