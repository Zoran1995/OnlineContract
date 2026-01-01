using Microsoft.EntityFrameworkCore;
using OnlineContract.Data;

namespace OnlineContract.Helpers
{
    public static class LookupHelper
    {
        public static async Task<string> GetLookupValueAsync(AppDbContext db, int lookupSetId)
        {
            var result = await db.Database
                .SqlQueryRaw<string>("SELECT dbo.fn_get_lookup_value({0})", lookupSetId)
                .FirstOrDefaultAsync();

            return result ?? "Unknown";
        }
    }
}