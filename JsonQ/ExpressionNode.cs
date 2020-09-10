using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace JsonQ
{
    public class ExpressionNode
    {
        internal int Depth { get; }
        internal string NodeName { get; }
        internal bool IsEnumerable { get; }
        internal bool ShouldInclude { get; }
        internal Type Type { get; }
        internal ExpressionNode Parent { get; }
        internal ExpressionNode Root => Parent == null ? this : Parent.Root;
        internal ParameterExpression ParameterExpression { get; }
        internal Expression ComputedExpression { get; }
        internal Dictionary<string, ExpressionNode> Properties { get; }
        internal IList<ExpressionNode> Include { get; set; }
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
            foreach (var subNode in Properties.Values)
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

            var predicts = Properties.Values.Where(p => p.WherePredictExpression != null).Select(p => p.WherePredictExpression).ToList();
            return predicts.Any() ? WherePredictExpression = predicts.Aggregate(Expression.AndAlso) : null;
        }

        internal Expression CreateIncludeExpression(Expression pExpression, bool hasncludeFilter)
        {
            //if this is the end of node chain
            if (Include == null || Include.Count == 0)
            {
                var exp = CreateIncludeMethodCallExpression(pExpression, hasncludeFilter);
                return Properties.Values.Where(v => v.ShouldInclude).Aggregate(exp, (expression, node) => (MethodCallExpression)node.CreateIncludeExpression(expression, hasncludeFilter));
            }

            var result = pExpression;

            foreach (var node in Properties.Values.Where(p => p.ShouldInclude))
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
            var list = Properties.Values.Where(p => !p.ShouldInclude && p.ComputedExpression != null).Select(p => p.ComputedExpression).ToList();

            if (hasncludeFilter && list.Any())
            {
                var w = MethodCreator.CreateGenericEnumerableWhereMethod(Type);
                var whereExpr = list.Aggregate(Expression.AndAlso);
                var l = Expression.Lambda(whereExpr, false, ParameterExpression);
                var c = Expression.Property(Parent.ParameterExpression, NodeName); 
                includePropertyExpr = Expression.Call(null, w, new Expression[] { c, l });
                include = Depth > 1 ? MethodCreator.CreateGenericThenIncludeMethod(Parent, this, typeof(IEnumerable<>)) : MethodCreator.CreateGenericIncludeMethod(Parent, this, typeof(IEnumerable<>));
            }
            else
            {
                includePropertyExpr = Expression.Property(Parent.ParameterExpression, NodeName);
                include = Depth > 1 ? MethodCreator.CreateGenericThenIncludeMethod(Parent, this, typeof(ICollection<>)) : MethodCreator.CreateGenericIncludeMethod(Parent, this, typeof(ICollection<>));
            }

            var lambda = Expression.Lambda(includePropertyExpr, false, Parent.ParameterExpression);
            var exp = Expression.Call(null, include, new[] { pExpression, Expression.Quote(lambda) });
            return exp;
        }

        private Expression CreateAnyExpression()
        {
            if (WherePredictExpression == null)
                return null;

            var wInfo = MethodCreator.CreateGenericAnyMethod(Type);
            var lambda = Expression.Lambda(WherePredictExpression, false, ParameterExpression);
            var c = Expression.Property(Parent.ParameterExpression, NodeName);
            var exp = Expression.Call(null, wInfo, new Expression[] {c, Expression.Quote(lambda) });

            return exp;
        }
    }
}