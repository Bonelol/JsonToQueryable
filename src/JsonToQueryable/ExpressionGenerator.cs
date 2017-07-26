using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query.Internal;
using JsonToQueryable.Extensions;
using Newtonsoft.Json.Linq;

namespace JsonToQueryable
{
    public class ExpressionGenerator
    {
        private readonly JObject _jObject;
        private readonly Type _rootType;

        public ExpressionGenerator(string query, Type rootType)
        {
            _jObject = JObject.Parse(query);
            _rootType = rootType;
        }

        private static MethodInfo CreateGenericWhereMethod(Type type)
        {
            var qInfo = typeof(Queryable);
            var mInfos = qInfo.GetMethods().Where(m => m.Name == "Where");
            var mInfo = mInfos.First(m => m.GetGenericArguments().Length == 1).MakeGenericMethod(type);
            return mInfo;
        }

        private static MethodInfo CreateStringContainMethod()
        {
            var qInfo = typeof(string);
            var mInfos = qInfo.GetMethods().Where(m => m.Name == "Contains");
            var mInfo = mInfos.First();
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
            var obj = ParseJsonString();
            var set = context.Set<T>();
            var q = CreateQueryInclude(set, obj.Root, hasIncludeFilter);
            q = CreateQueryWhere(q, obj.Root);

            if (obj.PageSize > 0)
            {
                q = q.Skip(obj.Page * obj.PageSize).Take(obj.PageSize);
            }

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

            foreach (var node in expressionNode.Properties.Values.Where(p => p.IsReferencedType))
            {
                var exp = node.CreateIncludeExpression2(temp.Expression, hasIncludeFilter);
                temp = queryable.Provider.CreateQuery<T>(exp);
            }

            return temp;
        }

        private IQueryable<T> CreateQueryWhere<T>(IQueryable<T> queryable, ExpressionNode node)
        {
            var predict = node.CreateWherePredictExpression();

            if (predict == null)
                return queryable;

            var wInfo = CreateGenericWhereMethod(node.Type);
            var lambda = Expression.Lambda(node.WherePredictExpression, false, node.ParameterExpression);
            var exp = Expression.Call(null, wInfo, new[] { queryable.Expression, Expression.Quote(lambda) });

            return queryable.Provider.CreateQuery<T>(exp);
        }

        private QueryableObject ParseJsonString()
        {
            var obj = new QueryableObject();

            foreach (var property in this._jObject.Properties())
            {
                var name = property.Name.ToLower();
                switch (name)
                {
                    case "query":
                    {
                        //TODO
                        obj.Root = ParseNode((JObject)property.Value, this._rootType);
                        break;
                    }
                    case "page":
                    {
                        if (property.Value.Type == JTokenType.Integer)
                        {
                            obj.Page = property.Value.ToObject<int>();
                        }
                        else
                        {
                            throw new Exception("Invalid page value");
                        }
                        break;
                    }
                    case "pagesize":
                    {
                        if (property.Value.Type == JTokenType.Integer)
                        {
                            obj.PageSize = property.Value.ToObject<int>();
                        }
                        else
                        {
                            throw new Exception("Invalid pageSize value");
                        }
                        break;
                    }
                    default:
                        throw new NotSupportedException();
                }
            }

            return obj;
        }

        private ExpressionNode ParseNode(JObject jObject, Type type, string name = null, ExpressionNode parent = null)
        {
            var node = new ExpressionNode(type, name, parent);


            foreach (var property in jObject.Properties())
            {
                ExpressionNode childNode;

                var propertyType = node.Type.GetProperty(property.Name).PropertyType;
                if (property.Value.Type == JTokenType.Object)
                {
                    childNode = ParseNode((JObject)property.Value, propertyType, property.Name, node);
                }
                else
                {
                    var expression = ParseToLambdaExpression(node.ParameterExpression, property.Name, property.Value.ToString());
                    childNode = new ExpressionNode(propertyType, property.Name, node)
                    {
                        ComputedExpression = expression
                    };
                }

                node.Properties.Add(property.Name, childNode);
            }

            return node;
        }

        private Expression ParseToLambdaExpression(ParameterExpression parameter, string property, string queryString)
        {
            Expression expression = null;
            var parse = Parser.Parse(new Source(queryString));


            while (true)
            {
                Expression temp;
                var operation = parse.Next();

                if (operation.Kind == TokenKind.EOF || operation.Kind == TokenKind.COMMA || operation.Kind == TokenKind.BRACE_L || operation.Kind == TokenKind.BRACE_R)
                {
                    break;
                }

                TokenKind? oo = null;

                //if && ||
                if (operation.Kind == TokenKind.AND || operation.Kind == TokenKind.OR)
                {
                    oo = operation.Kind;
                    operation = parse.Next();
                }

                switch (operation.Kind)
                {
                    case TokenKind.LESSTHAN:
                    {
                        var compare = LambdaCompare.LessThan;
                        var n = parse.Next();
                        if (n.Kind == TokenKind.EQUALS)
                        {
                            n = parse.Next();
                            compare = LambdaCompare.LessThanOrEqual;
                        }

                        var v = n.Value.ToInt32();
                        temp = CreateLambdaExpression(parameter, property, v, compare);
                    }
                        break;
                    case TokenKind.GREATERTHAN:
                    {
                        var compare = LambdaCompare.GreaterThan;
                        var n = parse.Next();
                        if (n.Kind == TokenKind.EQUALS)
                        {
                            n = parse.Next();
                            compare = LambdaCompare.GreaterThanOrEqual;
                        }

                        var v = n.Value.ToInt32();
                        temp = CreateLambdaExpression(parameter, property, v, compare);
                    }
                        break;
                    case TokenKind.EQUALS:
                    {
                        var n = parse.Next();
                        var v = n.Value.ToInt32();
                        temp = CreateLambdaExpression(parameter, property, v, LambdaCompare.Equal);
                    }
                        break;
                    case TokenKind.INT:
                    {
                        var v = operation.Value.ToInt32();
                        temp = CreateLambdaExpression(parameter, property, v, LambdaCompare.Equal);
                    }
                        break;
                    case TokenKind.STRING:
                    {
                        var v = operation.Value;
                        temp = CreateLambdaExpression(parameter, property, v, LambdaCompare.Equal);
                    }
                        break;
                    case TokenKind.BOOLEAN:
                    {
                        var v = operation.Value.ToBoolean();
                        temp = CreateLambdaExpression(parameter, property, v, LambdaCompare.Equal);
                    }
                        break;
                    case TokenKind.NAME:
                    {
                        var operationName = operation.Value.ToLower();
                        switch (operationName)
                        {
                            case "$contains":
                                operation = parse.Next();
                                var v = operation.Value;
                                temp = CreateLambdaExpression(parameter, property, v, LambdaCompare.Contains);
                                break;
                            default:
                                throw new NotSupportedException(operationName);
                        }
                        break;
                    }
                    case TokenKind.DOLLAR:
                    {
                        var operationName = parse.Next().Value.ToLower();
                        switch (operationName)
                        {
                            case "contains":
                                operation = parse.Next();
                                var v = operation.Value;
                                temp = CreateLambdaExpression(parameter, property, v, LambdaCompare.Contains);
                                break;
                            default:
                                throw new NotSupportedException(operationName);
                        }
                        break;
                    }
                    default:
                        throw new NotImplementedException();
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
                case LambdaCompare.Contains:
                    return Expression.Call(Expression.Property(parameter, property), CreateStringContainMethod(), Expression.Constant(value));
                case LambdaCompare.Unknown:
                default:
                    throw new ArgumentOutOfRangeException(nameof(compare), compare, null);
            }
        }
    }
}
