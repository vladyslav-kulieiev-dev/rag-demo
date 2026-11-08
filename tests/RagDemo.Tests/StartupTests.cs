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
            Task.FromResult<StoredFile?>(null);
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
