extern alias Api;
using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WeldingThozhilalarSangam.Api.Services;

// Exists only in the test assembly. Production has no fake sender or capture route.
sealed class CapturingSms : ISmsSender
{
    readonly ConcurrentDictionary<string, string> codes = new();
    public string Code(string phone) => codes[MobileNumber.Normalize(phone)];
    public Task<bool> SendOtpAsync(string phoneNumber, string otp, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        codes[phoneNumber] = otp;
        return Task.FromResult(true);
    }
}

sealed class IsolatedApi(string root, Dictionary<string, string?> settings, CapturingSms sender, IPrivateUploadStorage? uploadOverride = null)
    : WebApplicationFactory<Api::Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development").UseContentRoot(root);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISmsSender>();
            services.AddSingleton<ISmsSender>(sender);
            if (uploadOverride is not null)
            {
                services.RemoveAll<IPrivateUploadStorage>();
                services.AddSingleton(uploadOverride);
            }
        });
    }
}

// Loopback-only bridge lets the existing real browser exercise TestServer. OTP
// capture is guarded by a per-run secret known only to the test runner, never React.
sealed class TestBridge : IAsyncDisposable
{
    readonly HttpListener listener = new();
    readonly HttpClient api;
    readonly CapturingSms sender;
    readonly string captureKey;
    readonly CancellationTokenSource stopping = new();
    readonly Task loop;
    public TestBridge(int port, HttpClient api, CapturingSms sender, string captureKey)
    {
        this.api = api; this.sender = sender; this.captureKey = captureKey;
        listener.Prefixes.Add($"http://127.0.0.1:{port}/"); listener.Start();
        loop = Run();
    }
    async Task Run()
    {
        while (!stopping.IsCancellationRequested)
        {
            HttpListenerContext context;
            try { context = await listener.GetContextAsync(); }
            catch (HttpListenerException) when (stopping.IsCancellationRequested) { break; }
            catch (ObjectDisposedException) when (stopping.IsCancellationRequested) { break; }
            _ = Forward(context);
        }
    }
    async Task Forward(HttpListenerContext context)
    {
        try
        {
            if (context.Request.Url!.AbsolutePath.StartsWith("/__test-capture/", StringComparison.Ordinal))
            {
                if (context.Request.Headers["X-Test-Capture"] != captureKey) { context.Response.StatusCode = 403; return; }
                var phone = context.Request.Url.AbsolutePath.Split('/').Last();
                var bytes = System.Text.Encoding.UTF8.GetBytes(sender.Code(phone));
                context.Response.ContentType = "text/plain";
                await context.Response.OutputStream.WriteAsync(bytes); return;
            }
            using var request = new HttpRequestMessage(new HttpMethod(context.Request.HttpMethod), context.Request.RawUrl);
            if (context.Request.HasEntityBody) request.Content = new StreamContent(context.Request.InputStream);
            foreach (var name in context.Request.Headers.AllKeys)
            {
                if (name is null || name.Equals("Host", StringComparison.OrdinalIgnoreCase) || name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) continue;
                if (!request.Headers.TryAddWithoutValidation(name, context.Request.Headers[name]))
                    request.Content?.Headers.TryAddWithoutValidation(name, context.Request.Headers[name]);
            }
            using var response = await api.SendAsync(request);
            context.Response.StatusCode = (int)response.StatusCode;
            foreach (var header in response.Headers.Concat(response.Content.Headers))
            {
                if (header.Key is "Content-Length" or "Transfer-Encoding") continue;
                context.Response.Headers[header.Key] = string.Join(", ", header.Value);
            }
            await response.Content.CopyToAsync(context.Response.OutputStream);
        }
        catch { context.Response.StatusCode = 500; }
        finally { context.Response.Close(); }
    }
    public async ValueTask DisposeAsync()
    {
        // Mark cancellation BEFORE aborting Windows' pending GetContext I/O.
        // IsListening can still be true while Stop is aborting that operation.
        stopping.Cancel();
        listener.Stop();
        try { await loop; }
        finally { listener.Close(); stopping.Dispose(); }
    }
}
