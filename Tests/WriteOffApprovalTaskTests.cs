using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using OnlineContract.Data;
using OnlineContract.Helpers;
using OnlineContract.Models;
using Xunit;

namespace OnlineContract.Tests;

public class WriteOffApprovalTaskTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public WriteOffApprovalTaskTests(WebAppFactory factory) { _factory = factory; }

    // Using GlobalEnums for lookup values; no DB seeding required

    private async Task<(int contractId, int contextId)> SeedContractAndRuleAsync(decimal amount, params (decimal threshold, int assignedToUserId, bool active, bool deleted)[] rules)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Lookup IDs from GlobalEnums
        var priorityHighId = (int)TaskPriority.High;
        var statusNotStartedId = (int)OnlineContract.Helpers.TaskStatus.NotStarted;
        var writeOffCtxId = (int)ApprovalRuleContext.ContractWriteOff;
        Assert.True(priorityHighId > 0 && statusNotStartedId > 0 && writeOffCtxId > 0);

        // Contract
        var c = new Contract
        {
            Id = 600 + new Random().Next(1, 5000),
            InputUserId = 124, // admin
            ContractState = ContractState.Submitted,
            EntryDate = DateTime.Now,
            Amount = amount,
            IsActive = true,
            IsDeleted = false,
            Stamp = 0
        };
        db.Contracts.Add(c);
        await db.SaveChangesAsync();

        // Rules
        foreach (var r in rules)
        {
            db.ApprovalRules.Add(new ApprovalRule
            {
                Name = "Write-Off",
                Description = "Auto rule",
                IsActive = r.active,
                IsDeleted = r.deleted,
                TaskAssignedToId = r.assignedToUserId,
                AmtThreshold = r.threshold,
                PctThreshold = 0m,
                ApprovalRuleContextId = writeOffCtxId,
                Stamp = 0
            });
        }
        await db.SaveChangesAsync();

        return (c.Id, writeOffCtxId);
    }

    [Fact]
    public async Task CreatesTask_WhenAmountMeetsActiveRuleThreshold()
    {
        var (client, authCookie) = await _factory.CreateAuthenticatedClientAsync("admin", "Password1");
        if (!string.IsNullOrEmpty(authCookie)) client.DefaultRequestHeaders.Add("Cookie", authCookie!);

        var (contractId, _) = await SeedContractAndRuleAsync(12000m, (5000m, 124, true, false));

        var resp = await client.PostAsJsonAsync($"/api/contracts/{contractId}/state", new { nextStateId = (int)ContractState.WrittenOff, comment = "" });
        resp.EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var task = await db.Tasks.AsNoTracking().OrderByDescending(t => t.Id).FirstOrDefaultAsync(t => t.ContractId == contractId);
        Assert.NotNull(task);
        Assert.Equal($"Contract {contractId} Write-Off", task!.Subject);
        Assert.Equal(124, task.AssignedToUserId);
        // Priority and Status exist
        Assert.Equal((int)TaskPriority.High, task.Priority);
        Assert.Equal((int)OnlineContract.Helpers.TaskStatus.NotStarted, task.Status);
        Assert.Equal(contractId, task.ContractId);
        Assert.Equal(0, task.Stamp);
        Assert.NotNull(task.ReminderDt);
        var spanDays = (task.ReminderDt!.Value - task.InputDt).TotalDays;
        Assert.True(spanDays >= 0.99 && spanDays <= 1.01);
        Assert.Equal(new DateTime(1900, 1, 1), task.CompletedDt!.Value);
    }

    [Fact]
    public async Task NoTask_WhenAmountBelowThreshold()
    {
        var (client, authCookie) = await _factory.CreateAuthenticatedClientAsync("admin", "Password1");
        if (!string.IsNullOrEmpty(authCookie)) client.DefaultRequestHeaders.Add("Cookie", authCookie!);

        var (contractId, _) = await SeedContractAndRuleAsync(1000m, (5000m, 124, true, false));

        var resp = await client.PostAsJsonAsync($"/api/contracts/{contractId}/state", new { nextStateId = (int)ContractState.WrittenOff, comment = "" });
        resp.EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var count = await db.Tasks.AsNoTracking().CountAsync(t => t.ContractId == contractId);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task NoTask_WhenRuleInactiveOrDeleted()
    {
        var (client, authCookie) = await _factory.CreateAuthenticatedClientAsync("admin", "Password1");
        if (!string.IsNullOrEmpty(authCookie)) client.DefaultRequestHeaders.Add("Cookie", authCookie!);

        var (contractId1, _) = await SeedContractAndRuleAsync(12000m, (5000m, 124, false, false));
        var resp1 = await client.PostAsJsonAsync($"/api/contracts/{contractId1}/state", new { nextStateId = (int)ContractState.WrittenOff, comment = "" });
        resp1.EnsureSuccessStatusCode();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var count = await db.Tasks.AsNoTracking().CountAsync(t => t.ContractId == contractId1);
            Assert.Equal(0, count);
        }

        var (contractId2, _) = await SeedContractAndRuleAsync(12000m, (5000m, 124, true, true));
        var resp2 = await client.PostAsJsonAsync($"/api/contracts/{contractId2}/state", new { nextStateId = (int)ContractState.WrittenOff, comment = "" });
        resp2.EnsureSuccessStatusCode();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var count = await db.Tasks.AsNoTracking().CountAsync(t => t.ContractId == contractId2);
            Assert.Equal(0, count);
        }
    }

    [Fact]
    public async Task ChoosesHighestThresholdRule_WhenMultipleMatch()
    {
        var (client, authCookie) = await _factory.CreateAuthenticatedClientAsync("admin", "Password1");
        if (!string.IsNullOrEmpty(authCookie)) client.DefaultRequestHeaders.Add("Cookie", authCookie!);

        var (contractId, _) = await SeedContractAndRuleAsync(12000m,
            (5000m, 124, true, false),
            (10000m, 123, true, false));

        var resp = await client.PostAsJsonAsync($"/api/contracts/{contractId}/state", new { nextStateId = (int)ContractState.WrittenOff, comment = "" });
        resp.EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var task = await db.Tasks.AsNoTracking().OrderByDescending(t => t.Id).FirstOrDefaultAsync(t => t.ContractId == contractId);
        Assert.NotNull(task);
        Assert.Equal(123, task!.AssignedToUserId); // chosen by 10000 threshold
    }

    [Fact]
    public async Task Idempotent_NoDuplicateTask_OnRepeatedTransitionAttempt()
    {
        var (client, authCookie) = await _factory.CreateAuthenticatedClientAsync("admin", "Password1");
        if (!string.IsNullOrEmpty(authCookie)) client.DefaultRequestHeaders.Add("Cookie", authCookie!);

        var (contractId, _) = await SeedContractAndRuleAsync(12000m, (5000m, 124, true, false));

        var resp1 = await client.PostAsJsonAsync($"/api/contracts/{contractId}/state", new { nextStateId = (int)ContractState.WrittenOff, comment = "" });
        resp1.EnsureSuccessStatusCode();
        var resp2 = await client.PostAsJsonAsync($"/api/contracts/{contractId}/state", new { nextStateId = (int)ContractState.WrittenOff, comment = "" });
        resp2.EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var count = await db.Tasks.AsNoTracking().CountAsync(t => t.ContractId == contractId && t.Subject == $"Contract {contractId} Write-Off");
        Assert.Equal(1, count);
    }
}
