using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace OnlineContract.Helpers
{
    public record SortSpec(string By, bool Desc);

    public static class QuerySorting
    {
        public static IQueryable<T> ApplySort<T>(this IQueryable<T> query,
            SortSpec? sort,
            IReadOnlyDictionary<string, Expression<Func<T, object?>>> map,
            Expression<Func<T, object?>> stableKey)
        {
            if (sort == null || string.IsNullOrWhiteSpace(sort.By) || map == null || !map.ContainsKey(sort.By))
            {
                return query;
            }

            var key = map[sort.By];

            try
            {
                if (sort.Desc)
                {
                    var ordered = Queryable.OrderByDescending(query, (dynamic)key);
                    return Queryable.ThenByDescending((IOrderedQueryable<T>)ordered, (dynamic)stableKey);
                }
                else
                {
                    var ordered = Queryable.OrderBy(query, (dynamic)key);
                    return Queryable.ThenBy((IOrderedQueryable<T>)ordered, (dynamic)stableKey);
                }
            }
            catch
            {
                // Fallback to original query when dynamic ordering fails for any reason
                return query;
            }
        }
    }
}
