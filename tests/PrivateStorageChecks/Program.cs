using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.Identity;
using WeldingThozhilalarSangam.Api.Services;
using WeldingThozhilalarSangam.Api.Controllers;

var index = new MemoryIndex();
var handler = new CloudHandler();
var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Cloudinary:CloudName"] = "synthetic",
    ["Cloudinary:ApiKey"] = "test-key",
    ["Cloudinary:ApiSecret"] = "test-secret"
}).Build();
var storage = new CloudinaryPrivateUploadStorage(new HttpClient(handler), index, config);
Check(UploadAccessRules.MemberCanRead(true, false), "own profile allowed");
Check(!UploadAccessRules.MemberCanRead(false, false), "cross-member profile denied");
Check(!UploadAccessRules.MemberCanRead(true, true), "Aadhaar and payment proof denied to members");
var productionValues = new Dictionary<string, string?>
{
    ["Admin:Username"] = "synthetic-admin",
    ["Admin:PasswordHash"] = new PasswordHasher<string>().HashPassword("synthetic", "synthetic-test-password"),
    ["Admin:JwtKey"] = "SyntheticOnlyKey_0123456789_abcdefghijklm",
    ["Admin:Issuer"] = "https://api.example.test",
    ["Admin:Audience"] = "https://example.test",
    ["Admin:ExpiryHours"] = "8",
    ["Cors:AllowedOrigins:0"] = "https://example.test",
    ["MongoDb:ConnectionString"] = "mongodb://synthetic:synthetic@localhost",
    ["MongoDb:DatabaseName"] = "WTS_Synthetic",
    ["Uploads:Provider"] = "Local"
};
try
{
    ProductionConfiguration.Validate(new ConfigurationBuilder().AddInMemoryCollection(productionValues).Build(), new SyntheticHost());
    throw new Exception("Production accepted local ephemeral storage");
}
catch (InvalidOperationException ex) when (ex.Message.Contains("Cloudinary")) { }
productionValues["Uploads:Provider"] = "Cloudinary";
productionValues["Cloudinary:CloudName"] = "synthetic";
productionValues["Cloudinary:ApiKey"] = "synthetic-key";
productionValues["Cloudinary:ApiSecret"] = "synthetic-secret";
ProductionConfiguration.Validate(new ConfigurationBuilder().AddInMemoryCollection(productionValues).Build(), new SyntheticHost());
var bytes = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 0 };
var file = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "synthetic.png")
{ Headers = new HeaderDictionary(), ContentType = "image/png" };
var reference = await storage.SaveAsync(file, "profile");
Check(reference.StartsWith("/uploads/profile-") && !reference.Contains("asset-synthetic"), "opaque reference");
Check(handler.UploadPath == "/v1_1/synthetic/image/authenticated", "authenticated upload endpoint");
Check(index.Records.Count == 1, "durable mapping");
var secondInstance = new CloudinaryPrivateUploadStorage(new HttpClient(handler), index, config);
var retrieved = await secondInstance.ReadAsync(Path.GetFileName(reference));
Check(retrieved?.Bytes.SequenceEqual(bytes) == true, "read after service recreation");
Check(handler.DownloadUsedPost && handler.BasicAuthUsed, "server-authenticated download");
await storage.DeleteAsync(reference);
Check(index.Records.Count == 0 && handler.DestroyCount == 1, "remote delete and mapping removal");
var pdfBytes = Encoding.ASCII.GetBytes("%PDF-1.4 synthetic");
var pdf = new FormFile(new MemoryStream(pdfBytes), 0, pdfBytes.Length, "file", "synthetic.pdf")
{ Headers = new HeaderDictionary(), ContentType = "application/pdf" };
var pdfReference = await storage.SaveAsync(pdf, "payment-proof");
Check(handler.UploadPath == "/v1_1/synthetic/raw/authenticated", "private raw PDF upload endpoint");
await storage.DeleteAsync(pdfReference);
Check(index.Records.Count == 0, "PDF mapping cleanup");
handler.FailUpload = true;
try { await storage.SaveAsync(file, "aadhaar"); throw new Exception("Upload failure was accepted"); }
catch (HttpRequestException) { Check(index.Records.Count == 0, "upload failure creates no mapping"); }
handler.FailUpload = false;
index.FailAdd = true;
try { await storage.SaveAsync(file, "aadhaar"); throw new Exception("Index failure was accepted"); }
catch (InvalidOperationException) { Check(handler.DestroyCount == 3, "index failure destroys remote asset"); }
Console.WriteLine("Private storage checks passed: authenticated image/PDF upload, download, opaque references, recreation, deletion, upload failure, mapping rollback, access rules, and production local-storage rejection.");
if (Environment.GetEnvironmentVariable("WTS_CLOUDINARY_LIVE_TEST") == "1")
    await CloudinaryLiveChecks.RunAsync();

static void Check(bool condition, string label)
{
    if (!condition) throw new Exception($"Failed: {label}");
}

sealed class MemoryIndex : IPrivateUploadIndex
{
    public Dictionary<string, PrivateUploadRecord> Records { get; } = new();
    public bool FailAdd { get; set; }
    public Task AddAsync(PrivateUploadRecord record, CancellationToken ct)
    {
        if (FailAdd) throw new InvalidOperationException("Synthetic index failure");
        Records.Add(record.Filename, record);
        return Task.CompletedTask;
    }
    public Task<PrivateUploadRecord?> FindAsync(string filename, CancellationToken ct) =>
        Task.FromResult(Records.GetValueOrDefault(filename));
    public Task RemoveAsync(string filename, CancellationToken ct)
    {
        Records.Remove(filename);
        return Task.CompletedTask;
    }
}

sealed class CloudHandler : HttpMessageHandler
{
    public bool FailUpload { get; set; }
    public string UploadPath { get; private set; } = "";
    public bool DownloadUsedPost { get; private set; }
    public bool BasicAuthUsed { get; private set; }
    public int DestroyCount { get; private set; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var path = request.RequestUri!.AbsolutePath;
        BasicAuthUsed = request.Headers.Authorization?.Scheme == "Basic";
        if (path.EndsWith("/authenticated"))
        {
            UploadPath = path;
            if (FailUpload) return new HttpResponseMessage(HttpStatusCode.BadGateway);
            var body = await request.Content!.ReadAsStringAsync(ct);
            if (!body.Contains("public_id") || !body.Contains("filename=")) throw new Exception("Upload form missing expected fields");
            return Json(path.Contains("/raw/") ?
                "{\"asset_id\":\"asset-synthetic\",\"type\":\"authenticated\",\"resource_type\":\"raw\"}" :
                "{\"asset_id\":\"asset-synthetic\",\"type\":\"authenticated\",\"resource_type\":\"image\"}");
        }
        if (path.EndsWith("/asset/download"))
        {
            DownloadUsedPost = request.Method == HttpMethod.Post;
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new ByteArrayContent(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 0 }) };
        }
        if (path.EndsWith("/destroy"))
        {
            DestroyCount++;
            return Json("{\"result\":\"ok\"}");
        }
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }
    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

sealed class SyntheticHost : IWebHostEnvironment
{
    public string EnvironmentName { get; set; } = "Production";
    public string ApplicationName { get; set; } = "Synthetic";
    public string WebRootPath { get; set; } = Path.GetTempPath();
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public string ContentRootPath { get; set; } = Path.GetTempPath();
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
