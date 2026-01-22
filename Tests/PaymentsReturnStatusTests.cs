using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using OnlineContract.Data;
using OnlineContract.Models;
using Xunit;

namespace OnlineContract.Tests
{
    public class PaymentsReturnStatusTests : IClassFixture<WebAppFactory>
    {
        private readonly WebAppFactory _factory;
        public PaymentsReturnStatusTests(WebAppFactory factory) { _factory = factory; }

        [Fact]
        public async System.Threading.Tasks.Task Online_Submit_Returns_Redirect_And_Status_Pending_Until_Callback()
        {
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var contract = new Contract { Id = 930, InputUserId = 123, ContractState = OnlineContract.Helpers.ContractState.Draft, EntryDate = System.DateTime.Now, Amount = 0m, IsActive = true, IsDeleted = false, Stamp = 0 };
                db.Contracts.Add(contract);
                var det = new ContractDet { Id = 931, ContractId = 930, ProductVariantId = 101, Quantity = 1, Amount = 1500m, ProductName = "T-Shirt", Size = "M", Color = "Red", ItemStateId = OnlineContract.Helpers.ProductStateInOrder.Draft, InputDt = System.DateTime.Now, InputUserId = 123, Stamp = 0, IsActive = true, IsDeleted = false };
                db.ContractDets.Add(det);
                await db.SaveChangesAsync();
            }
            var (client, authCookie) = await _factory.CreateAuthenticatedClientAsync("testuser", "Password1");
            if (!string.IsNullOrEmpty(authCookie)) client.DefaultRequestHeaders.Add("Cookie", ExtractCookie(authCookie!, ".OnlineContract.Auth"));

            var resp = await client.PostAsync("/api/contracts/930/submit?method=online", null);
            Assert.True(resp.IsSuccessStatusCode);
            var j = await resp.Content.ReadFromJsonAsync<SubmitDto>();
            Assert.NotNull(j);
            Assert.True(j!.success);
            Assert.False(string.IsNullOrWhiteSpace(j.redirectUrl));
            Assert.False(string.IsNullOrWhiteSpace(j.reference));

            // Status initially Pending/Unknown
            var status = await client.GetFromJsonAsync<StatusDto>($"/api/payments/status?reference={j.reference}");
            Assert.NotNull(status);
            Assert.True(status!.status == "Pending" || status.status == "Unknown" || status.status == null);

            // After success callback, status shows Succeeded and contract toggles to Submitted
            var cb = await client.PostAsJsonAsync("/api/payments/wspay/callback", new { status = "success", contractId = 930 });
            Assert.True(cb.IsSuccessStatusCode);
            var status2 = await client.GetFromJsonAsync<StatusDto>($"/api/payments/status?reference={j.reference}");
            Assert.NotNull(status2);
            Assert.Equal("Succeeded", status2!.status);

            using (var scope2 = _factory.Services.CreateScope())
            {
                var db = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
                var c = await db.Contracts.AsNoTracking().SingleAsync(x => x.Id == 930);
                Assert.Equal(OnlineContract.Helpers.ContractState.Submitted, c.ContractState);
            }
        }

        private static string ExtractCookie(string setCookieHeader, string cookieName)
        {
            var parts = setCookieHeader.Split(';');
            var nv = parts[0];
            if (nv.StartsWith(cookieName + "=")) return nv;
            return nv;
        }
    }

    file sealed class SubmitDto { public bool success { get; set; } public string redirectUrl { get; set; } = string.Empty; public string reference { get; set; } = string.Empty; }
    file sealed class StatusDto { public string? status { get; set; } public int contractId { get; set; } }
}
