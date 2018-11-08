using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;

namespace JsonToQueryable.Extensions
{
    public static class DbContextExtensions
    {
        public static IQueryable CreateQuery(this DbContext context, string queryString, List<Type> types)
        {
            return new JsonQueryableParser(queryString, types).ParseJsonString().CreateQuery(context);
        }

        public static IQueryable CreateQuery(this DbContext context, JObject jObject, List<Type> types)
        {
            return new JsonQueryableParser(jObject, types).ParseJsonString().CreateQuery(context);
        }
    }
}
