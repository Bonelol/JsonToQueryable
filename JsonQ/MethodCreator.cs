using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace JsonQ
{
    internal static class MethodCreator
    {
        internal const string ANY = "Any";
        internal const string WHERE = "Where";
        internal const string SELECT = "Select";
        internal const string CONTAINS = "Contains";
        internal const string INCLUDE = "Include";
        internal const string THEN_INCLUDE = "ThenInclude";
        internal const string INCLUDE_WITH_FILTER = "IncludeWithFilter";
        internal const string THEN_INCLUDE_WITH_FILTER = "ThenIncludeWithFilter";

        internal static MethodInfo CreateGenericIncludeMethod(ExpressionNode parent, ExpressionNode node, Type collectionType)
        {
            var propertyType = node.IsEnumerable ? collectionType.MakeGenericType(node.Type) : node.Type;
            var mInfo = typeof(EntityFrameworkQueryableExtensions).GetMethods()
                .Where(m => m.Name == INCLUDE)
                .First(m => m.GetGenericArguments().Length == 2)
                .MakeGenericMethod(parent.Type, propertyType);
            return mInfo;
        }

        internal static MethodInfo CreateGenericThenIncludeMethod(ExpressionNode parent, ExpressionNode node, Type collectionType)
        {
            var propertyType = node.IsEnumerable ? collectionType.MakeGenericType(node.Type) : node.Type;
            var mInfo = typeof(EntityFrameworkQueryableExtensions).GetMethods()
                .Where(m => m.Name == THEN_INCLUDE).ElementAt(parent.IsEnumerable ? 0 : 1)
                .MakeGenericMethod(parent.Root.Type, parent.Type, propertyType);
            return mInfo;
        }

        internal static MethodInfo CreateGenericIncludeWithFilterMethod(ExpressionNode parent, ExpressionNode node)
        {
            var propertyType = node.IsEnumerable ? typeof(ICollection<>).MakeGenericType(node.Type) : node.Type;
            var mInfo = Type.GetType("EntityFrameworkCore.IncludeFilter.QueryableExtensions,EntityFrameworkCore.IncludeFilter")?.GetMethods()
                .Where(m => m.Name == INCLUDE_WITH_FILTER)
                .First(m => m.GetGenericArguments().Length == 2)
                .MakeGenericMethod(parent.Type, propertyType);
            return mInfo;
        }

        internal static MethodInfo CreateGenericThenIncludeWithFilterMethod(ExpressionNode parent, ExpressionNode node)
        {
            var propertyType = node.IsEnumerable ? typeof(ICollection<>).MakeGenericType(node.Type) : node.Type;
            var mInfo = Type.GetType("EntityFrameworkCore.IncludeFilter.QueryableExtensions,EntityFrameworkCore.IncludeFilter")?.GetMethods()
                .Where(m => m.Name == THEN_INCLUDE_WITH_FILTER)
                .First(m => m.GetGenericArguments().Length == 3)
                .MakeGenericMethod(parent.Root.Type, parent.Type, propertyType);
            return mInfo;
        }

        internal static MethodInfo CreateGenericQueryableWhereMethod(Type type)
        {
            var qInfo = typeof(Queryable);
            var mInfos = qInfo.GetMethods().Where(m => m.Name == WHERE);
            var mInfo = mInfos.First(m => m.GetGenericArguments().Length == 1).MakeGenericMethod(type);
            return mInfo;
        }

        internal static MethodInfo CreateStringContainMethod()
        {
            var qInfo = typeof(string);
            var mInfos = qInfo.GetMethods().Where(m => m.Name == CONTAINS);
            var mInfo = mInfos.First();
            return mInfo;
        }

        internal static MethodInfo CreateGenericSelectMethod(Type type)
        {
            var qInfo = typeof(Queryable);
            var mInfos = qInfo.GetMethods().Where(m => m.Name == SELECT);
            var mInfo = mInfos.First(m => m.GetGenericArguments().Length == 2).MakeGenericMethod(type, type);
            return mInfo;
        }


        internal static MethodInfo CreateGenericAnyMethod(Type type)
        {
            var qInfo = typeof(Enumerable);
            var mInfos = qInfo.GetMethods().Where(m => m.Name == MethodCreator.ANY);
            var mInfo = mInfos.First(m => m.GetGenericArguments().Length == 1 && m.GetParameters().Length == 2).MakeGenericMethod(type);
            return mInfo;
        }

        internal static MethodInfo CreateGenericEnumerableWhereMethod(Type type)
        {
            var qInfo = typeof(Enumerable);
            var mInfos = qInfo.GetMethods().Where(m => m.Name == MethodCreator.WHERE);
            var mInfo = mInfos.First(m => m.GetGenericArguments().Length == 1 && m.GetParameters().Length == 2).MakeGenericMethod(type);
            return mInfo;
        }
    }
}
