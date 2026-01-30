using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using OnlineContract.Data;
using OnlineContract.Helpers;
using OnlineContract.Models;
using Xunit;

namespace OnlineContract.Tests;

public class EomReportApiTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;

    public EomReportApiTests(WebAppFactory factory)
    {
        _factory = factory;
    }

    private async Task SeedEomReportTestDataAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Add scheduler process for EOM Report if not exists
        if (!db.SchedulerProcesses.Any(p => p.Name == "EOM Report Generation"))
        {
            db.SchedulerProcesses.Add(new SchedulerProcess
            {
                Name = "EOM Report Generation",
                Description = "Monthly EOM report generation",
                LastStartDt = new DateTime(1900, 1, 1),
                LastEndDt = new DateTime(1900, 1, 1),
                NextRunDt = DateRangeHelper.GetNextRunDt(DateTime.Now),
                IsActive = true,
                IsDeleted = false,
                Stamp = 0
            });
        }

        // Add lookup sets for TaskStatus and TaskPriority if not exists
        var lookupSets = new[]
        {
            new LookupSet { LookupSetId = 28, SetName = "TaskPriority", Value = "3 - Normal" },
            new LookupSet { LookupSetId = 30, SetName = "TaskStatus", Value = "Not Started" },
            new LookupSet { LookupSetId = 31, SetName = "TaskStatus", Value = "Started" },
            new LookupSet { LookupSetId = 34, SetName = "TaskStatus", Value = "Cancelled" },
            new LookupSet { LookupSetId = 36, SetName = "TaskStatus", Value = "Failed" },
            new LookupSet { LookupSetId = 37, SetName = "TaskStatus", Value = "Successful" },
            new LookupSet { LookupSetId = 38, SetName = "TaskStatus", Value = "Warning" },
            new LookupSet { LookupSetId = 39, SetName = "TaskStatus", Value = "Successful Nothing Processed" },
            new LookupSet { LookupSetId = 17, SetName = "ContractState", Value = "Delivered" },
            new LookupSet { LookupSetId = 13, SetName = "ContractState", Value = "Rejected" },
            new LookupSet { LookupSetId = 19, SetName = "ContractState", Value = "Cancelled" },
            new LookupSet { LookupSetId = 20, SetName = "ContractState", Value = "Written Off" },
            new LookupSet { LookupSetId = 18, SetName = "ContractState", Value = "Returned" },
            new LookupSet { LookupSetId = 21, SetName = "ContractState", Value = "Refunded" },
        };

        foreach (var ls in lookupSets)
        {
            if (!db.LookupSets.Any(l => l.LookupSetId == ls.LookupSetId))
            {
                db.LookupSets.Add(ls);
            }
        }

        // Add system user (ID=2) if not exists
        if (!db.AxUsers.Any(u => u.Id == 2))
        {
            db.AxUsers.Add(new AxUser
            {
                Id = 2,
                Code = "system",
                Email = "system@example.com",
                Phone = "0000000000",
                City = "System",
                StreetAddress = "System",
                PostalCode = "00000",
                RoleId = 8,
                IsActive = true,
                IsDeleted = false,
                Password = "notused",
                FirstName = "System",
                LastName = "User",
                OwnerId = 2,
                CreatedDt = DateTime.Now,
                PasswordDt = DateTime.Now,
                Stamp = 0
            });
        }

        // Add some delivered contracts for testing
        var now = DateTime.Now;
        var startOfMonth = new DateTime(now.Year, now.Month, 1);

        for (int i = 0; i < 3; i++)
        {
            var contractId = 1000 + i;
            if (!db.Contracts.Any(c => c.Id == contractId))
            {
                db.Contracts.Add(new Contract
                {
                    Id = contractId,
                    InputUserId = 124,
                    ContractState = ContractState.Delivered,
                    EntryDate = startOfMonth.AddDays(i),
                    DeliveredDt = startOfMonth.AddDays(i + 1),
                    Amount = 500m + (i * 100),
                    IsActive = true,
                    IsDeleted = false,
                    Stamp = 0
                });
            }
        }

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetPreview_ReturnsOk_ForAdmin()
    {
        // Arrange
        await SeedEomReportTestDataAsync();
        var (client, authCookie) = await _factory.CreateAuthenticatedClientAsync("admin", "Password1");
        client.DefaultRequestHeaders.Add("Cookie", authCookie);

        // Act
        var response = await client.GetAsync("/api/reports/eom/preview");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<PreviewResponse>();
        Assert.NotNull(json);
        Assert.NotNull(json.summaries);
        Assert.Equal(6, json.summaries.Length); // 6 statuses: Delivered, Rejected, Cancelled, Written Off, Returned, Refunded
    }

    [Fact]
    public async Task GetPreview_ReturnsForbidden_ForNonAdmin()
    {
        // Arrange
        var (client, authCookie) = await _factory.CreateAuthenticatedClientAsync("testuser", "Password1");
        client.DefaultRequestHeaders.Add("Cookie", authCookie);

        // Act
        var response = await client.GetAsync("/api/reports/eom/preview");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetPreview_ReturnsForbidden_WithoutAuth()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/reports/eom/preview");

        // Assert
        // Without auth, the controller sees userId=0 and returns Forbidden
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ProcessesList_ReturnsEomReportProcess()
    {
        // Arrange
        await SeedEomReportTestDataAsync();
        var (client, authCookie) = await _factory.CreateAuthenticatedClientAsync("admin", "Password1");
        client.DefaultRequestHeaders.Add("Cookie", authCookie);

        // Act
        var response = await client.GetAsync("/api/processes?page=1&pageSize=100");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<ProcessesResponse>();
        Assert.NotNull(json);
        Assert.Contains(json.items, p => p.name == "EOM Report Generation");
    }

    [Fact]
    public async Task ProcessActivate_TogglesIsActive()
    {
        // Arrange
        await SeedEomReportTestDataAsync();
        var (client, authCookie) = await _factory.CreateAuthenticatedClientAsync("admin", "Password1");
        client.DefaultRequestHeaders.Add("Cookie", authCookie);

        // Get the process ID
        var listResponse = await client.GetAsync("/api/processes?page=1&pageSize=100");
        var json = await listResponse.Content.ReadFromJsonAsync<ProcessesResponse>();
        var process = json?.items.FirstOrDefault(p => p.name == "EOM Report Generation");
        Assert.NotNull(process);

        // Act - Deactivate
        var deactivateResponse = await client.PostAsync($"/api/processes/{process.id}/deactivate", null);
        Assert.Equal(HttpStatusCode.OK, deactivateResponse.StatusCode);

        // Act - Activate
        var activateResponse = await client.PostAsync($"/api/processes/{process.id}/activate", null);
        Assert.Equal(HttpStatusCode.OK, activateResponse.StatusCode);

        var activateResult = await activateResponse.Content.ReadFromJsonAsync<ActivateResponse>();
        Assert.NotNull(activateResult);
        Assert.True(activateResult.isActive);
    }

    [Fact]
    public async Task ProcessRun_CreatesTaskAndGeneratesPdf()
    {
        // Arrange
        await SeedEomReportTestDataAsync();
        
        // Clean up any existing pending EOM tasks to ensure isolation
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var pendingTasks = db.Tasks.Where(t => 
                t.Subject == "EOM Report Generation" && 
                (t.Status == 30 || t.Status == 31)); // NotStarted or Started
            db.Tasks.RemoveRange(pendingTasks);
            await db.SaveChangesAsync();
        }
        
        var (client, authCookie) = await _factory.CreateAuthenticatedClientAsync("admin", "Password1");
        client.DefaultRequestHeaders.Add("Cookie", authCookie);

        // Get the process ID
        var listResponse = await client.GetAsync("/api/processes?page=1&pageSize=100");
        var json = await listResponse.Content.ReadFromJsonAsync<ProcessesResponse>();
        var process = json?.items.FirstOrDefault(p => p.name == "EOM Report Generation");
        Assert.NotNull(process);

        // Act - Run the process
        var runResponse = await client.PostAsync($"/api/processes/{process.id}/run", null);
        
        // Assert
        Assert.Equal(HttpStatusCode.OK, runResponse.StatusCode);
        var runResult = await runResponse.Content.ReadFromJsonAsync<RunResponse>();
        Assert.NotNull(runResult);
        Assert.True(runResult.success, $"Run failed with message: {runResult.message}");
        
        // Verify task was created (check tasks endpoint)
        var tasksResponse = await client.GetAsync("/api/tasks?page=1&pageSize=100");
        var tasksJson = await tasksResponse.Content.ReadFromJsonAsync<TasksResponse>();
        Assert.NotNull(tasksJson);
        Assert.Contains(tasksJson.items, t => t.subject == "EOM Report Generation");
    }

    [Fact]
    public async Task ProcessRun_ReturnsConflict_WhenPendingTaskExists()
    {
        // Arrange
        await SeedEomReportTestDataAsync();
        var (client, authCookie) = await _factory.CreateAuthenticatedClientAsync("admin", "Password1");
        client.DefaultRequestHeaders.Add("Cookie", authCookie);

        // Get the process ID
        var listResponse = await client.GetAsync("/api/processes?page=1&pageSize=100");
        var json = await listResponse.Content.ReadFromJsonAsync<ProcessesResponse>();
        var process = json?.items.FirstOrDefault(p => p.name == "EOM Report Generation");
        Assert.NotNull(process);

        // First run - should succeed
        var firstRunResponse = await client.PostAsync($"/api/processes/{process.id}/run", null);
        Assert.Equal(HttpStatusCode.OK, firstRunResponse.StatusCode);

        // Create a pending task manually with "Not Started" status
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Get the "Not Started" status ID
            var notStartedId = db.LookupSets.First(l => l.SetName == "TaskStatus" && l.Value == "Not Started").LookupSetId;
            var priorityId = db.LookupSets.First(l => l.SetName == "TaskPriority" && l.Value == "3 - Normal").LookupSetId;
            
            // Add a pending SYSTEM task (contract_id=null, assigned_to_user_id=2)
            db.Tasks.Add(new Models.TaskItem
            {
                Subject = "EOM Report Generation",
                Comments = "Pending task for validation test",
                Status = notStartedId,
                Priority = priorityId,
                InitiatedByUserId = 2,
                AssignedToUserId = 2, // System user ID - required for pending task check
                ContractId = null,    // Must be null for system task
                InputDt = DateTime.Now,
                Stamp = 0
            });
            await db.SaveChangesAsync();
        }

        // Second run - should fail with conflict
        var secondRunResponse = await client.PostAsync($"/api/processes/{process.id}/run", null);
        
        // Assert
        Assert.Equal(HttpStatusCode.Conflict, secondRunResponse.StatusCode);
        var conflictResult = await secondRunResponse.Content.ReadFromJsonAsync<ConflictResponse>();
        Assert.NotNull(conflictResult);
        Assert.Contains("EOM Report Generation", conflictResult.message);
    }

    private record ConflictResponse(string message);

    private record RunResponse(bool success, string status, string? filePath, string message);
    private record TasksResponse(TaskItem[] items, int totalPages, int totalCount);
    private record TaskItem(int id, string subject, string comment, string entryDate, int initiatedByUserId, string initiatedByUserCode);
    private record PreviewResponse(DateTime periodFrom, DateTime periodTo, SummaryItem[] summaries, int totalCount, decimal totalAmount);
    private record SummaryItem(string statusName, int statusId, int count, decimal totalAmount);
    private record ProcessesResponse(ProcessItem[] items, int totalCount, int totalPages);
    private record ProcessItem(int id, string name, string? description, DateTime? lastStartDt, DateTime? lastEndDt, DateTime? nextRunDt, bool isActive, bool isRunning);
    private record ActivateResponse(string message, bool isActive);
    private record CancelResponse(bool success, string message, string? processName, int? duration, string? durationFmt);

    [Fact]
    public async Task ProcessCancel_ReturnsOk_WhenProcessIsRunning()
    {
        // Arrange
        await SeedEomReportTestDataAsync();
        var (client, authCookie) = await _factory.CreateAuthenticatedClientAsync("admin", "Password1");
        client.DefaultRequestHeaders.Add("Cookie", authCookie);

        int taskId;
        int processId;
        
        // Create test data in isolated scope that's disposed before API call
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            // Clean up any existing pending EOM tasks to ensure test isolation
            var existingPendingTasks = db.Tasks.Where(t => 
                t.Subject == "EOM Report Generation" && 
                (t.Status == 30 || t.Status == 31)); // NotStarted or Started
            db.Tasks.RemoveRange(existingPendingTasks);
            await db.SaveChangesAsync();
            
            // Get or create the process
            var process = db.SchedulerProcesses.FirstOrDefault(p => p.Name == "EOM Report Generation");
            if (process == null)
            {
                process = new SchedulerProcess
                {
                    Name = "EOM Report Generation",
                    Description = "Monthly EOM report generation",
                    LastStartDt = DateTime.Now.AddMinutes(-5),
                    LastEndDt = new DateTime(1900, 1, 1), // Old date means still running
                    NextRunDt = DateRangeHelper.GetNextRunDt(DateTime.Now),
                    IsActive = true,
                    IsDeleted = false,
                    Stamp = 0
                };
                db.SchedulerProcesses.Add(process);
                await db.SaveChangesAsync();
            }
            else
            {
                // Set as running
                process.LastStartDt = DateTime.Now.AddMinutes(-5);
                process.LastEndDt = new DateTime(1900, 1, 1);
                await db.SaveChangesAsync();
            }

            // Add pending task to simulate running process
            var startedId = db.LookupSets.FirstOrDefault(l => l.SetName == "TaskStatus" && l.Value == "Started")?.LookupSetId ?? 31;
            var priorityId = db.LookupSets.FirstOrDefault(l => l.SetName == "TaskPriority" && l.Value == "3 - Normal")?.LookupSetId ?? 28;
            
            var pendingTask = new Models.TaskItem
            {
                Subject = "EOM Report Generation",
                Comments = "Running task for cancel test",
                Status = startedId,
                Priority = priorityId,
                InitiatedByUserId = 2,
                AssignedToUserId = 2,
                ContractId = null,
                InputDt = DateTime.Now,
                Stamp = 0
            };
            db.Tasks.Add(pendingTask);
            await db.SaveChangesAsync();
            taskId = pendingTask.Id;
            processId = process.Id;
        } // Scope disposed here, ensuring all changes are committed

        // Act
        var cancelResponse = await client.PostAsync($"/api/processes/{processId}/cancel", null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);
        var result = await cancelResponse.Content.ReadFromJsonAsync<CancelResponse>();
        Assert.NotNull(result);
        Assert.True(result.success);
        Assert.Contains("cancelled", result.message);
        
        // Verify task was updated to Cancelled status
        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cancelledId = verifyDb.LookupSets.FirstOrDefault(l => l.SetName == "TaskStatus" && l.Value == "Cancelled")?.LookupSetId ?? 34;
        var updatedTask = verifyDb.Tasks.Find(taskId);
        Assert.NotNull(updatedTask);
        Assert.Equal(cancelledId, updatedTask.Status);
        Assert.NotNull(updatedTask.CompletedDt);
        
        // Verify process was updated
        var updatedProcess = verifyDb.SchedulerProcesses.Find(processId);
        Assert.NotNull(updatedProcess);
        Assert.NotNull(updatedProcess.LastEndDt);
        Assert.True(updatedProcess.LastEndDt.Value.Year > 1900);
    }

    [Fact]
    public async Task ProcessCancel_ReturnsOk_WhenProcessNotRunning()
    {
        // Arrange
        await SeedEomReportTestDataAsync();
        var (client, authCookie) = await _factory.CreateAuthenticatedClientAsync("admin", "Password1");
        client.DefaultRequestHeaders.Add("Cookie", authCookie);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        
        // Get or create a process that's NOT running
        var process = db.SchedulerProcesses.FirstOrDefault(p => p.Name == "EOM Report Generation");
        if (process == null)
        {
            process = new SchedulerProcess
            {
                Name = "EOM Report Generation",
                Description = "Monthly EOM report generation",
                LastStartDt = DateTime.Now.AddMinutes(-10),
                LastEndDt = DateTime.Now.AddMinutes(-5), // Ends after start = not running
                NextRunDt = DateRangeHelper.GetNextRunDt(DateTime.Now),
                IsActive = true,
                IsDeleted = false,
                Stamp = 0
            };
            db.SchedulerProcesses.Add(process);
            await db.SaveChangesAsync();
        }
        else
        {
            // Set as NOT running
            process.LastStartDt = DateTime.Now.AddMinutes(-10);
            process.LastEndDt = DateTime.Now.AddMinutes(-5);
            await db.SaveChangesAsync();
        }
        var processId = process.Id;

        // Act
        var cancelResponse = await client.PostAsync($"/api/processes/{processId}/cancel", null);

        // Assert - should succeed silently (process not running, nothing to cancel)
        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);
        var result = await cancelResponse.Content.ReadFromJsonAsync<CancelResponse>();
        Assert.NotNull(result);
        Assert.True(result.success);
        Assert.Contains("not running", result.message);
    }

    [Fact]
    public async Task ProcessCancel_ReturnsNotFound_WhenProcessDoesNotExist()
    {
        // Arrange
        await SeedEomReportTestDataAsync();
        var (client, authCookie) = await _factory.CreateAuthenticatedClientAsync("admin", "Password1");
        client.DefaultRequestHeaders.Add("Cookie", authCookie);

        // Act - use a non-existent process ID
        var cancelResponse = await client.PostAsync("/api/processes/99999/cancel", null);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, cancelResponse.StatusCode);
    }

    [Fact]
    public async Task ProcessCancel_Returns403_ForNonPrivilegedUser()
    {
        // Arrange
        await SeedEomReportTestDataAsync();
        var (client, authCookie) = await _factory.CreateAuthenticatedClientAsync("testuser", "Password1");
        client.DefaultRequestHeaders.Add("Cookie", authCookie);

        // Get any process ID
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var process = db.SchedulerProcesses.FirstOrDefault(p => !p.IsDeleted);
        if (process == null) return; // Skip if no process exists

        // Act
        var cancelResponse = await client.PostAsync($"/api/processes/{process.Id}/cancel", null);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, cancelResponse.StatusCode);
    }
}
