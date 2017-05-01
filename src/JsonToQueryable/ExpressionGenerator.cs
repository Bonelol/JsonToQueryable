using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query.Internal;
using JsonToQueryable.Extensions;

namespace JsonToQueryable
{
    public class ExpressionGenerator
    {
        private readonly Parser _parser;
        private readonly Type _rootType;

        public ExpressionGenerator(string query, Type rootType)
        {
            _parser = Parser.Parse(new Source(query));
            _rootType = rootType;
        }

        private static MethodInfo CreateGenericWhereMethod(Type type)
        {
            var qInfo = typeof(Queryable);
            var mInfos = qInfo.GetMethods().Where(m => m.Name == "Where");
            var mInfo = mInfos.First(m => m.GetGenericArguments().Length == 1).MakeGenericMethod(type);
            return mInfo;
        }

        private static MethodInfo CreateGenericSelectMethod(Type type)
        {
            var qInfo = typeof(Queryable);
            var mInfos = qInfo.GetMethods().Where(m => m.Name == "Select");
            var mInfo = mInfos.First(m => m.GetGenericArguments().Length == 2).MakeGenericMethod(type, type);
            return mInfo;
        }

        public IQueryable<T> CreateQuery<T>(DbContext context) where T : class
        {
            var hasIncludeFilter = context.GetService<IQueryCompiler>().GetType().Name == "ReplaceQueryCompiler";
            var n = ParseNode(_rootType);
            var set = context.Set<T>();
            var q = CreateQueryWhere(set, n);
            q = CreateQueryInclude(q, n, hasIncludeFilter);

            //if (n.Properties.Count <= 0)
            //    return q;

            //q = CreateQuerySelect(q, _rootType, n);

            return q;
        }

        private IQueryable<T> CreateQueryInclude<T>(IQueryable<T> queryable, ExpressionNode expressionNode, bool hasIncludeFilter)
        {
            if (expressionNode.Properties.Count == 0)
                return queryable;

            var temp = queryable;

            foreach (var node in expressionNode.Properties.Values.Where(p=>p.IsReferencedType))
            {
                var exp = node.CreateIncludeExpression(temp, hasIncludeFilter);
                temp = queryable.Provider.CreateQuery<T>(exp);
            }

            return temp;
        }

        private IQueryable<T> CreateQueryWhere<T>(IQueryable<T> queryable, ExpressionNode node)
        {
            var predict = node.CreatePredictExpression();

            if (predict == null)
                return queryable;

            var wInfo = CreateGenericWhereMethod(node.Type);
            var lambda = Expression.Lambda(node.PredictExpression, false, node.ParameterExpression);
            var exp = Expression.Call(null, wInfo, new[] { queryable.Expression, Expression.Quote(lambda) });

            return queryable.Provider.CreateQuery<T>(exp);
        }

        private ExpressionNode ParseNode(Type type, string  name = null, ExpressionNode parent = null)
        {
            var node = new ExpressionNode(type, name, parent);

            while (true)
            {
                var oper = _parser.Next();

                if (oper.Kind == TokenKind.BRACE_L)
                {

                }
                else if (oper.Kind == TokenKind.NAME)
                {
                    ExpressionNode childNode;
                    var property = oper;
                    var propertyType = node.Type.GetProperty(property.Value).PropertyType;
                    
                    var next = _parser.Next();

                    if (next.Kind == TokenKind.COLON)
                    {
                        next = _parser.Next();
                        _parser.Back();

                        if (next.Kind == TokenKind.BRACE_L)
                        {
                            var t = propertyType;
                            childNode = ParseNode(t, property.Value, node);
                        }
                        else
                        {
                            var expression = ParseToLambdaExpression(node.ParameterExpression, property);
                            childNode = new ExpressionNode(propertyType, property.Value, node)
                            {
                                ComputedExpression = expression
                            };
                        }
                    }
                    else
                    {
                        childNode = new ExpressionNode(propertyType, property.Value, node);
                    }

                    node.Properties.Add(property.Value, childNode);
                }
                else if (oper.Kind == TokenKind.BRACE_R || oper.Kind == TokenKind.EOF)
                {
                    break;
                }
            }

            return node;
        }

        private Expression ParseToLambdaExpression(ParameterExpression parameter, Token property)
        {
            Expression expression = null;

            while (true)
            {
                Expression temp;
                var operation = _parser.Next();

                if (operation.Kind == TokenKind.COMMA || operation.Kind == TokenKind.BRACE_L || operation.Kind == TokenKind.BRACE_R)
                {
                    break;
                }
                
                TokenKind? oo = null;

                //if && ||
                if (operation.Kind == TokenKind.AND || operation.Kind == TokenKind.OR)
                {
                    oo = operation.Kind;
                    operation = _parser.Next();
                }

                if (operation.Kind == TokenKind.NAME)
                {
                    operation = _parser.Next();
                }

                switch (operation.Kind)
                {
                    case TokenKind.LESSTHAN:
                        {
                            var compare = LambdaCompare.LessThan;
                            var n = _parser.Next();
                            if (n.Kind == TokenKind.EQUALS)
                            {
                                n = _parser.Next();
                                compare = LambdaCompare.LessThanOrEqual;
                            }

                            var v = n.Value.ToInt32();
                            temp = CreateLambdaExpression(parameter, property.Value, v, compare);
                        }
                        break;
                    case TokenKind.GREATERTHAN:
                        {
                            var compare = LambdaCompare.GreaterThan;
                            var n = _parser.Next();
                            if (n.Kind == TokenKind.EQUALS)
                            {
                                n = _parser.Next();
                                compare = LambdaCompare.GreaterThanOrEqual;
                            }

                            var v = n.Value.ToInt32();
                            temp = CreateLambdaExpression(parameter, property.Value, v, compare);
                        }
                        break;
                    case TokenKind.EQUALS:
                        {
                            var n = _parser.Next();
                            var v = n.Value.ToInt32();
                            temp = CreateLambdaExpression(parameter, property.Value, v, LambdaCompare.Equal);
                        }
                        break;
                    case TokenKind.INT:
                        {
                            var v = operation.Value.ToInt32();
                            temp = CreateLambdaExpression(parameter, property.Value, v, LambdaCompare.Equal);
                        }
                        break;
                    case TokenKind.STRING:
                        {
                            var v = operation.Value;
                            temp = CreateLambdaExpression(parameter, property.Value, v, LambdaCompare.Equal);
                        }
                        break;
                    case TokenKind.BOOLEAN:
                        {
                            var v = operation.Value.ToBoolean();
                            temp = CreateLambdaExpression(parameter, property.Value, v, LambdaCompare.Equal);
                        }
                        break;
                    default:
                        throw new Exception();
                }

                if (oo != null)
                {
                    switch (oo)
                    {
                        case TokenKind.AND:
                            temp = Expression.AndAlso(expression, temp);
                            break;
                        case TokenKind.OR:
                            temp = Expression.OrElse(expression, temp);
                            break;
                        default:
                            throw new Exception(oo.ToString());
                    }
                }

                expression = temp;
            }

            return expression;
        }

        private static Expression CreateLambdaExpression(ParameterExpression parameter, string property, object value, LambdaCompare compare)
        {
            switch (compare)
            {
                case LambdaCompare.LessThan:
                    return Expression.LessThan(Expression.Property(parameter, property), Expression.Constant(value));
                case LambdaCompare.LessThanOrEqual:
                    return Expression.LessThanOrEqual(Expression.Property(parameter, property), Expression.Constant(value));
                case LambdaCompare.GreaterThan:
                    return Expression.GreaterThan(Expression.Property(parameter, property), Expression.Constant(value));
                case LambdaCompare.GreaterThanOrEqual:
                    return Expression.GreaterThanOrEqual(Expression.Property(parameter, property), Expression.Constant(value));
                case LambdaCompare.Equal:
                    return Expression.Equal(Expression.Property(parameter, property), Expression.Constant(value));
                case LambdaCompare.NotEqual:
                    return Expression.NotEqual(Expression.Property(parameter, property), Expression.Constant(value));
                case LambdaCompare.Unknown:
                default:
                    throw new ArgumentOutOfRangeException(nameof(compare), compare, null);
            }
        }
    }
}
