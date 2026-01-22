using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OnlineContract.Data;
using OnlineContract.Helpers;
using Xunit;

namespace OnlineContract.Tests;

public class ContractStateDraftGuardTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public ContractStateDraftGuardTests(WebAppFactory factory) { _factory = factory; }

    [Fact]
    public async System.Threading.Tasks.Task Draft_Contract_State_Change_Blocked()
    {
        var (client, authCookie) = await _factory.CreateAuthenticatedClientAsync("admin", "Password1");
        if (!string.IsNullOrEmpty(authCookie)) client.DefaultRequestHeaders.Add("Cookie", authCookie);

        int cid;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var c = new OnlineContract.Models.Contract { Id = 9950, InputUserId = 123, ContractState = ContractState.Draft, IsActive = true, IsDeleted = false, Amount = 0m, Stamp = 0, EntryDate = DateTime.Now };
            db.Contracts.Add(c);
            await db.SaveChangesAsync();
            cid = c.Id;
        }

        var modalResp = await client.GetAsync($"/api/contracts/{cid}/state/modal-data");
        Assert.Equal(HttpStatusCode.OK, modalResp.StatusCode);
        var post = await client.PostAsJsonAsync($"/api/contracts/{cid}/state", new { nextStateId = (int)ContractState.Accepted, comment = "try" });
        Assert.Equal(HttpStatusCode.BadRequest, post.StatusCode);
    }
}
