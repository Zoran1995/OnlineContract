using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using OnlineContract.Data;
using OnlineContract.Models;
using Xunit;

namespace OnlineContract.Tests
{
    public class ProcessLogApiTests : IClassFixture<WebAppFactory>
    {
        private readonly WebAppFactory _factory;
        public ProcessLogApiTests(WebAppFactory factory) { _factory = factory; }

        private static Task EnsureAuthCookieAsync(HttpClient client, string cookie)
        {
            if (!string.IsNullOrEmpty(cookie))
            {
                client.DefaultRequestHeaders.Remove("Cookie");
                client.DefaultRequestHeaders.Add("Cookie", cookie);
            }
            return Task.CompletedTask;
        }

        [Fact]
        public async Task Get_ReturnsAccessDenied_ForCustomer()
        {
            // testuser has roleId = 5 (Customer)
            var (client, cookie) = await _factory.CreateAuthenticatedClientAsync("testuser", "Password1");
            await EnsureAuthCookieAsync(client, cookie ?? "");

            var resp = await client.GetAsync("/api/processlog");
            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        }

        [Fact]
        public async Task Get_ReturnsOk_ForAdmin()
        {
            // admin has roleId = 8 (Administrator)
            var (client, cookie) = await _factory.CreateAuthenticatedClientAsync("admin", "Password1");
            await EnsureAuthCookieAsync(client, cookie ?? "");

            var resp = await client.GetAsync("/api/processlog");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            
            var data = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            Assert.True(data.TryGetProperty("items", out _));
            Assert.True(data.TryGetProperty("totalCount", out _));
            Assert.True(data.TryGetProperty("totalPages", out _));
        }

        [Fact]
        public async Task Get_FiltersOnlyProcessTasks()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Add a process-related task
            db.Tasks.Add(new TaskItem
            {
                Id = 9001,
                Subject = "EOM Report Generation",
                Comments = "Test process task",
                AssignedToUserId = 2, // System
                InitiatedByUserId = 2,
                Priority = (int)OnlineContract.Helpers.TaskPriority.Normal,
                Status = (int)OnlineContract.Helpers.TaskStatus.Started,
                InputDt = DateTime.UtcNow,
                Stamp = 0
            });

            // Add a non-process task
            db.Tasks.Add(new TaskItem
            {
                Id = 9002,
                Subject = "Regular Task",
                Comments = "Not a process task",
                AssignedToUserId = 123,
                InitiatedByUserId = 124,
                Priority = (int)OnlineContract.Helpers.TaskPriority.Normal,
                Status = (int)OnlineContract.Helpers.TaskStatus.Started,
                InputDt = DateTime.UtcNow,
                Stamp = 0
            });
            db.SaveChanges();

            var (client, cookie) = await _factory.CreateAuthenticatedClientAsync("admin", "Password1");
            await EnsureAuthCookieAsync(client, cookie ?? "");

            var resp = await client.GetAsync("/api/processlog?page=1&pageSize=100");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            var data = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            var items = data.GetProperty("items");

            // Verify only process-related tasks are returned
            foreach (var item in items.EnumerateArray())
            {
                var process = item.GetProperty("process").GetString();
                Assert.True(
                    process == "EOM Report Generation" || process == "Draft Contract Purge",
                    $"Unexpected process name: {process}");
            }
        }

        [Fact]
        public async Task Get_FiltersBy_StatusId()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Add tasks with different statuses
            db.Tasks.Add(new TaskItem
            {
                Id = 9101,
                Subject = "EOM Report Generation",
                Comments = "Started task",
                AssignedToUserId = 2,
                InitiatedByUserId = 2,
                Priority = (int)OnlineContract.Helpers.TaskPriority.Normal,
                Status = (int)OnlineContract.Helpers.TaskStatus.Started,
                InputDt = DateTime.UtcNow,
                Stamp = 0
            });

            db.Tasks.Add(new TaskItem
            {
                Id = 9102,
                Subject = "EOM Report Generation",
                Comments = "Completed task",
                AssignedToUserId = 2,
                InitiatedByUserId = 2,
                Priority = (int)OnlineContract.Helpers.TaskPriority.Normal,
                Status = (int)OnlineContract.Helpers.TaskStatus.Completed,
                InputDt = DateTime.UtcNow,
                Stamp = 0
            });
            db.SaveChanges();

            var (client, cookie) = await _factory.CreateAuthenticatedClientAsync("admin", "Password1");
            await EnsureAuthCookieAsync(client, cookie ?? "");

            // Filter by Started status (31)
            var resp = await client.GetAsync("/api/processlog?statusId=31&page=1&pageSize=100");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            var data = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            var items = data.GetProperty("items");

            foreach (var item in items.EnumerateArray())
            {
                var status = item.GetProperty("status").GetInt32();
                Assert.Equal(31, status);
            }
        }

        [Fact]
        public async Task Cancel_ReturnsAccessDenied_ForCustomer()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            db.Tasks.Add(new TaskItem
            {
                Id = 9201,
                Subject = "EOM Report Generation",
                Comments = "Task to cancel",
                AssignedToUserId = 2,
                InitiatedByUserId = 2,
                Priority = (int)OnlineContract.Helpers.TaskPriority.Normal,
                Status = (int)OnlineContract.Helpers.TaskStatus.Started,
                InputDt = DateTime.UtcNow,
                Stamp = 0
            });
            db.SaveChanges();

            var (client, cookie) = await _factory.CreateAuthenticatedClientAsync("testuser", "Password1");
            await EnsureAuthCookieAsync(client, cookie ?? "");

            var resp = await client.PostAsync("/api/processlog/9201/cancel", null);
            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        }

        [Fact]
        public async Task Cancel_ReturnsNotFound_ForNonExistentTask()
        {
            var (client, cookie) = await _factory.CreateAuthenticatedClientAsync("admin", "Password1");
            await EnsureAuthCookieAsync(client, cookie ?? "");

            var resp = await client.PostAsync("/api/processlog/99999/cancel", null);
            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        }

        [Fact]
        public async Task Cancel_ReturnsBadRequest_ForNonProcessTask()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            db.Tasks.Add(new TaskItem
            {
                Id = 9301,
                Subject = "Regular Task",
                Comments = "Not a process task",
                AssignedToUserId = 123,
                InitiatedByUserId = 124,
                Priority = (int)OnlineContract.Helpers.TaskPriority.Normal,
                Status = (int)OnlineContract.Helpers.TaskStatus.Started,
                InputDt = DateTime.UtcNow,
                Stamp = 0
            });
            db.SaveChanges();

            var (client, cookie) = await _factory.CreateAuthenticatedClientAsync("admin", "Password1");
            await EnsureAuthCookieAsync(client, cookie ?? "");

            var resp = await client.PostAsync("/api/processlog/9301/cancel", null);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);

            var data = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            Assert.Contains("not a process-related task", data.GetProperty("message").GetString());
        }

        [Fact]
        public async Task Cancel_ReturnsBadRequest_ForNonStartedTask()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            db.Tasks.Add(new TaskItem
            {
                Id = 9401,
                Subject = "EOM Report Generation",
                Comments = "Completed task",
                AssignedToUserId = 2,
                InitiatedByUserId = 2,
                Priority = (int)OnlineContract.Helpers.TaskPriority.Normal,
                Status = (int)OnlineContract.Helpers.TaskStatus.Completed,
                InputDt = DateTime.UtcNow,
                Stamp = 0
            });
            db.SaveChanges();

            var (client, cookie) = await _factory.CreateAuthenticatedClientAsync("admin", "Password1");
            await EnsureAuthCookieAsync(client, cookie ?? "");

            var resp = await client.PostAsync("/api/processlog/9401/cancel", null);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);

            var data = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            Assert.Contains("not in Started status", data.GetProperty("message").GetString());
        }

        [Fact]
        public async Task Cancel_UpdatesTaskAndProcess_ForAdmin()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Clean up any existing scheduler process with this name first
            var existingProcess = db.SchedulerProcesses.FirstOrDefault(p => p.Name == "EOM Report Generation");
            if (existingProcess != null)
            {
                existingProcess.LastStartDt = DateTime.UtcNow.AddMinutes(-5);
                existingProcess.LastEndDt = null;
                existingProcess.DurationSec = 0;
                existingProcess.DurationFmt = null;
            }
            else
            {
                // Add a scheduler process
                db.SchedulerProcesses.Add(new SchedulerProcess
                {
                    Name = "EOM Report Generation",
                    Description = "Test process",
                    IsActive = true,
                    IsDeleted = false,
                    LastStartDt = DateTime.UtcNow.AddMinutes(-5),
                    LastEndDt = null,
                    Stamp = 0
                });
            }

            // Add a started task for the process
            db.Tasks.Add(new TaskItem
            {
                Id = 9501,
                Subject = "EOM Report Generation",
                Comments = "Running task",
                AssignedToUserId = 2,
                InitiatedByUserId = 2,
                Priority = (int)OnlineContract.Helpers.TaskPriority.Normal,
                Status = (int)OnlineContract.Helpers.TaskStatus.Started,
                InputDt = DateTime.UtcNow,
                Stamp = 0
            });
            db.SaveChanges();

            var (client, cookie) = await _factory.CreateAuthenticatedClientAsync("admin", "Password1");
            await EnsureAuthCookieAsync(client, cookie ?? "");

            var resp = await client.PostAsync("/api/processlog/9501/cancel", null);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            var data = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            Assert.True(data.GetProperty("success").GetBoolean());
            Assert.Contains("cancelled", data.GetProperty("message").GetString()?.ToLower() ?? "");

            // Verify task status was updated
            using var scope2 = _factory.Services.CreateScope();
            var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
            
            var updatedTask = await db2.Tasks.FindAsync(9501);
            Assert.NotNull(updatedTask);
            Assert.Equal((int)OnlineContract.Helpers.TaskStatus.Cancelled, updatedTask!.Status);
            Assert.NotNull(updatedTask.CompletedDt);

            // Verify scheduler process was updated (find by name, not by id)
            var updatedProcess = db2.SchedulerProcesses.FirstOrDefault(p => p.Name == "EOM Report Generation");
            Assert.NotNull(updatedProcess);
            Assert.NotNull(updatedProcess!.LastEndDt);
        }

        [Fact]
        public async Task Cancel_WorksWithoutSchedulerProcess()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Add a started task without a corresponding scheduler process
            db.Tasks.Add(new TaskItem
            {
                Id = 9601,
                Subject = "Draft Contract Purge",
                Comments = "Running task without scheduler process",
                AssignedToUserId = 2,
                InitiatedByUserId = 2,
                Priority = (int)OnlineContract.Helpers.TaskPriority.Normal,
                Status = (int)OnlineContract.Helpers.TaskStatus.Started,
                InputDt = DateTime.UtcNow,
                Stamp = 0
            });
            db.SaveChanges();

            var (client, cookie) = await _factory.CreateAuthenticatedClientAsync("admin", "Password1");
            await EnsureAuthCookieAsync(client, cookie ?? "");

            var resp = await client.PostAsync("/api/processlog/9601/cancel", null);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            // Verify task status was updated even without scheduler process
            using var scope2 = _factory.Services.CreateScope();
            var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
            
            var updatedTask = await db2.Tasks.FindAsync(9601);
            Assert.NotNull(updatedTask);
            Assert.Equal((int)OnlineContract.Helpers.TaskStatus.Cancelled, updatedTask!.Status);
        }

        [Fact]
        public async Task Get_ReturnsSystemUser_ForInitiatedByUserId2()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            db.Tasks.Add(new TaskItem
            {
                Id = 9701,
                Subject = "EOM Report Generation",
                Comments = "System initiated task",
                AssignedToUserId = 2,
                InitiatedByUserId = 2, // System user
                Priority = (int)OnlineContract.Helpers.TaskPriority.Normal,
                Status = (int)OnlineContract.Helpers.TaskStatus.Started,
                InputDt = DateTime.UtcNow,
                Stamp = 0
            });
            db.SaveChanges();

            var (client, cookie) = await _factory.CreateAuthenticatedClientAsync("admin", "Password1");
            await EnsureAuthCookieAsync(client, cookie ?? "");

            var resp = await client.GetAsync("/api/processlog?page=1&pageSize=100");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            var data = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            var items = data.GetProperty("items");

            bool found = false;
            foreach (var item in items.EnumerateArray())
            {
                if (item.GetProperty("id").GetInt32() == 9701)
                {
                    Assert.Equal("System", item.GetProperty("initiatedBy").GetString());
                    found = true;
                    break;
                }
            }
            Assert.True(found, "Task 9701 not found in results");
        }
    }
}
