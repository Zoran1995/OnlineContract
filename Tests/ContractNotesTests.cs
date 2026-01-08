using System.Net.Http.Json;
using Xunit;

namespace OnlineContract.Tests;

public class ContractNotesTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;
    public ContractNotesTests(WebAppFactory factory) { _factory = factory; }

    [Fact]
    public async Task Notes_List_Add_Update_Delete_Workflow()
    {
        var (client, authCookie) = await _factory.CreateAuthenticatedClientAsync("testuser", "Password1");
        if (!string.IsNullOrEmpty(authCookie)) client.DefaultRequestHeaders.Add("Cookie", ExtractCookie(authCookie!, ".OnlineContract.Auth"));

        // Initial list
        var list1 = await client.GetAsync("/api/contracts/500/notes?q=&page=1&pageSize=10");
        list1.EnsureSuccessStatusCode();
        var l1 = await list1.Content.ReadFromJsonAsync<NotesListDto>();
        Assert.NotNull(l1);

        // Add note
        var addPayload = new
        {
            Add = new[] { new { Comment = "Hello", Subject = "Test subject", IsActive = true } },
            Update = Array.Empty<object>(),
            Delete = Array.Empty<object>(),
            SetMainId = (int?)null,
            SetMainStamp = (int?)null
        };
        var addResp = await client.PutAsJsonAsync("/api/contracts/500/notes", addPayload);
        addResp.EnsureSuccessStatusCode();

        // List and find the created note
        var list2 = await client.GetAsync("/api/contracts/500/notes?page=1&pageSize=10");
        list2.EnsureSuccessStatusCode();
        var l2 = await list2.Content.ReadFromJsonAsync<NotesListDto>();
        Assert.NotNull(l2);
        Assert.True(l2!.items.Count >= 1);
        var note = l2.items.First(x => x.Comment == "Hello");

        // Update note
        var updPayload = new
        {
            Add = Array.Empty<object>(),
            Update = new[] { new { Id = note.Id, Comment = "Updated", IsActive = true, IsDeleted = false, Stamp = note.Stamp } },
            Delete = Array.Empty<object>(),
            SetMainId = (int?)null,
            SetMainStamp = (int?)null
        };
        var updResp = await client.PutAsJsonAsync("/api/contracts/500/notes", updPayload);
        updResp.EnsureSuccessStatusCode();

        // List and verify update
        var list3 = await client.GetAsync("/api/contracts/500/notes?page=1&pageSize=10");
        list3.EnsureSuccessStatusCode();
        var l3 = await list3.Content.ReadFromJsonAsync<NotesListDto>();
        var updated = l3!.items.First(x => x.Id == note.Id);
        Assert.Equal("Updated", updated.Comment);

        // Delete note
        var delPayload = new
        {
            Add = Array.Empty<object>(),
            Update = Array.Empty<object>(),
            Delete = new[] { new { Id = note.Id, Stamp = updated.Stamp } },
            SetMainId = (int?)null,
            SetMainStamp = (int?)null
        };
        var delResp = await client.PutAsJsonAsync("/api/contracts/500/notes", delPayload);
        delResp.EnsureSuccessStatusCode();

        // Verify not listed anymore
        var list4 = await client.GetAsync("/api/contracts/500/notes?page=1&pageSize=10");
        list4.EnsureSuccessStatusCode();
        var l4 = await list4.Content.ReadFromJsonAsync<NotesListDto>();
        Assert.DoesNotContain(l4!.items, x => x.Id == note.Id);
    }

    private static string ExtractCookie(string setCookieHeader, string cookieName)
    {
        var parts = setCookieHeader.Split(';');
        var nv = parts[0];
        if (nv.StartsWith(cookieName + "=")) return nv;
        return nv;
    }
}

file sealed class NotesListDto
{
    public List<NoteRow> items { get; set; } = new();
    public int totalCount { get; set; }
    public int totalPages { get; set; }
}

file sealed class NoteRow
{
    public int Id { get; set; }
    public string Comment { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsMain { get; set; }
    public bool IsDeleted { get; set; }
    public string InputDt { get; set; } = string.Empty;
    public string InputUserCode { get; set; } = string.Empty;
    public string LastModifiedByCode { get; set; } = string.Empty;
    public string LastUpdatedDt { get; set; } = string.Empty;
    public int Stamp { get; set; }
}
