using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OnlineContract.Models;

namespace OnlineContract.Data
{
    public static class UserRepository
    {
        public static async Task<AxUser?> FindByEmailAsync(this AppDbContext db, string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return null;
            var norm = email.Trim().ToLowerInvariant();
            return await db.AxUsers.FirstOrDefaultAsync(u => ((u.Email ?? "").ToLower() == norm) && !u.IsDeleted && u.IsActive);
        }
    }
}
