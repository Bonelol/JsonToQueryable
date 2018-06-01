using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace JsonToQueryable
{
    public class QueryableCreator
    {
        public ExpressionNode Root { get; set; }
        public int Page { get; set; } = 0;
        public int PageSize { get; set; } = 0;

        private static readonly MethodInfo _createQueryMethodInfo = typeof(QueryableCreator).GetMethod("CreateQuery", new [] {typeof(DbContext)});

        public IQueryable CreateQuery(DbContext context, Type type = null)
        {
            return _createQueryMethodInfo.MakeGenericMethod(type ?? this.Root.Type).Invoke(this, new object[]{ context }) as IQueryable;
        }

        public IQueryable<T> CreateQuery<T>(DbContext context) where T : class
        {
            const bool hasIncludeFilter = false;//context.GetService<IQueryCompiler>().GetType().Name == "ReplaceQueryCompiler";

            var set = context.Set<T>();
            var q = CreateQueryInclude(set, Root, hasIncludeFilter);
            q = CreateQueryWhere(q, Root);

            if (PageSize > 0)
            {
                q = q.Skip(Page * PageSize).Take(PageSize);
            }

            //if (n.Properties.Count <= 0)
            //    return q;

            //q = CreateQuerySelect(q, _rootType, n);

            return q;
        }

        private static IQueryable<T> CreateQueryInclude<T>(IQueryable<T> queryable, ExpressionNode expressionNode, bool hasIncludeFilter)
        {
            if (expressionNode.Properties.Count == 0)
                return queryable;

            var temp = queryable;

            foreach (var node in expressionNode.Properties.Values.Where(p => p.ShouldInclude))
            {
                var exp = node.CreateIncludeExpression(temp.Expression, hasIncludeFilter);
                temp = queryable.Provider.CreateQuery<T>(exp);
            }

            return temp;
        }

        private static IQueryable<T> CreateQueryWhere<T>(IQueryable<T> queryable, ExpressionNode node)
        {
            var predict = node.CreateWherePredictExpression();

            if (predict == null)
                return queryable;

            var wInfo = MethodCreator.CreateGenericQueryableWhereMethod(node.Type);
            var lambda = Expression.Lambda(node.WherePredictExpression, false, node.ParameterExpression);
            var exp = Expression.Call(null, wInfo, new[] { queryable.Expression, Expression.Quote(lambda) });

            return queryable.Provider.CreateQuery<T>(exp);
        }
    }
}
