using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
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
        using var client = new HttpClient(handler);
        var storage = new CloudinaryPrivateUploadStorage(client, index, config);
        string? reference = null;
        string? restored = null;
        try
        {
            reference = await storage.SaveAsync(File(), "synthetic-live");
            Check(index.Count == 1, "authenticated upload index");
            Check(handler.DeliveryUrl is not null && handler.DeliveryUrl.AbsolutePath.Contains("/image/authenticated/"),
                "authenticated delivery type");

            using var publicClient = new HttpClient();
            using var unsigned = await publicClient.GetAsync(handler.DeliveryUrl);
            Check(!unsigned.IsSuccessStatusCode, "unsigned original must be denied");
            var transformed = new Uri(handler.DeliveryUrl!.AbsoluteUri.Replace("/image/authenticated/", "/image/authenticated/w_2/"));
            using var unsignedTransform = await publicClient.GetAsync(transformed);
            Check(!unsignedTransform.IsSuccessStatusCode, "unsigned transformation must be denied");

            var secondInstance = new CloudinaryPrivateUploadStorage(client, index, config);
            var read = await secondInstance.ReadAsync(Path.GetFileName(reference));
            Check(read?.Bytes.SequenceEqual(Png) == true, "authenticated retrieval after service recreation");

            // Synthetic asset-only recovery: copy retrieved bytes into a new authenticated asset.
            restored = await secondInstance.SaveAsync(File(read!.Bytes), "synthetic-restore");
            var recovered = await storage.ReadAsync(Path.GetFileName(restored));
            Check(recovered?.Bytes.SequenceEqual(Png) == true, "synthetic asset recovery");
            index.FailAdd = true;
            var destroyCount = handler.DestroyCount;
            try { await storage.SaveAsync(File(), "synthetic-failed-index"); throw new Exception("Index failure was accepted"); }
            catch (InvalidOperationException) { Check(handler.DestroyCount == destroyCount + 1, "failed-index remote cleanup"); }
            index.FailAdd = false;
            Console.WriteLine("Live Cloudinary checks passed: authenticated upload, unsigned original/transformation denied, server retrieval after service recreation, synthetic asset recovery, and failed-index cleanup.");
        }
        finally
        {
            if (restored is not null) await storage.DeleteAsync(restored);
            if (reference is not null) await storage.DeleteAsync(reference);
            Check(index.Count == 0, "remote deletion and mapping cleanup");
        }
        Console.WriteLine("Live Cloudinary cleanup passed. MongoDB backup/restore and full application restart require an isolated database drill.");
    }

    private static FormFile File(byte[]? bytes = null)
    {
        bytes ??= Png;
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "synthetic.png")
        { Headers = new HeaderDictionary(), ContentType = "image/png" };
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
                DestroyCount++;
            if (request.RequestUri?.AbsolutePath.EndsWith("/authenticated") == true && response.IsSuccessStatusCode)
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
