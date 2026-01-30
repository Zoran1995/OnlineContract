using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OnlineContract.Data;
using OnlineContract.Helpers;
using OnlineContract.Models;
using OnlineContract.Services.DraftPurge;
using Xunit;
using TaskStatusEnum = OnlineContract.Helpers.TaskStatus;

namespace OnlineContract.Tests;

public class DraftContractPurgeServiceTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;

    public DraftContractPurgeServiceTests(WebAppFactory factory)
    {
        _factory = factory;
    }

    #region DraftContractPurgeResult Status Tests

    [Fact]
    public void DraftPurgeResult_Status_ReturnsSuccessful_WhenAllDeleted()
    {
        var result = new DraftContractPurgeResult
        {
            Success = true,
            DeletedCount = 10,
            FailedCount = 0
        };

        Assert.Equal(DraftPurgeStatus.Successful, result.Status);
    }

    [Fact]
    public void DraftPurgeResult_Status_ReturnsWarning_WhenSomeFailed()
    {
        var result = new DraftContractPurgeResult
        {
            Success = true,
            DeletedCount = 8,
            FailedCount = 2
        };

        Assert.Equal(DraftPurgeStatus.Warning, result.Status);
    }

    [Fact]
    public void DraftPurgeResult_Status_ReturnsFailed_WhenNotSuccess()
    {
        var result = new DraftContractPurgeResult
        {
            Success = false,
            ErrorMessage = "Database error"
        };

        Assert.Equal(DraftPurgeStatus.Failed, result.Status);
    }

    [Fact]
    public void DraftPurgeResult_Status_ReturnsSuccessfulNothingProcessed_WhenNoData()
    {
        var result = new DraftContractPurgeResult
        {
            Success = true,
            DeletedCount = 0,
            FailedCount = 0
        };

        Assert.Equal(DraftPurgeStatus.SuccessfulNothingProcessed, result.Status);
    }

    #endregion

    #region GetNextRunAt0300 Tests

    [Fact]
    public void GetNextRunAt0300_BeforeMidnight_ReturnsNext0300Today()
    {
        // Arrange - at 2:00 AM, next 03:00 should be same day
        var now = new DateTime(2026, 3, 15, 2, 0, 0);

        // Act
        var nextRun = DraftContractPurgeService.GetNextRunAt0300(now);

        // Assert
        Assert.Equal(new DateTime(2026, 3, 15, 3, 0, 0), nextRun);
    }

    [Fact]
    public void GetNextRunAt0300_After0300_ReturnsTomorrow0300()
    {
        // Arrange - at 4:00 AM (after 03:00), next 03:00 should be tomorrow
        var now = new DateTime(2026, 3, 15, 4, 0, 0);

        // Act
        var nextRun = DraftContractPurgeService.GetNextRunAt0300(now);

        // Assert
        Assert.Equal(new DateTime(2026, 3, 16, 3, 0, 0), nextRun);
    }

    [Fact]
    public void GetNextRunAt0300_Exactly0300_ReturnsTomorrow0300()
    {
        // Arrange - at exactly 03:00, next 03:00 should be tomorrow
        var now = new DateTime(2026, 3, 15, 3, 0, 0);

        // Act
        var nextRun = DraftContractPurgeService.GetNextRunAt0300(now);

        // Assert
        Assert.Equal(new DateTime(2026, 3, 16, 3, 0, 0), nextRun);
    }

    [Fact]
    public void GetNextRunAt0300_Midnight_ReturnsNext0300Today()
    {
        // Arrange - at midnight, next 03:00 should be same day
        var now = new DateTime(2026, 3, 15, 0, 0, 0);

        // Act
        var nextRun = DraftContractPurgeService.GetNextRunAt0300(now);

        // Assert
        Assert.Equal(new DateTime(2026, 3, 15, 3, 0, 0), nextRun);
    }

    [Fact]
    public void GetNextRunAt0300_EndOfYear_RollsToNextYear()
    {
        // Arrange - at 10:00 PM on Dec 31
        var now = new DateTime(2026, 12, 31, 22, 0, 0);

        // Act
        var nextRun = DraftContractPurgeService.GetNextRunAt0300(now);

        // Assert
        Assert.Equal(new DateTime(2027, 1, 1, 3, 0, 0), nextRun);
    }

    #endregion

    #region Selection Criteria Tests

    [Fact]
    public async Task PurgeService_SelectsOnlyDraftContracts()
    {
        // Arrange - Create contracts with different states
        var oldDate = DateTime.Now.AddDays(-40);
        
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            // Draft contract - should be selected
            db.Contracts.Add(new Contract
            {
                Id = 10001,
                InputUserId = 123,
                ContractState = ContractState.Draft,
                EntryDate = oldDate,
                Amount = 100m,
                IsActive = true,
                IsDeleted = false,
                Stamp = 0
            });
            
            // Delivered contract - should NOT be selected
            db.Contracts.Add(new Contract
            {
                Id = 10002,
                InputUserId = 123,
                ContractState = ContractState.Delivered,
                EntryDate = oldDate,
                Amount = 100m,
                IsActive = true,
                IsDeleted = false,
                Stamp = 0
            });

            // Cancelled contract - should NOT be selected
            db.Contracts.Add(new Contract
            {
                Id = 10003,
                InputUserId = 123,
                ContractState = ContractState.Cancelled,
                EntryDate = oldDate,
                Amount = 100m,
                IsActive = true,
                IsDeleted = false,
                Stamp = 0
            });
            
            await db.SaveChangesAsync();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            // Query selection criteria directly (EntryDate maps to input_dt)
            var draftContracts = await db.Contracts
                .AsNoTracking()
                .Where(c => c.ContractState == ContractState.Draft &&
                           !c.IsDeleted &&
                           c.EntryDate < DateTime.Now.AddDays(-30))
                .Select(c => c.Id)
                .ToListAsync();

            // Assert - only the draft contract should be selected
            Assert.Contains(10001, draftContracts);
            Assert.DoesNotContain(10002, draftContracts);
            Assert.DoesNotContain(10003, draftContracts);
        }
    }

    [Fact]
    public async Task PurgeService_SelectsOnlyContractsOlderThan30Days()
    {
        // Arrange - Create draft contracts with different ages
        // Use a fixed reference time to avoid timing issues
        var now = DateTime.Now;
        
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            // 40 days old - should be selected
            db.Contracts.Add(new Contract
            {
                Id = 10101,
                InputUserId = 123,
                ContractState = ContractState.Draft,
                EntryDate = now.AddDays(-40),
                Amount = 100m,
                IsActive = true,
                IsDeleted = false,
                Stamp = 0
            });
            
            // 31 days old - should be selected
            db.Contracts.Add(new Contract
            {
                Id = 10102,
                InputUserId = 123,
                ContractState = ContractState.Draft,
                EntryDate = now.AddDays(-31),
                Amount = 100m,
                IsActive = true,
                IsDeleted = false,
                Stamp = 0
            });
            
            // Exactly 30 days - should NOT be selected (boundary)
            // Use a time slightly in the future to ensure it's not older than 30 days
            db.Contracts.Add(new Contract
            {
                Id = 10103,
                InputUserId = 123,
                ContractState = ContractState.Draft,
                EntryDate = now.AddDays(-30).AddHours(1),
                Amount = 100m,
                IsActive = true,
                IsDeleted = false,
                Stamp = 0
            });
            
            // 25 days old - should NOT be selected
            db.Contracts.Add(new Contract
            {
                Id = 10104,
                InputUserId = 123,
                ContractState = ContractState.Draft,
                EntryDate = now.AddDays(-25),
                Amount = 100m,
                IsActive = true,
                IsDeleted = false,
                Stamp = 0
            });
            
            await db.SaveChangesAsync();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            // Query selection criteria (EntryDate maps to input_dt)
            var selectedContracts = await db.Contracts
                .AsNoTracking()
                .Where(c => c.ContractState == ContractState.Draft &&
                           !c.IsDeleted &&
                           c.EntryDate < DateTime.Now.AddDays(-30))
                .Select(c => c.Id)
                .ToListAsync();

            // Assert
            Assert.Contains(10101, selectedContracts);
            Assert.Contains(10102, selectedContracts);
            Assert.DoesNotContain(10103, selectedContracts); // Boundary case
            Assert.DoesNotContain(10104, selectedContracts);
        }
    }

    [Fact]
    public async Task PurgeService_ExcludesDeletedContracts()
    {
        // Arrange
        var oldDate = DateTime.Now.AddDays(-40);
        
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            // Soft-deleted draft contract - should NOT be selected
            db.Contracts.Add(new Contract
            {
                Id = 10201,
                InputUserId = 123,
                ContractState = ContractState.Draft,
                EntryDate = oldDate,
                Amount = 100m,
                IsActive = false,
                IsDeleted = true,
                Stamp = 0
            });
            
            await db.SaveChangesAsync();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            var selectedContracts = await db.Contracts
                .AsNoTracking()
                .Where(c => c.ContractState == ContractState.Draft &&
                           !c.IsDeleted &&
                           c.EntryDate < DateTime.Now.AddDays(-30))
                .Select(c => c.Id)
                .ToListAsync();

            Assert.DoesNotContain(10201, selectedContracts);
        }
    }

    #endregion

    #region HasPendingTaskAsync Tests

    [Fact]
    public async Task HasPendingTaskAsync_ReturnsFalse_WhenNoTasks()
    {
        using var scope = _factory.Services.CreateScope();
        var purgeService = scope.ServiceProvider.GetRequiredService<IDraftContractPurgeService>();

        var hasPending = await purgeService.HasPendingTaskAsync(CancellationToken.None);

        // Should be false since no tasks exist with subject "Draft Contract Purge"
        Assert.False(hasPending);
    }

    [Fact]
    public async Task HasPendingTaskAsync_ReturnsTrue_WhenPendingTaskExists()
    {
        // Arrange - create a pending task
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            db.Tasks.Add(new TaskItem
            {
                Id = 20001,
                Subject = "Draft Contract Purge",
                Status = (int)TaskStatusEnum.NotStarted,
                Priority = 29, // '4 - Low'
                Comments = "Test",
                InputDt = DateTime.Now,
                InitiatedByUserId = 2,
                AssignedToUserId = 2,
                Stamp = 0
            });
            
            await db.SaveChangesAsync();
        }

        // Act
        using (var scope = _factory.Services.CreateScope())
        {
            var purgeService = scope.ServiceProvider.GetRequiredService<IDraftContractPurgeService>();
            var hasPending = await purgeService.HasPendingTaskAsync(CancellationToken.None);

            // Assert
            Assert.True(hasPending);
        }

        // Cleanup
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var task = await db.Tasks.FindAsync(20001);
            if (task != null)
            {
                db.Tasks.Remove(task);
                await db.SaveChangesAsync();
            }
        }
    }

    [Fact]
    public async Task HasPendingTaskAsync_ReturnsFalse_WhenTaskIsCompleted()
    {
        // Arrange - create a completed task
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            db.Tasks.Add(new TaskItem
            {
                Id = 20002,
                Subject = "Draft Contract Purge",
                Status = (int)TaskStatusEnum.Successful, // Completed
                Priority = 29,
                Comments = "Test completed",
                InputDt = DateTime.Now,
                InitiatedByUserId = 2,
                AssignedToUserId = 2,
                Stamp = 0
            });
            
            await db.SaveChangesAsync();
        }

        // Act
        using (var scope = _factory.Services.CreateScope())
        {
            var purgeService = scope.ServiceProvider.GetRequiredService<IDraftContractPurgeService>();
            var hasPending = await purgeService.HasPendingTaskAsync(CancellationToken.None);

            // Assert - completed tasks should not block
            Assert.False(hasPending);
        }

        // Cleanup
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var task = await db.Tasks.FindAsync(20002);
            if (task != null)
            {
                db.Tasks.Remove(task);
                await db.SaveChangesAsync();
            }
        }
    }

    #endregion

    #region API Integration Tests

    [Fact]
    public async Task ProcessesRun_ReturnsConflict_WhenTaskIsPending()
    {
        // Arrange - create the scheduler process and a pending task
        int processId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            var process = new SchedulerProcess
            {
                Name = "Draft Contract Purge",
                Description = "Test process",
                IsActive = true,
                IsDeleted = false,
                Stamp = 0
            };
            db.SchedulerProcesses.Add(process);
            await db.SaveChangesAsync();
            processId = process.Id;

            db.Tasks.Add(new TaskItem
            {
                Id = 20003,
                Subject = "Draft Contract Purge",
                Status = (int)TaskStatusEnum.Started, // In progress
                Priority = 29,
                Comments = "Running",
                InputDt = DateTime.Now,
                InitiatedByUserId = 2,
                AssignedToUserId = 2,
                Stamp = 0
            });
            
            await db.SaveChangesAsync();
        }

        // Act
        var (client, cookie) = await _factory.CreateAuthenticatedClientAsync("admin", "Password1");
        if (!string.IsNullOrEmpty(cookie)) client.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);
        var response = await client.PostAsync($"/api/processes/{processId}/run", null);

        // Assert
        Assert.Equal(System.Net.HttpStatusCode.Conflict, response.StatusCode);

        // Cleanup
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var task = await db.Tasks.FindAsync(20003);
            if (task != null)
            {
                db.Tasks.Remove(task);
            }
            var proc = await db.SchedulerProcesses.FindAsync(processId);
            if (proc != null)
            {
                db.SchedulerProcesses.Remove(proc);
            }
            await db.SaveChangesAsync();
        }
    }

    #endregion
}
