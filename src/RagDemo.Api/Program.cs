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
app.UseStaticFiles();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.MapGet("/health", () => TypedResults.Ok(new { status = "ok" })).WithTags("Health");
app.MapDocumentEndpoints();
app.MapAskEndpoints();

app.Run();

public partial class Program;
