using System.Linq;
using System.Net.Http.Json;
using Xunit;

namespace OnlineContract.Tests
{
    public class UsersActivationTests : IClassFixture<WebAppFactory>
    {
        private readonly WebAppFactory _factory;
        public UsersActivationTests(WebAppFactory factory) { _factory = factory; }

        [Fact]
        public async System.Threading.Tasks.Task Toggle_User_Active_State()
        {
            var (client, authCookie) = await _factory.CreateAuthenticatedClientAsync("admin", "Password1");
            if (!string.IsNullOrEmpty(authCookie)) client.DefaultRequestHeaders.Add("Cookie", ExtractCookie(authCookie!, ".OnlineContract.Auth"));

            var list = await client.GetFromJsonAsync<System.Text.Json.Nodes.JsonObject>("/api/users?page=1&pageSize=50");
            var first = list!["items"]!.AsArray().First(x => ((string?)x!.AsObject()["code"]!) != "admin")!.AsObject();
            int id = (int)first["id"]!;
            int stamp = (int)first["stamp"]!;
            bool isActive = (bool)first["isActive"]!;

            var kind = isActive ? "deactivate" : "activate";
            var resp = await client.PostAsync($"/api/users/{id}/{kind}?userId=124&stamp={stamp}", new System.Net.Http.StringContent(""));
            resp.EnsureSuccessStatusCode();

            var list2 = await client.GetFromJsonAsync<System.Text.Json.Nodes.JsonObject>("/api/users?page=1&pageSize=50");
            var updated = list2!["items"]!.AsArray().First(x => (int)x!.AsObject()["id"]! == id)!.AsObject();
            Assert.NotEqual(isActive, (bool)updated["isActive"]!);
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