using System.Linq;
using System.Net.Http.Json;
using Xunit;

namespace OnlineContract.Tests
{
    public class NotesActivationTests : IClassFixture<WebAppFactory>
    {
        private readonly WebAppFactory _factory;
        public NotesActivationTests(WebAppFactory factory) { _factory = factory; }

        [Fact]
        public async System.Threading.Tasks.Task Create_And_Toggle_Note_Active_State()
        {
            var (client, authCookie) = await _factory.CreateAuthenticatedClientAsync("admin", "Password1");
            if (!string.IsNullOrEmpty(authCookie)) client.DefaultRequestHeaders.Add("Cookie", ExtractCookie(authCookie!, ".OnlineContract.Auth"));

            // Create a product-linked note
            var createResp = await client.PostAsJsonAsync("/api/notes", new { Subject = "Test", Comment = "Test comment", ProductId = 100 });
            createResp.EnsureSuccessStatusCode();
            var created = await createResp.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>();
            int id = (int)created!["id"]!;

            // Toggle deactivate via PUT
            var updResp = await client.PutAsJsonAsync($"/api/notes/{id}", new { IsActive = false });
            updResp.EnsureSuccessStatusCode();

            // Verify via list filter
            var list1 = await client.GetFromJsonAsync<System.Text.Json.Nodes.JsonObject>("/api/notes?productId=100&page=1&pageSize=50");
            var found = list1!["items"]!.AsArray().First(x => (int)x!.AsObject()["id"]! == id)!.AsObject();
            Assert.False((bool)found["isActive"]!);

            // Reactivate
            var updResp2 = await client.PutAsJsonAsync($"/api/notes/{id}", new { IsActive = true });
            updResp2.EnsureSuccessStatusCode();
            var list2 = await client.GetFromJsonAsync<System.Text.Json.Nodes.JsonObject>("/api/notes?productId=100&page=1&pageSize=50");
            var found2 = list2!["items"]!.AsArray().First(x => (int)x!.AsObject()["id"]! == id)!.AsObject();
            Assert.True((bool)found2["isActive"]!);
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