using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDirectoryBrowser();
var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/healthz", () => Results.Ok("ok"));

app.MapPost("/upload", async (HttpRequest req) =>
{
    if (!req.HasFormContentType) return Results.BadRequest("Expected multipart/form-data");
    var form = await req.ReadFormAsync();
    var file = form.Files["file"];
    if (file is null || file.Length == 0) return Results.BadRequest("No file");

    // Read config from env/app settings
    var account = Environment.GetEnvironmentVariable("STORAGE_ACCOUNT_NAME");   // e.g., stimg12345
    var containerName = Environment.GetEnvironmentVariable("CONTAINER_NAME") ?? "uploads";
    if (string.IsNullOrWhiteSpace(account)) return Results.Problem("STORAGE_ACCOUNT_NAME not set", statusCode: 500);

    // Managed Identity auth, private endpoint reachable via VNet Integration + Private DNS
    var blobUri = new Uri($"https://{account}.blob.core.windows.net");
    var service = new BlobServiceClient(blobUri, new DefaultAzureCredential());
    var container = service.GetBlobContainerClient(containerName);
    await container.CreateIfNotExistsAsync(PublicAccessType.None);

    var safeName = Path.GetFileName(file.FileName);
    var unique = $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}-{safeName}";
    var blob = container.GetBlobClient(unique);
    using var stream = file.OpenReadStream();
    await blob.UploadAsync(stream, new BlobUploadOptions {
        HttpHeaders = new BlobHttpHeaders { ContentType = file.ContentType ?? "application/octet-stream" }
    });

    return Results.Ok(new { name = unique, url = blob.Uri.ToString() });
});

app.Run();
