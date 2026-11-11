using System.Net;
using System.Net.Http.Json;
using Google.Cloud.DiscoveryEngine.V1;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RagDemo.Api.Ask;
using RagDemo.Api.Documents;

namespace RagDemo.Tests;

public class StartupTests
{
    private static WebApplicationFactory<Program> Factory(Dictionary<string, string?> settings) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            foreach (var (key, value) in settings) b.UseSetting(key, value);
            // Swap the Google-backed services for fakes so tests need no cloud credentials.
            b.ConfigureTestServices(services =>
            {
                services.AddSingleton<IDocumentIndexer, FakeIndexer>();
                services.AddSingleton<IAnswerClient, FakeAnswerClient>();
            });
        });

    private sealed class FakeIndexer : IDocumentIndexer
    {
        public Task<UploadResult> UploadAndImportAsync(string fileName, Stream content, CancellationToken ct) =>
            Task.FromResult(new UploadResult($"gs://bucket/uploads/{fileName}", "operations/fake"));

        public Task<IReadOnlyList<IndexedDocument>> ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<IndexedDocument>>([]);

        public Task<StoredFile?> DownloadAsync(string gcsUri, CancellationToken ct) =>
            Task.FromResult(gcsUri switch
            {
                "gs://bucket/uploads/notes.txt" => new StoredFile("Zażółć gęślą jaźń"u8.ToArray(), "text/plain"),
                "gs://bucket/uploads/page.html" => new StoredFile("<script>alert(1)</script>"u8.ToArray(), "text/html"),
                "gs://bucket/uploads/doc.pdf" => new StoredFile("%PDF-1.7"u8.ToArray(), "application/pdf"),
                _ => (StoredFile?)null,
            });
    }

    private static Task<HttpResponseMessage> GetFile(HttpClient client, string uri) =>
        client.GetAsync("/api/documents/file?uri=" + Uri.EscapeDataString(uri));

    [Fact]
    public async Task Text_files_are_served_as_utf8()
    {
        using var factory = Factory(ValidSettings);
        var response = await GetFile(factory.CreateClient(), "gs://bucket/uploads/notes.txt");

        Assert.Equal("text/plain; charset=utf-8", response.Content.Headers.ContentType!.ToString());
        Assert.Equal("Zażółć gęślą jaźń", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Html_files_are_sandboxed_and_pdfs_are_not()
    {
        using var factory = Factory(ValidSettings);
        var client = factory.CreateClient();

        var html = await GetFile(client, "gs://bucket/uploads/page.html");
        Assert.Equal("sandbox", html.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("nosniff", html.Headers.GetValues("X-Content-Type-Options").Single());

        var pdf = await GetFile(client, "gs://bucket/uploads/doc.pdf");
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType!.MediaType);
        Assert.False(pdf.Headers.Contains("Content-Security-Policy"));
    }

    [Fact]
    public async Task Unknown_files_return_404()
    {
        using var factory = Factory(ValidSettings);
        var response = await GetFile(factory.CreateClient(), "gs://other-bucket/secret.pdf");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Pdfjs_files_are_served_and_pages_are_not_cached()
    {
        using var factory = Factory(ValidSettings);
        var client = factory.CreateClient();

        var translation = await client.GetAsync("/pdfjs/web/locale/en-US/viewer.ftl");
        Assert.Equal(HttpStatusCode.OK, translation.StatusCode);

        var page = await client.GetAsync("/source.html");
        Assert.Equal("no-cache", page.Headers.CacheControl!.ToString());
    }

    private sealed class FakeAnswerClient : IAnswerClient
    {
        public Task<Answer> AnswerAsync(string question, CancellationToken ct) => Task.FromResult(new Answer());
    }

    private static readonly Dictionary<string, string?> ValidSettings = new()
    {
        ["Gcp:ProjectId"] = "test-project",
        ["Gcp:DataStoreId"] = "store",
        ["Gcp:EngineId"] = "engine",
        ["Gcp:Bucket"] = "bucket",
    };

    [Fact]
    public void Missing_gcp_settings_stop_the_app_from_starting()
    {
        using var factory = Factory([]);
        var error = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
        Assert.Contains("ProjectId", error.Message);
    }

    [Fact]
    public async Task Health_returns_200_with_valid_settings()
    {
        using var factory = Factory(ValidSettings);
        var response = await factory.CreateClient().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ask_rejects_empty_question()
    {
        using var factory = Factory(ValidSettings);
        var response = await factory.CreateClient().PostAsJsonAsync("/api/ask", new { question = " " });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Upload_requires_api_key_when_configured()
    {
        using var factory = Factory(new(ValidSettings) { ["Upload:ApiKey"] = "secret" });
        using var content = new MultipartFormDataContent { { new StringContent("hi"), "file", "a.txt" } };
        var response = await factory.CreateClient().PostAsync("/api/documents", content);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Upload_rejects_disallowed_file_type()
    {
        using var factory = Factory(ValidSettings);
        using var content = new MultipartFormDataContent { { new StringContent("MZ"), "file", "tool.exe" } };
        var response = await factory.CreateClient().PostAsync("/api/documents", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Upload_accepts_valid_file_with_202()
    {
        using var factory = Factory(new(ValidSettings) { ["Upload:ApiKey"] = "secret" });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "secret");
        using var content = new MultipartFormDataContent { { new StringContent("hello"), "file", "notes.txt" } };

        var response = await client.PostAsync("/api/documents", content);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UploadResult>();
        Assert.Equal("operations/fake", body!.OperationName);
    }

    [Fact]
    public async Task Ask_with_nothing_found_returns_no_answer_message()
    {
        using var factory = Factory(ValidSettings);
        var response = await factory.CreateClient().PostAsJsonAsync("/api/ask", new { question = "What is the moon made of?" });
        var body = await response.Content.ReadFromJsonAsync<AskResponse>();
        Assert.False(body!.Found);
        Assert.Equal(CitationMapper.NoAnswerMessage, body.Answer);
    }

    [Fact]
    public async Task Daily_ask_limit_is_shared_by_all_callers_and_explains_itself()
    {
        using var factory = Factory(new(ValidSettings) { ["RateLimit:PermitLimit"] = "100", ["RateLimit:DailyAskLimit"] = "2" });
        var client = factory.CreateClient();

        for (var i = 0; i < 2; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/ask", new { question = "Q?" })).StatusCode);

        var rejected = await client.PostAsJsonAsync("/api/ask", new { question = "Q?" });
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Contains("daily", await rejected.Content.ReadAsStringAsync());

        // Other endpoints don't count toward the daily question limit.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/documents")).StatusCode);
    }

    [Fact]
    public async Task Rate_limiter_returns_429_after_the_limit()
    {
        using var factory = Factory(new(ValidSettings) { ["RateLimit:PermitLimit"] = "3" });
        var client = factory.CreateClient();

        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
            codes.Add((await client.PostAsJsonAsync("/api/ask", new { question = "" })).StatusCode);

        Assert.Equal([HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.TooManyRequests], codes);
    }
}
