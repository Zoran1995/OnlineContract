using System;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace OnlineContract.Infrastructure
{
    public static class UserContextHelper
    {
        public static int GetCurrentUserId(HttpContext http)
        {
            var claim = http.User?.FindFirst(ClaimTypes.NameIdentifier)
                ?? http.User?.FindFirst("userId")
                ?? http.User?.FindFirst("sub");
            if (claim == null) return 0;
            return int.TryParse(claim.Value, out var id) ? id : 0;
        }

        public static int? TryGetUserId(HttpContext http)
        {
            var claim = http.User?.FindFirst("sub")
                ?? http.User?.FindFirst("userId")
                ?? http.User?.FindFirst(ClaimTypes.NameIdentifier);
            if (claim == null) return null;
            return int.TryParse(claim.Value, out var id) ? id : (int?)null;
        }

        public static bool IsCustomer(HttpContext http)
        {
            try
            {
                var rc = http.User?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role || c.Type.EndsWith("/role", StringComparison.OrdinalIgnoreCase))?.Value;
                return int.TryParse(rc, out var roleId) && roleId == 5;
            }
            catch { return false; }
        }

        public static bool CanManageProducts(HttpContext http)
        {
            try
            {
                var rc = http.User?.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role || c.Type.EndsWith("/role", StringComparison.OrdinalIgnoreCase))?.Value;
                return int.TryParse(rc, out var roleId) && (roleId == 6 || roleId == 7 || roleId == 8);
            }
            catch { return false; }
        }
    }
}