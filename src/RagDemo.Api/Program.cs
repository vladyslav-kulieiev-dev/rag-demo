using Microsoft.AspNetCore.StaticFiles;
using RagDemo.Api;
using RagDemo.Api.Ask;
using RagDemo.Api.Documents;
using RagDemo.Api.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<GcpSettings>()
    .Bind(builder.Configuration.GetSection(GcpSettings.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.Configure<UploadSettings>(builder.Configuration.GetSection(UploadSettings.SectionName));

builder.Services.AddGcpClients();
builder.Services.AddSingleton<IDocumentIndexer, DocumentIndexer>();
builder.Services.AddSingleton<IAnswerClient, AnswerClient>();

// Let multipart uploads up to the size limit through; the validator gives the friendly message.
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
    o.MultipartBodyLengthLimit = UploadValidator.MaxBytes + 1024 * 1024);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = UploadValidator.MaxBytes + 1024 * 1024);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GcpErrorHandler>();
builder.Services.AddRagDemoRateLimiting(builder.Configuration);
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    // PDF.js ships file types ASP.NET Core doesn't know (translations, font and character maps);
    // without these mappings they return 404.
    ContentTypeProvider = PdfJsContentTypes(),
    // Pages and scripts are checked with the server on every load (a cheap 304 when unchanged),
    // so visitors get a new version right after a deploy instead of a cached old one.
    OnPrepareResponse = context =>
    {
        var extension = Path.GetExtension(context.File.Name);
        if (extension is ".html" or ".mjs" or ".js" or ".css")
            context.Context.Response.Headers.CacheControl = "no-cache";
    },
});
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.MapGet("/health", () => TypedResults.Ok(new { status = "ok" })).WithTags("Health");
app.MapDocumentEndpoints();
app.MapAskEndpoints();

app.Run();

static FileExtensionContentTypeProvider PdfJsContentTypes()
{
    var provider = new FileExtensionContentTypeProvider();
    provider.Mappings[".ftl"] = "text/plain; charset=utf-8";
    provider.Mappings[".bcmap"] = "application/octet-stream";
    provider.Mappings[".pfb"] = "application/octet-stream";
    provider.Mappings[".icc"] = "application/vnd.iccprofile";
    return provider;
}

public partial class Program;
