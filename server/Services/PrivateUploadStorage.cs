namespace WeldingThozhilalarSangam.Api.Services;

// Keep existing authorized /uploads references; production uses durable private storage.
public sealed class PrivateUploadStorage(IConfiguration configuration, IWebHostEnvironment environment)
{
    public string RootPath { get; } = Path.GetFullPath(configuration["Uploads:RootPath"] ??
        Path.Combine(environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot"), "uploads"));
}
