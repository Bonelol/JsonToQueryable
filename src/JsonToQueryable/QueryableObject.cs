using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace JsonToQueryable
{
    public class QueryableObject
    {
        public ExpressionNode Root { get; set; }
        public int Page { get; set; } = 0;
        public int PageSize { get; set; } = 0;
    }
}
