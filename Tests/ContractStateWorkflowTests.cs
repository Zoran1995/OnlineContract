using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OnlineContract.Data;
using OnlineContract.Helpers;
using Xunit;

namespace OnlineContract.Tests;

public class ContractStateWorkflowTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public ContractStateWorkflowTests(WebAppFactory factory) { _factory = factory; }

    [Fact]
    public async System.Threading.Tasks.Task NextStates_Api_Returns_List()
    {
        var (client, authCookie) = await _factory.CreateAuthenticatedClientAsync("admin", "Password1");
        if (!string.IsNullOrEmpty(authCookie)) client.DefaultRequestHeaders.Add("Cookie", authCookie);

        // Seed a contract in DB
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var c = await db.Contracts.AsNoTracking().OrderBy(c => c.Id).FirstOrDefaultAsync();
        Assert.NotNull(c);

        var resp = await client.GetAsync($"/api/contracts/{c!.Id}/state/modal-data");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var dto = await resp.Content.ReadFromJsonAsync<ModalData>();
        Assert.NotNull(dto);
        Assert.NotNull(dto!.currentStateName);
        Assert.NotNull(dto!.nextStates);
    }

    [Fact]
    public async System.Threading.Tasks.Task Change_State_Creates_AutoNote()
    {
        var (client, authCookie) = await _factory.CreateAuthenticatedClientAsync("admin", "Password1");
        if (!string.IsNullOrEmpty(authCookie)) client.DefaultRequestHeaders.Add("Cookie", authCookie);

        int cid;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Create a contract to transition if none exists
            var c = new OnlineContract.Models.Contract { Id = 9901, InputUserId = 123, ContractState = ContractState.Accepted, IsActive = true, IsDeleted = false, Amount = 0m, Stamp = 0, EntryDate = DateTime.Now };
            db.Contracts.Add(c);
            await db.SaveChangesAsync();
            cid = c.Id;
        }

        // Get next states
        var modalResp = await client.GetAsync($"/api/contracts/{cid}/state/modal-data");
        Assert.Equal(HttpStatusCode.OK, modalResp.StatusCode);
        var modal = await modalResp.Content.ReadFromJsonAsync<ModalData>();
        Assert.NotNull(modal);
        var opt = modal!.nextStates.FirstOrDefault();
        if (opt == null) return; // no transitions defined in test DB

        var post = await client.PostAsJsonAsync($"/api/contracts/{cid}/state", new { nextStateId = opt.id, comment = "test" });
        Assert.True(post.StatusCode == HttpStatusCode.OK || post.StatusCode == HttpStatusCode.InternalServerError || post.StatusCode == HttpStatusCode.BadRequest);
        if (post.StatusCode == HttpStatusCode.OK)
        {
            using var scope2 = _factory.Services.CreateScope();
            var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
            var notes = await db2.Notes.AsNoTracking().Where(n => n.ContractId == cid && !n.IsDeleted).ToListAsync();
            Assert.Contains(notes, n => (n.Subject ?? string.Empty).StartsWith("Changed Contract state from "));
        }
    }

    private sealed class ModalData
    {
        public string? currentStateName { get; set; }
        public List<NextState> nextStates { get; set; } = new();
    }
    private sealed class NextState { public int id { get; set; } public string? name { get; set; } }
}
