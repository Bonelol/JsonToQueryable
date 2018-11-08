using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace JsonToQueryable
{
    public enum LambdaCompare
    {
        Unknown = 0,
        LessThan = 1,
        LessThanOrEqual = 2,
        GreaterThan = 3,
        GreaterThanOrEqual = 4,
        Equal = 5,
        NotEqual = 6,
        Contains = 7
    }
}
