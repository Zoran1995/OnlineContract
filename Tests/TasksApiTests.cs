using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using OnlineContract.Data;
using OnlineContract.Models;
using Xunit;

namespace OnlineContract.Tests
{
    public class TasksApiTests : IClassFixture<WebAppFactory>
    {
        private readonly WebAppFactory _factory;
        public TasksApiTests(WebAppFactory factory) { _factory = factory; }

        private static async Task EnsureAuthCookieAsync(HttpClient client)
        {
            var resp = await client.PostAsJsonAsync("/api/login", new OnlineContract.Dtos.LoginDto { Code = "testuser", Password = "Password1" });
            if (!resp.Headers.TryGetValues("Set-Cookie", out var values)) return;
            foreach (var v in values)
            {
                var parts = v.Split(';');
                var nv = parts[0];
                if (nv.StartsWith(".OnlineContract.Auth="))
                {
                    client.DefaultRequestHeaders.Remove("Cookie");
                    client.DefaultRequestHeaders.Add("Cookie", nv);
                    break;
                }
            }
        }

        [Fact]
        public async Task Search_AssignedTo_CurrentUser_Filters()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Seed two tasks: one for testuser(123), one for admin(124)
            db.Tasks.Add(new TaskItem { Id = 2001, Subject = "My Task", Comments = "A", AssignedToUserId = 123, InitiatedByUserId = 124, Priority = (int)OnlineContract.Helpers.TaskPriority.High, Status = (int)OnlineContract.Helpers.TaskStatus.Started, InputDt = DateTime.UtcNow, Stamp = 0 });
            db.Tasks.Add(new TaskItem { Id = 2002, Subject = "Other", Comments = "B", AssignedToUserId = 124, InitiatedByUserId = 123, Priority = (int)OnlineContract.Helpers.TaskPriority.Low, Status = (int)OnlineContract.Helpers.TaskStatus.NotStarted, InputDt = DateTime.UtcNow, Stamp = 0 });
            db.SaveChanges();

            var (client, _) = await _factory.CreateAuthenticatedClientAsync("testuser", "Password1");
            await EnsureAuthCookieAsync(client);
            var resp = await client.GetAsync("/api/tasks?page=1&pageSize=10&taskId=2001");
            Assert.True(resp.IsSuccessStatusCode);
            var doc = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            var items = doc.GetProperty("items");
            Assert.Equal(1, items.GetArrayLength());
            Assert.Equal(2001, items[0].GetProperty("id").GetInt32());
        }

        [Fact]
        public async Task Sorting_Subject_AscDesc_Works()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Tasks.Add(new TaskItem { Id = 2101, Subject = "Alpha", Comments = "", AssignedToUserId = 123, InitiatedByUserId = 124, Priority = (int)OnlineContract.Helpers.TaskPriority.Normal, Status = (int)OnlineContract.Helpers.TaskStatus.Started, InputDt = DateTime.UtcNow, Stamp = 0 });
            db.Tasks.Add(new TaskItem { Id = 2102, Subject = "Beta", Comments = "", AssignedToUserId = 123, InitiatedByUserId = 124, Priority = (int)OnlineContract.Helpers.TaskPriority.Normal, Status = (int)OnlineContract.Helpers.TaskStatus.Started, InputDt = DateTime.UtcNow, Stamp = 0 });
            db.SaveChanges();

            var (client, _) = await _factory.CreateAuthenticatedClientAsync("testuser", "Password1");
            await EnsureAuthCookieAsync(client);
            var normalId = (int)OnlineContract.Helpers.TaskPriority.Normal;
            var asc = await client.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/tasks?page=1&pageSize=10&sortBy=subject&sortDir=asc&priorityId={normalId}");
            Assert.Equal("Alpha", asc.GetProperty("items")[0].GetProperty("subject").GetString());
            var desc = await client.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/tasks?page=1&pageSize=10&sortBy=subject&sortDir=desc&priorityId={normalId}");
            Assert.Equal("Beta", desc.GetProperty("items")[0].GetProperty("subject").GetString());
        }

        [Fact]
        public async Task Modal_Loads_Basic_Fields()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Tasks.Add(new TaskItem { Id = 2201, Subject = "M", Comments = "C", AssignedToUserId = 123, InitiatedByUserId = 124, Priority = (int)OnlineContract.Helpers.TaskPriority.High, Status = (int)OnlineContract.Helpers.TaskStatus.Completed, InputDt = DateTime.UtcNow, ContractId = 500, Stamp = 0 });
            db.SaveChanges();

            var (client, _) = await _factory.CreateAuthenticatedClientAsync("testuser", "Password1");
            await EnsureAuthCookieAsync(client);
            var dto = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/tasks/2201");
            Assert.Equal(2201, dto.GetProperty("id").GetInt32());
            Assert.Equal("M", dto.GetProperty("subject").GetString());
            Assert.Equal("High", dto.GetProperty("priorityText").GetString());
            Assert.Equal(500, dto.GetProperty("contractId").GetInt32());
        }

        [Fact]
        public async Task Search_Includes_Owner_Assigned_Task()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Seed a task assigned to owner's id (admin 124)
            db.Tasks.Add(new TaskItem { Id = 2301, Subject = "Owner Task", Comments = "X", AssignedToUserId = 124, InitiatedByUserId = 123, Priority = (int)OnlineContract.Helpers.TaskPriority.Low, Status = (int)OnlineContract.Helpers.TaskStatus.Started, InputDt = DateTime.UtcNow, Stamp = 0 });
            db.SaveChanges();

            var (client, _) = await _factory.CreateAuthenticatedClientAsync("testuser", "Password1");
            await EnsureAuthCookieAsync(client);
            var resp = await client.GetAsync("/api/tasks?page=1&pageSize=10&taskId=2301");
            Assert.True(resp.IsSuccessStatusCode);
            var doc = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            var items = doc.GetProperty("items");
            Assert.Equal(1, items.GetArrayLength());
            Assert.Equal(2301, items[0].GetProperty("id").GetInt32());
        }
    }
}