using System.Linq;
using System.Net.Http.Json;
using Xunit;

namespace OnlineContract.Tests
{
    public class ApprovalRulesApiTests : IClassFixture<WebAppFactory>
    {
        private readonly WebAppFactory _factory;
        public ApprovalRulesApiTests(WebAppFactory factory) { _factory = factory; }

        [Fact]
        public async System.Threading.Tasks.Task List_Returns_Items_With_Status_And_Context()
        {
            var (client, authCookie) = await _factory.CreateAuthenticatedClientAsync("admin", "Password1");
            if (!string.IsNullOrEmpty(authCookie)) client.DefaultRequestHeaders.Add("Cookie", ExtractCookie(authCookie!, ".OnlineContract.Auth"));
            var resp = await client.GetAsync("/api/approval-rules?page=1&pageSize=50");
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>();
            Assert.NotNull(json);
            var items = json!["items"]!.AsArray();
            Assert.True(items.Count >= 1);
            var first = items.First()!.AsObject();
            Assert.NotNull(first["id"]);
            Assert.NotNull(first["name"]);
            Assert.NotNull(first["description"]);
            Assert.NotNull(first["isActive"]);
            Assert.NotNull(first["contextId"]);
        }

        [Fact]
        public async System.Threading.Tasks.Task Get_ById_Returns_Context_And_Fields()
        {
            var (client, authCookie) = await _factory.CreateAuthenticatedClientAsync("admin", "Password1");
            if (!string.IsNullOrEmpty(authCookie)) client.DefaultRequestHeaders.Add("Cookie", ExtractCookie(authCookie!, ".OnlineContract.Auth"));
            // Pick ID from list
            var list = await client.GetFromJsonAsync<System.Text.Json.Nodes.JsonObject>("/api/approval-rules?page=1&pageSize=50");
            var id = (int)list!["items"]!.AsArray().First()!.AsObject()["id"]!;
            var rule = await client.GetFromJsonAsync<System.Text.Json.Nodes.JsonObject>($"/api/approval-rules/{id}");
            Assert.NotNull(rule!["contextId"]);
            Assert.NotNull(rule!["amtThreshold"]);
            Assert.NotNull(rule!["pctThreshold"]);
        }

        [Fact]
        public async System.Threading.Tasks.Task Activate_Deactivate_Toggles_Status()
        {
            var (client, authCookie) = await _factory.CreateAuthenticatedClientAsync("admin", "Password1");
            if (!string.IsNullOrEmpty(authCookie)) client.DefaultRequestHeaders.Add("Cookie", ExtractCookie(authCookie!, ".OnlineContract.Auth"));
            var list = await client.GetFromJsonAsync<System.Text.Json.Nodes.JsonObject>("/api/approval-rules?page=1&pageSize=50");
            var obj = list!["items"]!.AsArray().First()!.AsObject();
            int id = (int)obj["id"]!;
            int stamp = (int)obj["stamp"]!;
            bool isActive = (bool)obj["isActive"]!;
            var kind = isActive ? "deactivate" : "activate";
            var resp = await client.PostAsync($"/api/approval-rules/{id}/{kind}?stamp={stamp}", new System.Net.Http.StringContent(""));
            resp.EnsureSuccessStatusCode();
            var after = await client.GetFromJsonAsync<System.Text.Json.Nodes.JsonObject>("/api/approval-rules?page=1&pageSize=50");
            var updated = after!["items"]!.AsArray().First(x => (int)x!.AsObject()["id"]! == id)!.AsObject();
            Assert.NotEqual(isActive, (bool)updated["isActive"]!);
        }

        [Fact]
        public async System.Threading.Tasks.Task Delete_Marks_Deleted_And_Excludes_From_List()
        {
            var (client, authCookie) = await _factory.CreateAuthenticatedClientAsync("admin", "Password1");
            if (!string.IsNullOrEmpty(authCookie)) client.DefaultRequestHeaders.Add("Cookie", ExtractCookie(authCookie!, ".OnlineContract.Auth"));
            var list = await client.GetFromJsonAsync<System.Text.Json.Nodes.JsonObject>("/api/approval-rules?page=1&pageSize=50");
            var obj = list!["items"]!.AsArray().Last()!.AsObject();
            int id = (int)obj["id"]!;
            int stamp = (int)obj["stamp"]!;
            var resp = await client.PostAsync($"/api/approval-rules/{id}/delete?stamp={stamp}", new System.Net.Http.StringContent(""));
            resp.EnsureSuccessStatusCode();
            var after = await client.GetFromJsonAsync<System.Text.Json.Nodes.JsonObject>("/api/approval-rules?page=1&pageSize=50");
            var exists = after!["items"]!.AsArray().Any(x => (int)x!.AsObject()["id"]! == id);
            Assert.False(exists);
        }

        [Fact]
        public async System.Threading.Tasks.Task Concurrency_Fails_On_Wrong_Stamp()
        {
            var (client, authCookie) = await _factory.CreateAuthenticatedClientAsync("admin", "Password1");
            if (!string.IsNullOrEmpty(authCookie)) client.DefaultRequestHeaders.Add("Cookie", ExtractCookie(authCookie!, ".OnlineContract.Auth"));
            var list = await client.GetFromJsonAsync<System.Text.Json.Nodes.JsonObject>("/api/approval-rules?page=1&pageSize=50");
            var obj = list!["items"]!.AsArray().First()!.AsObject();
            int id = (int)obj["id"]!;
            // Wrong stamp
            var resp = await client.PostAsync($"/api/approval-rules/{id}/activate?stamp=999999", new System.Net.Http.StringContent(""));
            var body = await resp.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>();
            Assert.True(body!["success"]!.GetValue<bool>() == false);
        }

        private static string ExtractCookie(string setCookieHeader, string cookieName)
        {
            var parts = setCookieHeader.Split(';');
            var nv = parts[0];
            if (nv.StartsWith(cookieName + "=")) return nv;
            return nv;
        }
    }
}
