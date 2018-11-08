﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using JsonToQueryable.Extensions;
using Newtonsoft.Json.Linq;

namespace JsonToQueryable
{
    public class JsonQueryableParser
    {
        private readonly JObject _jObject;
        private readonly List<Type> _includedTypes;
        private readonly List<Type> _excludedTypes;
        internal QueryableCreator QueryableObject { get; private set; }

        public JsonQueryableParser(JObject jObject, List<Type> includedTypes)
        {
            _jObject = jObject;
            _includedTypes = includedTypes;
        }

        public JsonQueryableParser(string query, List<Type> includedTypes)
        {
            _jObject = JObject.Parse(query);
            _includedTypes = includedTypes;
        }

        internal QueryableCreator ParseJsonString()
        {
            var obj = new QueryableCreator();
            Type rootType = null;
            JProperty rootJProperty = null;

            foreach (var property in this._jObject.Properties())
            {
                var name = property.Name.ToLower();
                switch (name)
                {
                    case "type":
                    {
                        rootType = _includedTypes.First(t=> t.FullName != null && t.FullName.ToLower().Contains(property.Value.ToObject<string>().ToLower()));
                        break;
                    }
                    case "query":
                    {
                        rootJProperty = property;
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

            if (rootType == null)
            {
                throw new Exception("Unable to find property name 'type'.");
            }

            if (rootJProperty == null)
            {
                throw new Exception("Unable to find property name 'query'.");
            }

            obj.Root = ParseNode((JObject)rootJProperty.Value, rootType, rootJProperty.Name, false, true);

            this.QueryableObject = obj;

            return obj;
        }

        private ExpressionNode ParseNode(JObject jObject, Type type, string name, bool isEnumerable, bool include, ExpressionNode parent = null)
        {
            var node = new ExpressionNode(type, name, include, isEnumerable, parent);

            foreach (var property in jObject.Properties())
            {
                ExpressionNode childNode;

                var propertyType = node.Type.GetProperty(property.Name).PropertyType;
                //check is enumerable
                var childNodeisEnumerable = typeof(IEnumerable).IsAssignableFrom(propertyType) && propertyType.GenericTypeArguments.Length > 0;
                //base type
                var childeType = childNodeisEnumerable ? propertyType.GenericTypeArguments[0] : propertyType;

                switch (property.Value.Type)
                {
                    case JTokenType.Object when _includedTypes.Contains(childeType):
                        childNode = ParseNode((JObject)property.Value, childeType, property.Name, childNodeisEnumerable, true, node);
                        break;
                    case JTokenType.Object:
                    case JTokenType.Comment:
                        continue;
                    default:
                        var expression = CreateLambdaExpression(node.ParameterExpression, property.Name, property.Value.ToString());
                        childNode = new ExpressionNode(childeType, property.Name, false, false, node, expression);
                        break;
                }

                node.Properties.Add(property.Name, childNode);
            }

            return node;
        }

        private static Expression CreateLambdaExpression(ParameterExpression parameter, string property, string queryString)
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

                TokenKind? kind = null;

                //if && ||
                if (operation.Kind == TokenKind.AND || operation.Kind == TokenKind.OR)
                {
                    kind = operation.Kind;
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

                if (kind != null && expression != null)
                {
                    switch (kind)
                    {
                        case TokenKind.AND:
                            temp = Expression.AndAlso(expression, temp);
                            break;
                        case TokenKind.OR:
                            temp = Expression.OrElse(expression, temp);
                            break;
                        default:
                            throw new Exception(kind.ToString());
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
                    return Expression.Call(Expression.Property(parameter, property), MethodCreator.CreateStringContainMethod(), Expression.Constant(value));
                case LambdaCompare.Unknown:
                default:
                    throw new ArgumentOutOfRangeException(nameof(compare), compare, null);
            }
        }
    }
}