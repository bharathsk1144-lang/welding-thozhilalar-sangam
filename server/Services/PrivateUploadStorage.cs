using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using WeldingThozhilalarSangam.Api.Data;

namespace WeldingThozhilalarSangam.Api.Services;

public record PrivateUpload(byte[] Bytes, string ContentType);

public interface IPrivateUploadStorage
{
    Task<string> SaveAsync(IFormFile file, string prefix, CancellationToken cancellationToken = default);
    Task<PrivateUpload?> ReadAsync(string filename, CancellationToken cancellationToken = default);
    Task DeleteAsync(string reference, CancellationToken cancellationToken = default);
}

public sealed class LocalPrivateUploadStorage(IConfiguration configuration, IWebHostEnvironment environment) : IPrivateUploadStorage
{
    private readonly string _root = Path.GetFullPath(configuration["Uploads:RootPath"] ??
        Path.Combine(environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot"), "uploads"));

    public async Task<string> SaveAsync(IFormFile file, string prefix, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_root);
        var name = $"{prefix}-{Guid.NewGuid():N}{Path.GetExtension(file.FileName).ToLowerInvariant()}";
        var path = Path.Combine(_root, name);
        try
        {
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await file.CopyToAsync(output, cancellationToken);
            return $"/uploads/{name}";
        }
        catch { File.Delete(path); throw; }
    }

    public async Task<PrivateUpload?> ReadAsync(string filename, CancellationToken cancellationToken = default)
    {
        if (!SafeName(filename)) return null;
        var path = Path.Combine(_root, filename);
        if (!File.Exists(path)) return null;
        if (new FileInfo(path).Length > 5 * 1024 * 1024) throw new InvalidOperationException("Private upload exceeds allowed size.");
        return new PrivateUpload(await File.ReadAllBytesAsync(path, cancellationToken), ContentType(filename));
    }

    public Task DeleteAsync(string reference, CancellationToken cancellationToken = default)
    {
        var name = Path.GetFileName(reference);
        if (reference == $"/uploads/{name}" && SafeName(name)) File.Delete(Path.Combine(_root, name));
        return Task.CompletedTask;
    }

    internal static bool SafeName(string name) => name.Length is > 0 and < 160 &&
        name == Path.GetFileName(name) && !name.Contains((char)92) && !name.Contains('/') && !name.Contains(':');

    internal static string ContentType(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg", ".png" => "image/png", ".webp" => "image/webp",
        ".pdf" => "application/pdf", _ => "application/octet-stream"
    };
}

// Server-only metadata: user documents retain only an opaque /uploads reference.
public sealed class PrivateUploadRecord
{
    [BsonId]
    public string Filename { get; set; } = string.Empty;
    public string AssetId { get; set; } = string.Empty;
    public string PublicId { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;
}

public interface IPrivateUploadIndex
{
    Task AddAsync(PrivateUploadRecord record, CancellationToken cancellationToken);
    Task<PrivateUploadRecord?> FindAsync(string filename, CancellationToken cancellationToken);
    Task RemoveAsync(string filename, CancellationToken cancellationToken);
}

public sealed class MongoPrivateUploadIndex(MongoContext mongo) : IPrivateUploadIndex
{
    public Task AddAsync(PrivateUploadRecord record, CancellationToken cancellationToken) =>
        mongo.PrivateUploads.InsertOneAsync(record, cancellationToken: cancellationToken);
    public async Task<PrivateUploadRecord?> FindAsync(string filename, CancellationToken cancellationToken) =>
        await mongo.PrivateUploads.Find(r => r.Filename == filename).FirstOrDefaultAsync(cancellationToken);
    public async Task RemoveAsync(string filename, CancellationToken cancellationToken)
    {
        await mongo.PrivateUploads.DeleteOneAsync(r => r.Filename == filename, cancellationToken);
    }
}

public sealed class CloudinaryPrivateUploadStorage : IPrivateUploadStorage
{
    private readonly HttpClient _http;
    private readonly IPrivateUploadIndex _records;
    private readonly string _cloud;
    private readonly ILogger<CloudinaryPrivateUploadStorage> _logger;

    public CloudinaryPrivateUploadStorage(HttpClient http, IPrivateUploadIndex records, IConfiguration config,
        ILogger<CloudinaryPrivateUploadStorage>? logger = null)
    {
        _http = http;
        _records = records;
        _logger = logger ?? NullLogger<CloudinaryPrivateUploadStorage>.Instance;
        _cloud = config["Cloudinary:CloudName"]?.Trim() ?? throw new InvalidOperationException("Cloudinary cloud name is required.");
        if (_cloud.Length == 0 || _cloud.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
            throw new InvalidOperationException("Cloudinary cloud name has an invalid format.");
        var key = config["Cloudinary:ApiKey"]?.Trim() ?? throw new InvalidOperationException("Cloudinary API key is required.");
        var secret = config["Cloudinary:ApiSecret"]?.Trim() ?? throw new InvalidOperationException("Cloudinary API secret is required.");
        if (key.Length == 0 || secret.Length == 0)
            throw new InvalidOperationException("Cloudinary API credentials are required.");
        _http.BaseAddress = new Uri("https://api.cloudinary.com/");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{key}:{secret}")));
        _http.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<string> SaveAsync(IFormFile file, string prefix, CancellationToken cancellationToken = default)
    {
        var filename = $"{prefix}-{Guid.NewGuid():N}{Path.GetExtension(file.FileName).ToLowerInvariant()}";
        var resource = file.ContentType == "application/pdf" ? "raw" : "image";
        using var form = new MultipartFormDataContent();
        var content = new StreamContent(file.OpenReadStream());
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(file.ContentType);
        form.Add(content, "file", filename);
        form.Add(new StringContent($"wts-private/{prefix}-{Guid.NewGuid():N}"), "public_id");
        form.Add(new StringContent("false"), "overwrite");
        form.Add(new StringContent("authenticated"), "type");
        using var response = await _http.PostAsync($"v1_1/{_cloud}/{resource}/upload", form, cancellationToken);
        await EnsureSuccessAsync(response, "upload", resource, cancellationToken);
        using var data = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var root = data.RootElement;
        var assetId = root.GetProperty("asset_id").GetString();
        var publicId = root.GetProperty("public_id").GetString();
        if (string.IsNullOrWhiteSpace(assetId) || root.GetProperty("type").GetString() != "authenticated" ||
            root.GetProperty("resource_type").GetString() != resource ||
            string.IsNullOrWhiteSpace(publicId) || !publicId.StartsWith("wts-private/", StringComparison.Ordinal))
        {
            if (!string.IsNullOrWhiteSpace(publicId)) await DestroyAsync(publicId, resource, CancellationToken.None);
            throw new InvalidOperationException("Private upload verification failed.");
        }
        try
        {
            await _records.AddAsync(new PrivateUploadRecord
            { Filename = filename, AssetId = assetId, PublicId = publicId, ResourceType = resource }, cancellationToken);
            return $"/uploads/{filename}";
        }
        catch
        {
            await DestroyAsync(publicId, resource, CancellationToken.None);
            throw;
        }
    }

    public async Task<PrivateUpload?> ReadAsync(string filename, CancellationToken cancellationToken = default)
    {
        if (!LocalPrivateUploadStorage.SafeName(filename)) return null;
        var record = await _records.FindAsync(filename, cancellationToken);
        if (record is null) return null;
        if (record.ResourceType is not ("image" or "raw"))
            throw new InvalidOperationException("Private upload resource type is invalid.");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"v1_1/{_cloud}/asset/download")
        { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["asset_id"] = record.AssetId }) };
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(response, "download", record.ResourceType, cancellationToken);
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await source.ReadAsync(buffer, cancellationToken)) != 0)
        {
            if (output.Length + count > 5 * 1024 * 1024) throw new InvalidOperationException("Private upload exceeds allowed size.");
            await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }
        return new PrivateUpload(output.ToArray(), LocalPrivateUploadStorage.ContentType(filename));
    }

    public async Task DeleteAsync(string reference, CancellationToken cancellationToken = default)
    {
        var name = Path.GetFileName(reference);
        if (reference != $"/uploads/{name}" || !LocalPrivateUploadStorage.SafeName(name)) return;
        var record = await _records.FindAsync(name, cancellationToken);
        if (record is null) return;
        if (record.ResourceType is not ("image" or "raw"))
            throw new InvalidOperationException("Private upload resource type is invalid.");
        if (string.IsNullOrWhiteSpace(record.PublicId))
            throw new InvalidOperationException("Private upload public ID is missing; deletion requires reconciliation.");
        await DestroyAsync(record.PublicId, record.ResourceType, cancellationToken);
        await _records.RemoveAsync(name, cancellationToken);
    }

    private async Task DestroyAsync(string publicId, string resource, CancellationToken cancellationToken)
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        { ["public_id"] = publicId, ["type"] = "authenticated", ["invalidate"] = "true" });
        using var response = await _http.PostAsync($"v1_1/{_cloud}/{resource}/destroy", form, cancellationToken);
        await EnsureSuccessAsync(response, "destroy", resource, cancellationToken);
        using var data = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var result = data.RootElement.GetProperty("result").GetString();
        if (result != "ok") throw new InvalidOperationException("Private upload deletion was not confirmed; mapping retained for reconciliation.");
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, string operation, string resource,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var errorText = string.Empty;
        if (response.Headers.TryGetValues("X-Cld-Error", out var headers))
            errorText = string.Join(' ', headers);
        try
        {
            using var data = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
            if (data.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object &&
                error.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
                errorText += " " + message.GetString();
        }
        catch (JsonException) { }
        var category = ClassifyError(errorText, response.StatusCode);
        // Never log the cloud name, provider text, asset URL, request body or credentials.
        var route = operation == "download" ? "/v1_1/{cloud_name}/asset/download" :
            $"/v1_1/{{cloud_name}}/{resource}/{operation}";
        _logger.LogWarning("Cloudinary {Operation} failed: HTTP {Status}, category {Category}, endpoint POST {Route}",
            operation, (int)response.StatusCode, category, route);
        throw new HttpRequestException($"Cloudinary {operation} failed: HTTP {(int)response.StatusCode}, category {category}.",
            null, response.StatusCode);
    }

    private static string ClassifyError(string message, System.Net.HttpStatusCode status)
    {
        if (message.Contains("api_secret mismatch", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("api secret mismatch", StringComparison.OrdinalIgnoreCase)) return "api-secret-mismatch";
        if (message.Contains("cloud name", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("cloud_name", StringComparison.OrdinalIgnoreCase)) return "cloud-configuration";
        if (message.Contains("signature", StringComparison.OrdinalIgnoreCase)) return "invalid-signature";
        if (message.Contains("timestamp", StringComparison.OrdinalIgnoreCase)) return "invalid-timestamp";
        if (message.Contains("public_id", StringComparison.OrdinalIgnoreCase)) return "public-id-parameter";
        if (message.Contains("asset_id", StringComparison.OrdinalIgnoreCase)) return "asset-id-parameter";
        if (message.Contains("delivery type", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("type", StringComparison.OrdinalIgnoreCase)) return "delivery-type-parameter";
        if (message.Contains("parameter", StringComparison.OrdinalIgnoreCase)) return "request-parameter";
        if (message.Contains("api key", StringComparison.OrdinalIgnoreCase) ||
            status == System.Net.HttpStatusCode.Unauthorized) return "authentication";
        if (message.Contains("quota", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("limit", StringComparison.OrdinalIgnoreCase)) return "quota";
        if (message.Contains("format", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("invalid file", StringComparison.OrdinalIgnoreCase)) return "file-validation";
        if (status == System.Net.HttpStatusCode.NotFound) return "endpoint-or-cloud";
        if (status == System.Net.HttpStatusCode.Forbidden) return "provider-policy";
        return "provider-error";
    }
}
