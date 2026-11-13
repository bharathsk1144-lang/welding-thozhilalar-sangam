using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using System.Net;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using WeldingThozhilalarSangam.Api.Data;
using WeldingThozhilalarSangam.Api.Middleware;
using WeldingThozhilalarSangam.Api.Models;
using WeldingThozhilalarSangam.Api.Services;

var builder = WebApplication.CreateBuilder(args);
if (builder.Environment.IsDevelopment())
{
    // Private local account classification stays out of source control and deployment.
    // Restore the normal environment/CLI precedence above optional file configuration.
    builder.Configuration.AddJsonFile("appsettings.Statistics.local.json", optional: true, reloadOnChange: false)
        .AddEnvironmentVariables()
        .AddCommandLine(args);
}
var mobileModes = args.Where(a => a is "--audit-mobile" or "--dry-run-mobile" or "--verify-mobile" or "--migrate-mobile" or "--rollback-mobile").ToArray();

builder.Services.Configure<MongoDbSettings>(builder.Configuration.GetSection("MongoDb"));
builder.Services.Configure<AdminSettings>(builder.Configuration.GetSection("Admin"));
builder.Services.AddOptions<PublicStatisticsSettings>().Bind(builder.Configuration.GetSection("PublicStatistics"))
    .Validate(settings => settings.IsValid(), "Public statistics require valid, non-conflicting account ID classifications.")
    .ValidateOnStart();
builder.Services.AddOptions<MembershipSettings>().Bind(builder.Configuration.GetSection("Membership"))
    .Validate(settings => MemberIdFormat.IsValidOrganizationNumber(settings.OrganizationRegistrationNumber),
        "Membership:OrganizationRegistrationNumber must be empty or an official number of up to 48 ASCII letters/digits separated by hyphens/slashes.")
    .ValidateOnStart();
builder.Services.Configure<SmsSettings>(options =>
{
    options.Provider = builder.Configuration["SMS_PROVIDER"] ?? builder.Configuration["Sms:Provider"] ?? string.Empty;
    options.ApiKey = builder.Configuration["SMS_API_KEY"] ?? builder.Configuration["Sms:ApiKey"] ?? string.Empty;
    options.SenderId = builder.Configuration["SMS_SENDER_ID"] ?? builder.Configuration["Sms:SenderId"] ?? string.Empty;
    options.TemplateId = builder.Configuration["SMS_TEMPLATE_ID"] ?? builder.Configuration["Sms:TemplateId"] ?? string.Empty;
    options.Enabled = bool.TryParse(builder.Configuration["SMS_ENABLED"] ?? builder.Configuration["Sms:Enabled"], out var enabled) && enabled;
});

builder.Services.AddSingleton<MongoContext>();
var uploadProvider = builder.Configuration["Uploads:Provider"] ?? "Local";
if (uploadProvider.Equals("Cloudinary", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IPrivateUploadIndex, MongoPrivateUploadIndex>();
    builder.Services.AddHttpClient<IPrivateUploadStorage, CloudinaryPrivateUploadStorage>();
}
else if (uploadProvider.Equals("Local", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IPrivateUploadStorage, LocalPrivateUploadStorage>();
}
else throw new InvalidOperationException("Uploads:Provider must be Local or Cloudinary.");
builder.Services.AddHttpClient<ISmsSender, ConfiguredSmsSender>();
builder.Services.AddSingleton<OtpService>();
builder.Services.AddSingleton(TimeProvider.System);
var trustedProxies = builder.Configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [];
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
    foreach (var value in trustedProxies)
    {
        if (!IPAddress.TryParse(value, out var address)) throw new InvalidOperationException("Configure exact trusted reverse-proxy IP addresses.");
        options.KnownProxies.Add(address);
    }
});
builder.Services.AddScoped<UserSessionService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<DistrictService>();
builder.Services.AddScoped<IPublicStatisticsService, PublicStatisticsService>();
builder.Services.AddScoped<AdminAuthService>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("ClientApp", policy =>
    {
        var configuredOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        policy.SetIsOriginAllowed(origin => configuredOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase) ||
                (builder.Environment.IsDevelopment() && ClientOriginPolicy.IsDevelopmentOrigin(origin)))
            .AllowAnyHeader()
            .AllowAnyMethod()
            .WithExposedHeaders("Content-Disposition");
    });
});

var adminSettings = builder.Configuration.GetSection("Admin").Get<AdminSettings>()
    ?? throw new InvalidOperationException("Admin settings are missing.");
var jwtKey = Encoding.UTF8.GetBytes(adminSettings.JwtKey);
if (mobileModes.Length == 0) ProductionConfiguration.Validate(builder.Configuration, builder.Environment);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = adminSettings.Issuer,
            ValidAudience = adminSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(jwtKey),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ClockSkew = TimeSpan.FromMinutes(2)
        };
    });

builder.Services.AddAuthorization(options =>
{
    // Existing [Authorize] endpoints are administrative. Member tokens must never grant admin access.
    options.DefaultPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().RequireRole("Admin").Build();
    options.AddPolicy("MemberOrAdmin", policy => policy.RequireAuthenticatedUser().RequireRole("Member", "Admin"));
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await context.HttpContext.Response.WriteAsJsonAsync(new { message = "Too many attempts. Please try again later." }, cancellationToken);
    };
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("admin-login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("public-verification", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("public-statistics", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

if (mobileModes.Length != 0)
{
    try
    {
        if (mobileModes.Length != 1) throw new InvalidOperationException("Choose exactly one mobile maintenance mode.");
        var mongo = app.Services.GetRequiredService<MongoContext>();
        var database = builder.Configuration["ExpectedDatabase"];
        if (database is not null && database != mongo.Users.Database.DatabaseNamespace.DatabaseName)
            throw new InvalidOperationException("Database differs from the explicitly expected target.");
        var report = await MobileIndexMigration.AuditAsync(mongo.Users);
        var reportPath = Path.GetFullPath(builder.Configuration["MobileAuditReport"] ?? "mobile-audit.json");
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        await File.WriteAllTextAsync(reportPath, System.Text.Json.JsonSerializer.Serialize(report,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Mobile dry-run report: {reportPath}. Users: {report.TotalUsers}; ready: {report.RecordsReady}; duplicate groups: {report.DuplicateUserIds.Length}; invalid: {report.InvalidUserIds.Length}; index conflicts: {report.IndexConflicts.Length}; needs backfill: {report.NeedsBackfill}; canonical index ready: {report.CanonicalIndexReady}.");
        var expectedCount = int.TryParse(builder.Configuration["ExpectedUserCount"], out var count) ? count : -1;
        if (expectedCount >= 0 && report.TotalUsers != expectedCount)
            throw new InvalidOperationException("User count differs from the explicitly expected count.");
        if (mobileModes[0] is "--migrate-mobile" or "--rollback-mobile")
        {
            if (builder.Configuration["WritersStopped"] != "true" || expectedCount < 0 || string.IsNullOrEmpty(database))
                throw new InvalidOperationException("Execution requires WritersStopped=true, ExpectedDatabase, and ExpectedUserCount.");
            var backup = builder.Configuration["BackupArchive"];
            var backupHash = builder.Configuration["BackupSha256"];
            if (string.IsNullOrWhiteSpace(backup) || !File.Exists(backup) || new FileInfo(backup).Length == 0 || string.IsNullOrWhiteSpace(backupHash))
                throw new InvalidOperationException("Execution requires a nonempty backup archive and its approved SHA256.");
            await using (var backupFile = File.OpenRead(backup))
            {
                var actual = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(backupFile));
                if (!actual.Equals(backupHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Backup checksum differs from the approved checksum.");
            }
            var manifest = builder.Configuration["MigrationManifest"];
            var fingerprint = builder.Configuration["ApprovedPlanFingerprint"];
            if (string.IsNullOrWhiteSpace(manifest) || string.IsNullOrWhiteSpace(fingerprint))
                throw new InvalidOperationException("Execution requires MigrationManifest and ApprovedPlanFingerprint from the reviewed dry-run.");
            await MobileIndexMigration.ExecuteApprovedAsync(mongo.Users, Path.GetFullPath(manifest), expectedCount, fingerprint,
                rollback: mobileModes[0] == "--rollback-mobile");
            Console.WriteLine("Approved operation and preservation checks completed. Retain the original manifest and backup.");
        }
        else if (!report.CanMigrate || (mobileModes[0] == "--verify-mobile" && (report.NeedsBackfill != 0 || !report.CanonicalIndexReady)))
            Environment.ExitCode = 2;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Mobile maintenance failed ({ex.GetType().Name}). Keep writers stopped if execution began. Review the private report/manifest; no automatic repair was attempted.");
        if (ex is InvalidOperationException) Console.Error.WriteLine(ex.Message);
        Environment.ExitCode = 1;
    }
    return;
}


using (var scope = app.Services.CreateScope())
{
    var mongoContext = scope.ServiceProvider.GetRequiredService<MongoContext>();
    var mobileAudit = await MobileIndexMigration.AuditAsync(mongoContext.Users);
    if (!mobileAudit.CanMigrate || mobileAudit.NeedsBackfill != 0)
    {
        var reportPath = Path.GetFullPath(builder.Configuration["MobileAuditReport"] ?? "mobile-audit.json");
        await File.WriteAllTextAsync(reportPath, System.Text.Json.JsonSerializer.Serialize(mobileAudit,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        throw new InvalidOperationException($"Mobile migration required. Review {reportPath}; run --migrate-mobile only with all writers stopped.");
    }
    await mongoContext.EnsureIndexesAsync();

    var districts = scope.ServiceProvider.GetRequiredService<DistrictService>();
    await districts.SeedTamilNaduDistrictsAsync();
}

// Never accept forwarded client identity from arbitrary internet senders.
if (trustedProxies.Length > 0) app.UseForwardedHeaders();
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        var headers = context.Response.Headers;
        // API responses contain no HTML or public static assets. These headers also
        // cover error responses and authorized downloads without altering CORS.
        if (!headers.ContainsKey("Content-Security-Policy"))
            headers["Content-Security-Policy"] = "default-src 'none'; object-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";
        if (!headers.ContainsKey("X-Content-Type-Options")) headers["X-Content-Type-Options"] = "nosniff";
        if (!headers.ContainsKey("Referrer-Policy")) headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        if (!headers.ContainsKey("Permissions-Policy"))
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
        if (!headers.ContainsKey("X-Frame-Options")) headers["X-Frame-Options"] = "DENY";
        if (!app.Environment.IsDevelopment() && !headers.ContainsKey("Strict-Transport-Security"))
            headers["Strict-Transport-Security"] = "max-age=31536000";
        return Task.CompletedTask;
    });
    await next();
});
app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseRouting();
app.UseCors("ClientApp");
app.UseRateLimiter();
// This API has no public webroot assets. Serve uploads only through the authorized
// controller; a static-file provider could otherwise expose alternate paths.
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapGet("/health/ready", async (MongoContext mongo, HttpContext context) =>
{
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
    timeout.CancelAfter(TimeSpan.FromSeconds(3));
    context.Response.Headers.CacheControl = "no-store";
    try
    {
        await mongo.Users.Database.RunCommandAsync<MongoDB.Bson.BsonDocument>(
            new MongoDB.Bson.BsonDocument("ping", 1), cancellationToken: timeout.Token);
        return Results.Ok(new { status = "ready" });
    }
    catch (Exception) { return Results.Json(new { status = "unavailable" }, statusCode: 503); }
});

app.Run();

// Entry point for test-assembly dependency injection; no HTTP test endpoint.
public partial class Program { }
