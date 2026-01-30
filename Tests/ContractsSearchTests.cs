using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using OnlineContract.Data;
using OnlineContract.Helpers;
using OnlineContract.Models;
using Xunit;

namespace OnlineContract.Tests
{
    public class ContractsSearchTests : IClassFixture<WebAppFactory>
    {
        private readonly WebAppFactory _factory;
        public ContractsSearchTests(WebAppFactory factory) { _factory = factory; }

        private async Task SeedLookupAsync(AppDbContext db)
        {
            if (!db.LookupSets.Any(l => l.SetName == "ContractState"))
            {
                db.LookupSets.AddRange(
                    new LookupSet { LookupSetId = (int)ContractState.Draft, SetName = "ContractState", Value = "Draft" },
                    new LookupSet { LookupSetId = (int)ContractState.Delivered, SetName = "ContractState", Value = "Delivered" }
                );
                await db.SaveChangesAsync();
            }
        }

        [Fact]
        public async Task StatusFilter_Draft_UsesLookupSetId()
        {
            var (client, cookie) = await _factory.CreateAuthenticatedClientAsync("admin", "Password1");
            client.DefaultRequestHeaders.Add("Cookie", cookie);
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await SeedLookupAsync(db);

            var user = db.AxUsers.First(u => u.Code == "admin");
            var today = DateTime.UtcNow.Date;
            // Seed one Draft and one Delivered today
            db.Contracts.Add(new Contract { Id = 9001, InputUserId = user.Id, ContractState = ContractState.Draft, EntryDate = today.AddHours(10), Amount = 10, AmtMatched = 0, IsActive = true, IsDeleted = false, Stamp = 0 });
            db.Contracts.Add(new Contract { Id = 9002, InputUserId = user.Id, ContractState = ContractState.Delivered, EntryDate = today.AddHours(11), Amount = 15, AmtMatched = 0, IsActive = true, IsDeleted = false, Stamp = 0 });
            await db.SaveChangesAsync();

            var qs = new Dictionary<string, string?>
            {
                ["state"] = "Draft",
                ["fromDate"] = today.ToString("yyyy-MM-dd"),
                ["toDate"] = today.ToString("yyyy-MM-dd"),
                ["page"] = "1",
                ["pageSize"] = "50"
            };
            var url = "/api/contracts?" + string.Join('&', qs.Select(kv => $"{kv.Key}={kv.Value}"));
            var resp = await client.GetAsync(url);
            Assert.True(resp.IsSuccessStatusCode);
            var data = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            int total = data.GetProperty("totalCount").GetInt32();
            Assert.Equal(1, total);
            var items = data.GetProperty("items");
            Assert.Equal(1, items.GetArrayLength());
        }

        [Fact]
        public async Task DateWindow_ExcludesOutsideRange()
        {
            var (client, cookie) = await _factory.CreateAuthenticatedClientAsync("admin", "Password1");
            client.DefaultRequestHeaders.Add("Cookie", cookie);
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await SeedLookupAsync(db);

            var user = db.AxUsers.First(u => u.Code == "admin");
            var day = DateTime.UtcNow.Date.AddDays(-3);
            db.Contracts.Add(new Contract { Id = 9010, InputUserId = user.Id, ContractState = ContractState.Draft, EntryDate = day, Amount = 5, IsActive = true, IsDeleted = false, Stamp = 0 });
            await db.SaveChangesAsync();

            var today = DateTime.UtcNow.Date;
            var url = $"/api/contracts?state=Draft&fromDate={today:yyyy-MM-dd}&toDate={today:yyyy-MM-dd}&page=1&pageSize=50";
            var resp = await client.GetAsync(url);
            Assert.True(resp.IsSuccessStatusCode);
            var data = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            int total = data.GetProperty("totalCount").GetInt32();
            Assert.Equal(0, total);
        }

        [Fact]
        public async Task UserSearch_MatchesFullNameAndCode_SingleJoin()
        {
            var (client, cookie) = await _factory.CreateAuthenticatedClientAsync("admin", "Password1");
            client.DefaultRequestHeaders.Add("Cookie", cookie);
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await SeedLookupAsync(db);

            var cust = new AxUser { Id = 7777, Code = "ANA1", FirstName = "Ana", LastName = "Popovic", RoleId = 5, IsActive = true, IsDeleted = false, Password = PasswordHelper.HashPassword("Password1"), OwnerId = 124, CreatedDt = DateTime.UtcNow, PasswordDt = DateTime.UtcNow, Stamp = 0 };
            db.AxUsers.Add(cust);
            db.Contracts.Add(new Contract { Id = 9020, InputUserId = cust.Id, ContractState = ContractState.Draft, EntryDate = DateTime.UtcNow, Amount = 12, IsActive = true, IsDeleted = false, Stamp = 0 });
            await db.SaveChangesAsync();

            var today = DateTime.UtcNow.Date;
            var url = $"/api/contracts?state=Draft&name=ana%20po&fromDate={today:yyyy-MM-dd}&toDate={today:yyyy-MM-dd}&page=1&pageSize=10";
            var resp = await client.GetAsync(url);
            Assert.True(resp.IsSuccessStatusCode);
            var data = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            int total = data.GetProperty("totalCount").GetInt32();
            Assert.Equal(1, total);
        }

        [Fact]
        public async Task Paging_ReturnsSecondPage()
        {
            var (client, cookie) = await _factory.CreateAuthenticatedClientAsync("admin", "Password1");
            client.DefaultRequestHeaders.Add("Cookie", cookie);
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await SeedLookupAsync(db);

            var user = db.AxUsers.First(u => u.Code == "admin");
            // Use a distinct day to avoid overlap with other tests
            var targetDay = DateTime.UtcNow.Date.AddDays(2);
            for (int i = 0; i < 15; i++)
            {
                db.Contracts.Add(new Contract { Id = 9100 + i, InputUserId = user.Id, ContractState = ContractState.Draft, EntryDate = targetDay.AddHours(1), Amount = 1 + i, IsActive = true, IsDeleted = false, Stamp = 0 });
            }
            await db.SaveChangesAsync();

            var url = $"/api/contracts?state=Draft&fromDate={targetDay:yyyy-MM-dd}&toDate={targetDay:yyyy-MM-dd}&page=2&pageSize=10";
            var resp = await client.GetAsync(url);
            Assert.True(resp.IsSuccessStatusCode);
            var data = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            int total = data.GetProperty("totalCount").GetInt32();
            Assert.Equal(15, total);
            int totalPages = data.GetProperty("totalPages").GetInt32();
            Assert.Equal(2, totalPages);
            var items = data.GetProperty("items");
            Assert.Equal(5, items.GetArrayLength());
        }
    }
}
