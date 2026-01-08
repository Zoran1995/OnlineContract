using Microsoft.EntityFrameworkCore;
using OnlineContract.Data;

namespace OnlineContract.Helpers
{
    public static class LookupHelper
    {
        public static async Task<string> GetLookupValueAsync(AppDbContext db, int lookupSetId)
        {
            try
            {
                // In tests (SQLite), there is no SQL function; return enum-based mapping
                if (db.Database.IsSqlite())
                {
                    return MapEnumName(lookupSetId);
                }

                var result = await db.Database
                    .SqlQueryRaw<string>("SELECT dbo.fn_get_lookup_value({0}) AS Value", lookupSetId)
                    .FirstOrDefaultAsync();
                return result ?? MapEnumName(lookupSetId);
            }
            catch
            {
                return MapEnumName(lookupSetId);
            }
        }

        private static string MapEnumName(int id)
        {
            // Map known lookup IDs to human-readable names based on GlobalEnums
            switch (id)
            {
                // ContractState
                case (int)ContractState.Draft: return nameof(ContractState.Draft);
                case (int)ContractState.Submitted: return nameof(ContractState.Submitted);
                case (int)ContractState.Accepted: return nameof(ContractState.Accepted);
                case (int)ContractState.PartiallyAccepted: return nameof(ContractState.PartiallyAccepted);
                case (int)ContractState.Rejected: return nameof(ContractState.Rejected);
                case (int)ContractState.InProgress: return nameof(ContractState.InProgress);
                case (int)ContractState.Completed: return nameof(ContractState.Completed);
                case (int)ContractState.Dispatched: return nameof(ContractState.Dispatched);
                case (int)ContractState.Delivered: return nameof(ContractState.Delivered);
                case (int)ContractState.Returned: return nameof(ContractState.Returned);
                case (int)ContractState.Cancelled: return nameof(ContractState.Cancelled);
                case (int)ContractState.WrittenOff: return nameof(ContractState.WrittenOff);

                // ProductStateInOrder
                case (int)ProductStateInOrder.Draft: return nameof(ProductStateInOrder.Draft);
                case (int)ProductStateInOrder.Submitted: return nameof(ProductStateInOrder.Submitted);
                case (int)ProductStateInOrder.Accepted: return nameof(ProductStateInOrder.Accepted);
                case (int)ProductStateInOrder.Rejected: return nameof(ProductStateInOrder.Rejected);
            }
            return "Unknown";
        }
    }
}