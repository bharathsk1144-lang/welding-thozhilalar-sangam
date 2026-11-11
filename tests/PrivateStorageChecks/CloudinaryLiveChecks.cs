using System.Net;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using WeldingThozhilalarSangam.Api.Data;
using WeldingThozhilalarSangam.Api.Models;
using WeldingThozhilalarSangam.Api.Services;

// Opt-in provider check. Uses only synthetic one-pixel PNGs and an in-memory index.
// No production MongoDB connection or member data is opened by this test.
internal static class CloudinaryLiveChecks
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/lZkAAAAASUVORK5CYII=");

    internal static async Task RunAsync()
    {
        var config = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        foreach (var key in new[] { "Cloudinary:CloudName", "Cloudinary:ApiKey", "Cloudinary:ApiSecret" })
            if (string.IsNullOrWhiteSpace(config[key]))
                throw new InvalidOperationException($"Live test requires {key.Replace(":", "__")} in the private environment.");

        var index = new LiveIndex();
        using var handler = new CaptureUploadHandler();
        using var client = new HttpClient(handler, disposeHandler: false);
        using var secondClient = new HttpClient(handler, disposeHandler: false);
        var storage = new CloudinaryPrivateUploadStorage(client, index, config);
        string? reference = null;
        string? restored = null;
        string? pdfReference = null;
        try
        {
            reference = await storage.SaveAsync(File(), "synthetic-live");
            Check(index.Count == 1, "authenticated upload index");
            Check(handler.DeliveryUrl is not null && handler.DeliveryUrl.AbsolutePath.Contains("/image/authenticated/"),
                "authenticated delivery type");

            using var publicClient = new HttpClient();
            var unsignedUrl = WithoutDeliverySignature(handler.DeliveryUrl!);
            using var unsigned = await publicClient.GetAsync(unsignedUrl);
            Check(!unsigned.IsSuccessStatusCode, "unsigned original must be denied");
            var transformed = new Uri(unsignedUrl.AbsoluteUri.Replace("/image/authenticated/", "/image/authenticated/w_2/"));
            using var unsignedTransform = await publicClient.GetAsync(transformed);
            Check(!unsignedTransform.IsSuccessStatusCode, "unsigned transformation must be denied");

            var initialRead = await storage.ReadAsync(Path.GetFileName(reference));
            var secondInstance = new CloudinaryPrivateUploadStorage(secondClient, index, config);
            var read = await secondInstance.ReadAsync(Path.GetFileName(reference));
            Check(IsOnePixelPng(read) && initialRead?.Bytes.SequenceEqual(read!.Bytes) == true,
                "authenticated image retrieval after service recreation");

            // Prove deletion before creating additional synthetic assets.
            var confirmedDeletes = handler.DestroyCount;
            await storage.DeleteAsync(reference);
            reference = null;
            Check(index.Count == 0 && handler.DestroyCount == confirmedDeletes + 1,
                "authenticated image deletion confirmed by provider");

            // Synthetic asset-only recovery: copy retrieved bytes into a new authenticated asset.
            restored = await secondInstance.SaveAsync(File(read!.Bytes), "synthetic-restore");
            var recovered = await storage.ReadAsync(Path.GetFileName(restored));
            Check(IsOnePixelPng(recovered), "synthetic asset recovery");
            if (Environment.GetEnvironmentVariable("WTS_CLOUDINARY_RESTORE_DRILL") == "1")
                await RunIsolatedRestoreAsync(restored, recovered!, index, storage, config);
            var pdfBytes = SyntheticPdf();
            pdfReference = await storage.SaveAsync(File(pdfBytes, "synthetic.pdf", "application/pdf"), "synthetic-pdf");
            Check(handler.DeliveryUrl is not null && handler.DeliveryUrl.AbsolutePath.Contains("/raw/authenticated/"),
                "authenticated PDF delivery type");
            using var unsignedPdf = await publicClient.GetAsync(WithoutDeliverySignature(handler.DeliveryUrl!));
            Check(!unsignedPdf.IsSuccessStatusCode, "unsigned PDF must be denied");
            var pdfRead = await secondInstance.ReadAsync(Path.GetFileName(pdfReference));
            Check(pdfRead?.Bytes.SequenceEqual(pdfBytes) == true, "authenticated PDF retrieval");
            index.FailAdd = true;
            var destroyCount = handler.DestroyCount;
            try { await storage.SaveAsync(File(), "synthetic-failed-index"); throw new Exception("Index failure was accepted"); }
            catch (InvalidOperationException) { Check(handler.DestroyCount == destroyCount + 1, "failed-index remote cleanup"); }
            index.FailAdd = false;
            Console.WriteLine("Live Cloudinary checks passed: authenticated image/PDF upload and retrieval, unsigned original/transformation/PDF denied, service recreation, synthetic asset recovery, and failed-index cleanup.");
        }
        finally
        {
            Exception? cleanupFailure = null;
            foreach (var item in new[] { pdfReference, restored, reference })
            {
                if (item is null) continue;
                try { await storage.DeleteAsync(item); }
                catch (Exception ex) { cleanupFailure ??= ex; }
            }
            if (cleanupFailure is not null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
            Check(index.Count == 0, "remote deletion and mapping cleanup");
        }
        Console.WriteLine(Environment.GetEnvironmentVariable("WTS_CLOUDINARY_RESTORE_DRILL") == "1"
            ? "Live Cloudinary cleanup passed. An actual hosted backend restart remains to be tested after deployment."
            : "Live Cloudinary cleanup passed. Isolated MongoDB backup/restore and hosted backend restart remain untested.");
    }

    private static Uri WithoutDeliverySignature(Uri signed)
    {
        var match = Regex.Match(signed.AbsolutePath, @"/s--[A-Za-z0-9_-]+--/");
        if (!match.Success) return signed;
        var builder = new UriBuilder(signed)
        { Path = signed.AbsolutePath.Replace(match.Value, "/", StringComparison.Ordinal), Query = string.Empty };
        return builder.Uri;
    }

    private static bool IsOnePixelPng(PrivateUpload? upload)
    {
        var bytes = upload?.Bytes;
        return upload?.ContentType == "image/png" && bytes is { Length: >= 24 } &&
            bytes.AsSpan(0, 8).SequenceEqual(Png.AsSpan(0, 8)) &&
            BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)) == 1 &&
            BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4)) == 1;
    }

    private static async Task RunIsolatedRestoreAsync(string reference, PrivateUpload backedUp,
        LiveIndex index, CloudinaryPrivateUploadStorage storage, IConfiguration config)
    {
        var root = Directory.GetCurrentDirectory();
        var bin = Path.Combine(root, "artifacts", "mongodb-tools", "extracted",
            "mongodb-database-tools-windows-x86_64-100.20.1", "bin");
        var dump = Path.Combine(bin, "mongodump.exe");
        var restore = Path.Combine(bin, "mongorestore.exe");
        Check(System.IO.File.Exists(dump) && System.IO.File.Exists(restore), "MongoDB restore tools available");
        const string uri = "mongodb://127.0.0.1:27017/?directConnection=true&serverSelectionTimeoutMS=5000";
        var suffix = Guid.NewGuid().ToString("N");
        var sourceName = "WTS_CloudRestoreSource_" + suffix;
        var targetName = "WTS_CloudRestoreTarget_" + suffix;
        var archive = Path.Combine(Path.GetTempPath(), $"wts-cloud-restore-{suffix}.archive.gz");
        var backup = Path.Combine(Path.GetTempPath(), $"wts-cloud-restore-{suffix}.png");
        var mongo = new MongoClient(uri);
        var databases = await (await mongo.ListDatabaseNamesAsync()).ToListAsync();
        Check(!databases.Contains(sourceName) && !databases.Contains(targetName), "fresh isolated restore namespaces");
        var source = new MongoContext(Options.Create(new MongoDbSettings { ConnectionString = uri, DatabaseName = sourceName }));
        var target = new MongoContext(Options.Create(new MongoDbSettings { ConnectionString = uri, DatabaseName = targetName }));
        var name = Path.GetFileName(reference);
        var sourceRecord = await index.FindAsync(name, CancellationToken.None);
        Check(sourceRecord is not null, "synthetic source mapping exists");
        string? replacement = null;
        try
        {
            await source.PrivateUploads.InsertOneAsync(sourceRecord!);
            await System.IO.File.WriteAllBytesAsync(backup, backedUp.Bytes);
            var originalHash = SHA256.HashData(backedUp.Bytes);
            await RunMongoToolAsync(dump, "--uri=" + uri, "--db=" + sourceName,
                "--archive=" + archive, "--gzip");
            await RunMongoToolAsync(restore, "--uri=" + uri, "--archive=" + archive, "--gzip",
                "--nsFrom=" + sourceName + ".*", "--nsTo=" + targetName + ".*");
            var restoredRecord = await target.PrivateUploads.Find(r => r.Filename == name).FirstOrDefaultAsync();
            Check(restoredRecord?.AssetId == sourceRecord!.AssetId &&
                restoredRecord.PublicId == sourceRecord.PublicId, "restored Mongo mapping matches source");
            var backupBytes = await System.IO.File.ReadAllBytesAsync(backup);
            Check(SHA256.HashData(backupBytes).SequenceEqual(originalHash), "synthetic backup file hash");
            replacement = await storage.SaveAsync(File(backupBytes), "synthetic-restore-target");
            var replacementRecord = await index.FindAsync(Path.GetFileName(replacement), CancellationToken.None);
            Check(replacementRecord is not null, "replacement authenticated asset mapping exists");
            var remapped = await target.PrivateUploads.ReplaceOneAsync(r => r.Filename == name,
                new PrivateUploadRecord { Filename = name, AssetId = replacementRecord!.AssetId,
                    PublicId = replacementRecord.PublicId, ResourceType = replacementRecord.ResourceType });
            Check(remapped.MatchedCount == 1, "isolated restored mapping remapped to replacement asset");
            using var restoredClient = new HttpClient();
            var restoredStorage = new CloudinaryPrivateUploadStorage(restoredClient,
                new MongoPrivateUploadIndex(target), config);
            Check(IsOnePixelPng(await restoredStorage.ReadAsync(name)),
                "backend retrieval through restored Mongo mapping and replacement asset");
            Console.WriteLine("Isolated Cloudinary/Mongo restore passed: authenticated retrieval, backup hash, Mongo archive restore, replacement upload and remapped backend read.");
        }
        finally
        {
            try { if (replacement is not null) await storage.DeleteAsync(replacement); }
            finally
            {
                await mongo.DropDatabaseAsync(sourceName);
                await mongo.DropDatabaseAsync(targetName);
                if (System.IO.File.Exists(backup)) System.IO.File.Delete(backup);
                if (System.IO.File.Exists(archive)) System.IO.File.Delete(archive);
            }
        }
    }

    private static async Task RunMongoToolAsync(string executable, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable)
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("MongoDB tool did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await Task.WhenAll(stdout, stderr);
        Check(process.ExitCode == 0, "isolated MongoDB dump/restore tool");
    }

    private static FormFile File(byte[]? bytes = null, string name = "synthetic.png", string contentType = "image/png")
    {
        bytes ??= Png;
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", name)
        { Headers = new HeaderDictionary(), ContentType = contentType };
    }

    private static byte[] SyntheticPdf()
    {
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Contents 4 0 R >>",
            "<< /Length 0 >>\nstream\n\nendstream"
        };
        var builder = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int> { 0 };
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(builder.Length);
            builder.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = builder.Length;
        builder.Append($"xref\n0 {offsets.Count}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) builder.Append($"{offset:0000000000} 00000 n \n");
        builder.Append($"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException($"Live Cloudinary check failed: {label}");
    }

    private sealed class LiveIndex : IPrivateUploadIndex
    {
        private readonly Dictionary<string, PrivateUploadRecord> _records = new();
        internal int Count => _records.Count;
        internal bool FailAdd { get; set; }
        public Task AddAsync(PrivateUploadRecord record, CancellationToken ct)
        { if (FailAdd) throw new InvalidOperationException("Synthetic index failure"); _records.Add(record.Filename, record); return Task.CompletedTask; }
        public Task<PrivateUploadRecord?> FindAsync(string filename, CancellationToken ct) =>
            Task.FromResult(_records.GetValueOrDefault(filename));
        public Task RemoveAsync(string filename, CancellationToken ct)
        { _records.Remove(filename); return Task.CompletedTask; }
    }

    private sealed class CaptureUploadHandler : DelegatingHandler
    {
        internal Uri? DeliveryUrl { get; private set; }
        internal int DestroyCount { get; private set; }
        internal CaptureUploadHandler() : base(new HttpClientHandler()) { }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var response = await base.SendAsync(request, ct);
            if (request.RequestUri?.AbsolutePath.EndsWith("/destroy") == true && response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(ct);
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.GetProperty("result").GetString() == "ok") DestroyCount++;
                response.Content = new StringContent(json);
            }
            if (request.RequestUri?.AbsolutePath.EndsWith("/upload") == true && response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(ct);
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.TryGetProperty("secure_url", out var url) &&
                    Uri.TryCreate(url.GetString(), UriKind.Absolute, out var parsed) &&
                    parsed.Scheme == Uri.UriSchemeHttps && parsed.Host == "res.cloudinary.com")
                    DeliveryUrl = parsed;
                response.Content = new StringContent(json);
            }
            return response;
        }
    }
}
