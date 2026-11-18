using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using SkiaSharp;
using WeldingThozhilalarSangam.Api.Data;
using WeldingThozhilalarSangam.Api.Models;
using WeldingThozhilalarSangam.Api.Services;
using WeldingThozhilalarSangam.Api.DTOs;

// No external test framework or mock database: exercise the real API and MongoDB index.
// Default: random WTS_SecurityTests_* database. Rehearsal mode: a named, fresh
// companion database that is always retained and never deleted by this harness.
var rootDirectory = new DirectoryInfo(Directory.GetCurrentDirectory());
while (rootDirectory != null && !File.Exists(Path.Combine(rootDirectory.FullName, "server", "WeldingThozhilalarSangam.Api.csproj")))
    rootDirectory = rootDirectory.Parent;
var root = rootDirectory?.FullName ?? throw new InvalidOperationException("Run from the repository directory.");
var runRoot = Path.Combine(root, "artifacts", "security-tests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(runRoot);
Directory.CreateDirectory(Path.Combine(runRoot, "wwwroot", "uploads"));
var signatureSource = Path.Combine(root, "server", "Assets", "OfficerSignatures");
var signatureTarget = Path.Combine(runRoot, "Assets", "OfficerSignatures");
Directory.CreateDirectory(signatureTarget);
foreach (var officer in new[] { "president", "secretary", "treasurer" })
{
    var filename = $"state-{officer}-signature.png";
    File.Copy(Path.Combine(signatureSource, filename), Path.Combine(signatureTarget, filename));
    var blue = $"state-{officer}-signature-blue.png";
    File.Copy(Path.Combine(signatureSource, blue), Path.Combine(signatureTarget, blue));
}
File.Copy(Path.Combine(root, "server", "Assets", "about-tamil.md"), Path.Combine(runRoot, "Assets", "about-tamil.md"));
var namedArgument = args.SingleOrDefault(a => a.StartsWith("--isolated-database=", StringComparison.Ordinal));
var namedDatabase = namedArgument?["--isolated-database=".Length..];
var retainDatabase = args.Contains("--keep-database");
var noMigration = args.Contains("--no-migration");
var officialIds = args.Contains("--official-id-format");
if (namedDatabase is not null && (!System.Text.RegularExpressions.Regex.IsMatch(namedDatabase,
    "^WTS_MobileRegression_[0-9]{8}_[a-f0-9]{32}$") || !retainDatabase))
    throw new InvalidOperationException("A rehearsal regression target must be a fresh WTS_MobileRegression_<date>_<guid> database with --keep-database.");
var dbName = namedDatabase ?? $"WTS_SecurityTests_{Guid.NewGuid():N}";
var connection = namedDatabase is not null ? "mongodb://127.0.0.1:27017/?directConnection=true&serverSelectionTimeoutMS=5000" :
    Environment.GetEnvironmentVariable("WTS_TEST_MONGO") ?? "mongodb://localhost:27017/?serverSelectionTimeoutMS=5000";
var mongo = new MongoClient(connection);
// Check before entering the cleanup block: an existing namespace is never reused or dropped.
if (namedDatabase is not null && (await (await mongo.ListDatabaseNamesAsync()).ToListAsync()).Contains(dbName))
    throw new InvalidOperationException("The named regression namespace already exists. Preserve it and request a fresh approved plan.");
var context = new MongoContext(Options.Create(new MongoDbSettings { ConnectionString = connection, DatabaseName = dbName }));
var users = context.Users;
var passed = 0;
IsolatedApi? api = null;
TestBridge? bridge = null;
var captured = new CapturingSms();
var captureKey = Guid.NewGuid().ToString("N");
var log = new List<string>();
void Check(bool condition, string name)
{
    if (!condition) throw new Exception($"FAIL: {name}");
    Console.WriteLine($"PASS: {name}");
    passed++;
}
UserRegistration Seed(string phone, string member, string password = "Original@123")
{
    var user = new UserRegistration { Id = ObjectId.GenerateNewId().ToString(), PhoneNumber = phone,
        Name = "Existing isolated member", MemberId = member, District = "Chennai", VerificationStatus = "Verified",
        AadhaarPhotoPath = "/uploads/legacy-aadhaar.png", ProfilePhotoPath = "/uploads/legacy-profile.png" };
    if (password != "") user.PasswordHash = new PasswordHasher<UserRegistration>().HashPassword(user, password);
    return user;
}
try
{
    Check(namedDatabase is not null || dbName.StartsWith("WTS_SecurityTests_", StringComparison.Ordinal), "test database safety guard");
    await ReadinessChecks.Run(Check);
    foreach (var phone in new[] { "9876543210", "+919876543210", "919876543210", " 9876543210 " })
        Check(MobileNumber.Normalize(phone) == "9876543210", $"normalization {phone}");
    foreach (var phone in new[] { "1234567890", "987654321", "+00919876543210", "98765 43210", "９８７６５４３２１０", "9876543210abc" })
        Check(!MobileNumber.TryNormalize(phone, out _), $"invalid mobile rejected: {phone}");

    var legacy = Seed(" +919876543210 ", "WTS-2024-LEGACY");
    await File.WriteAllBytesAsync(Path.Combine(runRoot, "wwwroot", "uploads", "legacy-aadhaar.png"), [137, 80, 78, 71, 13, 10, 26, 10]);
    var noPassword = Seed("919876543211", "WTS-2023-OLD", "");
    if (noMigration)
    {
        legacy.CanonicalMobile = MobileNumber.Normalize(legacy.PhoneNumber);
        noPassword.CanonicalMobile = MobileNumber.Normalize(noPassword.PhoneNumber);
    }
    await users.InsertManyAsync([legacy, noPassword]);
    if (noMigration)
    {
        await users.Indexes.CreateOneAsync(new CreateIndexModel<UserRegistration>(
            Builders<UserRegistration>.IndexKeys.Ascending(u => u.CanonicalMobile),
            new CreateIndexOptions { Name = MobileIndexMigration.IndexName, Unique = true }));
        var ready = await MobileIndexMigration.AuditAsync(users);
        Check(ready.NeedsBackfill == 0 && ready.CanonicalIndexReady, "fresh canonical test fixtures ready; migration modes skipped");
    }
    else
    {
    var beforeMigration = await users.Find(_ => true).ToListAsync();
    var audit = await MobileIndexMigration.AuditAsync(users);
    Check(audit.CanMigrate && audit.NeedsBackfill == 2, "legacy audit identifies additive migration");
    await MobileIndexMigration.ApplyAsync(users);
    foreach (var before in beforeMigration)
    {
        var after = await users.Find(u => u.Id == before.Id).SingleAsync();
        Check(after.MemberId == before.MemberId && after.PasswordHash == before.PasswordHash &&
            after.PhoneNumber == before.PhoneNumber && after.AadhaarPhotoPath == before.AadhaarPhotoPath &&
            after.ProfilePhotoPath == before.ProfilePhotoPath, "migration preserves all existing account fields");
    }
    var indexes = await (await users.Indexes.ListAsync()).ToListAsync();
    Check(indexes.Any(i => i["name"] == MobileIndexMigration.IndexName && i["unique"].AsBoolean &&
        !i.Contains("partialFilterExpression") && !i.Contains("sparse")), "full canonical MongoDB unique index exists");

    // Diagnostic fixture with canonical collisions. The migration must fail without modifying it.
    var diagnosticUsers = mongo.GetDatabase(dbName).GetCollection<UserRegistration>("DiagnosticUsers");
    await diagnosticUsers.InsertManyAsync([Seed("9876543212", "A"), Seed("+919876543212", "B"), Seed("invalid", "C")]);
    var diagnosticBefore = (await diagnosticUsers.Find(_ => true).ToListAsync()).ToJson();
    var diagnostic = await MobileIndexMigration.AuditAsync(diagnosticUsers);
    Check(!diagnostic.CanMigrate && diagnostic.DuplicateUserIds.Length == 1 && diagnostic.InvalidUserIds.Length == 1,
        "duplicate/invalid legacy data reported without passwords or document paths");
    try { await MobileIndexMigration.ApplyAsync(diagnosticUsers); throw new Exception("Migration should have failed"); }
    catch (InvalidOperationException) { }
    Check(diagnosticBefore == (await diagnosticUsers.Find(_ => true).ToListAsync()).ToJson(), "blocked migration changes no records");
    }

    var clock = new TestClock();
    var otpService = new OtpService(captured, NullLogger<OtpService>.Instance, clock);
    await otpService.SendOtpAsync("9876543213", true);
    var proof = otpService.VerifyOtp("+919876543213", captured.Code("9876543213"))!;
    Check(proof != null && !otpService.ConsumeVerificationToken(proof, "9876543214") &&
        !otpService.ConsumeVerificationToken(proof, "9876543213", OtpService.PasswordReset), "OTP proof binds phone and purpose");
    var consumed = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => otpService.ConsumeVerificationToken(proof!, "9876543213"))));
    Check(consumed.Count(x => x) == 1, "OTP proof consumption is atomic");
    await otpService.SendOtpAsync("9876543214", true);
    var expires = otpService.VerifyOtp("9876543214", captured.Code("9876543214"))!;
    clock.Advance(TimeSpan.FromMinutes(6));
    Check(!otpService.ConsumeVerificationToken(expires, "9876543214"), "OTP verification proof expires");
    await otpService.SendOtpAsync("9876543215", true);
    for (var i = 0; i < 5; i++) otpService.VerifyOtp("9876543215", "000000");
    Check(otpService.VerifyOtp("9876543215", captured.Code("9876543215")) == null, "OTP brute force attempt cap");
    var productionOtp = new OtpService(new DisabledSms(), NullLogger<OtpService>.Instance, TimeProvider.System);
    try { await productionOtp.SendOtpAsync("9876543216", false); throw new Exception("Production OTP must fail"); }
    catch (OtpDeliveryException) { }
    Check(productionOtp.VerifyOtp("9876543216", "123456") == null, "SMS failure cannot enable demo OTP in production");

    var userService = new UserService(context);
    foreach (var compressor in new[] { "snappy", "zlib" })
    {
        var compressed = new MongoClient(connection + (connection.Contains('?') ? "&" : "?") + "compressors=" + compressor);
        Check(await compressed.GetDatabase(dbName).GetCollection<UserRegistration>("Users").CountDocumentsAsync(_ => true) == 2,
            "patched MongoDB " + compressor + " compressor preserves reads");
    }
    await otpService.SendOtpAsync("9876543260", true, registrationName: "Bound member");
    var namedProof = otpService.VerifyOtp("9876543260", captured.Code("9876543260"));
    Check(!otpService.ConsumeVerificationToken(namedProof, "9876543260", registrationName: "Changed name") &&
        otpService.ConsumeVerificationToken(namedProof, "9876543260", registrationName: " Bound member "),
        "registration proof locks the supplied name while preserving trimmed spelling");
    Check(ClientOriginPolicy.IsDevelopmentOrigin("http://192.168.1.22:5173") &&
        ClientOriginPolicy.IsDevelopmentOrigin("http://localhost:5175") &&
        !ClientOriginPolicy.IsDevelopmentOrigin("http://192.168.1.22.attacker.test:5173") &&
        !ClientOriginPolicy.IsDevelopmentOrigin("http://8.8.8.8:5173") &&
        !ClientOriginPolicy.IsDevelopmentOrigin("http://192.168.1.22:9999"), "LAN CORS is limited to development private IPs and Vite ports");
    var race = await Task.WhenAll(Enumerable.Range(0, 16).Select(async i =>
    {
        try { await userService.CreateAsync(Seed(i % 2 == 0 ? "9876543220" : "+919876543220", "", $"Race@{i}12345")); return true; }
        catch (DuplicateMobileException) { return false; }
    }));
    Check(race.Count(x => x) == 1 && await users.CountDocumentsAsync(u => u.CanonicalMobile == "9876543220") == 1,
        "TC05/TC14: 16 concurrent service requests create exactly one account");
    var rawDuplicate = Seed("919876543220", "BYPASS");
    rawDuplicate.CanonicalMobile = "9876543220";
    try { await users.InsertOneAsync(rawDuplicate); throw new Exception("Unique index should reject raw insert"); }
    catch (MongoWriteException ex) when (ex.WriteError.Code == 11000) { }
    Check(true, "MongoDB rejects duplicates even when service checks are bypassed");

    // Only fresh synthetic identities: read projections must not rewrite any BSON field.
    var identityFixtures = new[] {
        Seed("9876543271", "WTS-2024-FD48EF"), Seed("9876543272", "WTS-2025-FD48EF"),
        Seed("9876543273", "WTS-TEST-REG/2026-AA5511"), Seed("9876543274", "WTS-2024-AA5511"),
        Seed("9876543275", "WTS-2024-D00B1E"), Seed("9876543276", "WTS-2024-D00B1E"),
        Seed("9876543277", ""), Seed("9876543278", "WTS-OLD-REG/2025-ABC123"),
        Seed("9876543279", "WTS-???-ABCDE0"), Seed("9876543281", "WTS-2024-ABCDE0") };
    identityFixtures[1].District = "Madurai";
    foreach (var fixture in identityFixtures) fixture.CanonicalMobile = fixture.PhoneNumber;
    await users.InsertManyAsync(identityFixtures);
    var identityBefore = identityFixtures.ToDictionary(u => u.Id!, u => u.ToJson());
    var officialService = new UserService(context, Options.Create(new MembershipSettings { OrganizationRegistrationNumber = "TEST-REG/2026" }));
    var legacyBeforeDisplay = (await users.Find(u => u.Id == legacy.Id).SingleAsync()).ToJson();
    var existingView = await officialService.PresentAsync(new MemberIdView(legacy.MemberId));
    Check(existingView.DisplayMemberId == "WTS-TEST-REG/2026-LEGACY" && existingView.MemberId == legacy.MemberId,
        "existing members receive an official display alias without replacing their stored ID");
    var disabledView = await userService.PresentAsync(new MemberIdView(legacy.MemberId));
    Check(disabledView.DisplayMemberId == legacy.MemberId, "disabling configuration restores the original display");
    var collisionView = await officialService.PresentAsync(new MemberIdView(identityFixtures[0].MemberId));
    Check(collisionView.DisplayMemberId == identityFixtures[0].MemberId && collisionView.MemberIdDisplayWarning != null,
        "duplicate suffix across years/districts suppresses unsafe aliases");
    Check((await officialService.GetUsersAsync("Chennai")).Single(u => u.Id == identityFixtures[0].Id).MemberIdDisplayWarning != null,
        "district filters cannot hide an alias collision in another district");
    Check((await officialService.GetByMemberIdAsync(identityFixtures[2].MemberId))!.Id == identityFixtures[2].Id,
        "exact printed ID takes precedence over another member's hypothetical alias");
    var priorOfficial = await officialService.PresentAsync(new MemberIdView(identityFixtures[7].MemberId));
    Check(priorOfficial.DisplayMemberId == "WTS-TEST-REG/2026-ABC123" && priorOfficial.MemberId == identityFixtures[7].MemberId,
        "previously persisted official-prefix IDs retain their identity and derive a new display");
    Check((await officialService.GetByMemberIdAsync("WTS-TEST-REG/2026-ABCDE0"))!.Id == identityFixtures[9].Id,
        "unrecognized legacy IDs do not create guessed aliases or hide a valid identity");
    await officialService.GetIdCardUserAsync(identityFixtures[6].Id!);
    Check(legacyBeforeDisplay == (await users.Find(u => u.Id == legacy.Id).SingleAsync()).ToJson() &&
        identityFixtures.All(u => identityBefore[u.Id!] == users.Find(v => v.Id == u.Id).Single().ToJson()),
        "display/configuration reads preserve all BSON fields, including missing legacy IDs");

    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop();
    var settings = new Dictionary<string, string?> {
        ["MongoDb:ConnectionString"] = connection, ["MongoDb:DatabaseName"] = dbName,
        ["Admin:JwtKey"] = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),
        ["Aadhaar:EncryptionKey"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),
        ["Admin:Issuer"] = "SecurityTests", ["Admin:Audience"] = "SecurityTests",
        ["Admin:Username"] = "test-admin", ["Admin:Password"] = "IsolatedAdmin@123", ["Admin:PasswordHash"] = "",
        ["SMS_ENABLED"] = "false", ["Membership:OrganizationRegistrationNumber"] = officialIds ? "TEST-REG/2026" : "",
        ["Authentication:OtpEnabled"] = "true",
        ["PublicStatistics:ClassificationReviewed"] = "true", ["PublicStatistics:TestUserIds:0"] = legacy.Id,
        ["Logging:LogLevel:Default"] = "Warning"
    };
    await File.WriteAllTextAsync(Path.Combine(runRoot, "appsettings.json"), System.Text.Json.JsonSerializer.Serialize(settings));
    Environment.SetEnvironmentVariable("ASPNETCORE_TEST_CONTENTROOT_WELDINGTHOZHILALARSANGAM_API", runRoot);
    api = new IsolatedApi(runRoot, settings, captured);
    using var testHttp = api.CreateClient();
    bridge = new TestBridge(port, testHttp, captured, captureKey);
    using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(15) };
    Check((await http.GetAsync("/api/districts")).IsSuccessStatusCode, "isolated API starts with mock delivery");
    Check((await http.GetAsync("/health/live")).IsSuccessStatusCode && (await http.GetAsync("/health/ready")).IsSuccessStatusCode, "health liveness/readiness are safe aggregates");
    using (var hardened = await testHttp.GetAsync("/health/live"))
    {
        Check(hardened.Headers.TryGetValues("Content-Security-Policy", out var csp) && csp.Single().Contains("default-src 'none'") &&
            hardened.Headers.TryGetValues("X-Content-Type-Options", out var nosniff) && nosniff.Single() == "nosniff" &&
            hardened.Headers.TryGetValues("Referrer-Policy", out var referrer) && referrer.Single() == "strict-origin-when-cross-origin" &&
            hardened.Headers.TryGetValues("Permissions-Policy", out var permissions) && permissions.Single().Contains("camera=()") &&
            hardened.Headers.TryGetValues("X-Frame-Options", out var frame) && frame.Single() == "DENY",
            "API security headers cover public health response");
    }
    using (var denied = await testHttp.GetAsync("/uploads/synthetic-private.png"))
        Check(denied.StatusCode == HttpStatusCode.Unauthorized && denied.Headers.Contains("Content-Security-Policy") &&
            denied.Headers.TryGetValues("X-Content-Type-Options", out var values) && values.Single() == "nosniff",
            "security headers cover unauthorized private upload response");
    async Task<JsonObject> Json(HttpResponseMessage response, HttpStatusCode expected, string name)
    {
        var content = await response.Content.ReadAsStringAsync();
        Check(response.StatusCode == expected, $"{name}: expected {(int)expected}, got {(int)response.StatusCode} {(!response.IsSuccessStatusCode ? content : "")}");
        return JsonNode.Parse(content)!.AsObject();
    }
    async Task<JsonObject> Post(string route, object body, HttpStatusCode expected, string name) =>
        await Json(await http.PostAsJsonAsync(route, body), expected, name);
    MultipartFormDataContent Registration(string phone, string token = "", string name = "New member", string password = "NewMember@123", bool consent = true, string? aadhaarNumber = "234567890124")
    {
        var form = new MultipartFormDataContent();
        foreach (var field in new Dictionary<string, string> { ["phoneNumber"] = phone, ["verificationToken"] = token,
            ["name"] = name, ["password"] = password, ["dateOfBirth"] = "1990-01-01",
            ["bloodGroup"] = "O+", ["fatherName"] = "Test father", ["permanentAddress"] = "Isolated test address",
            ["pincode"] = "600001", ["district"] = "Chennai", ["hasJoinedWhatsAppGroup"] = "true", ["consentAccepted"] = consent ? "true" : "false" })
            form.Add(new StringContent(field.Value), field.Key);
        if (aadhaarNumber is not null) form.Add(new StringContent(aadhaarNumber), "aadhaarNumber");
        foreach (var field in new[] { "profilePhoto" })
        {
            var portrait = field == "profilePhoto";
            using var bitmap = new SKBitmap(portrait ? 600 : 800, portrait ? 750 : 500);
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.White);
            using var paint = new SKPaint { Color = portrait ? SKColors.DarkBlue : SKColors.DarkRed };
            canvas.DrawRect(portrait ? new SKRect(210, 100, 390, 320) : new SKRect(45, 45, 755, 455), paint);
            paint.Color = SKColors.LightGray;
            for (var line = 0; line < 12; line++) canvas.DrawRect(80, 80 + line * 30, 500, 8, paint);
            using var picture = SKImage.FromBitmap(bitmap);
            using var encoded = picture.Encode(SKEncodedImageFormat.Png, 100);
            var file = new ByteArrayContent(encoded.ToArray());
            file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(file, field, "isolated.png");
        }
        return form;
    }
    var statsSettings = new PublicStatisticsSettings { ClassificationReviewed = true, TestUserIds = [legacy.Id!] };
    var statisticsService = new PublicStatisticsService(context, Options.Create(statsSettings));
    var beforeRegistrationStatistics = await statisticsService.GetAsync(default);
    using (var statsResponse = await http.GetAsync("/api/statistics"))
    {
        var summary = await Json(statsResponse, HttpStatusCode.OK, "anonymous statistics available");
        Check(summary.Count == 2 && summary["totalRegisteredMembers"]!.GetValue<long>() == beforeRegistrationStatistics.TotalRegisteredMembers &&
            summary["configuredDistricts"]!.GetValue<long>() == 38 && statsResponse.Headers.CacheControl?.NoStore == true,
            "statistics contain only database aggregate counts with no-store");
    }
    var withoutTestExclusion = new PublicStatisticsService(context, Options.Create(new PublicStatisticsSettings { ClassificationReviewed = true }));
    Check((await withoutTestExclusion.GetAsync(default)).TotalRegisteredMembers == beforeRegistrationStatistics.TotalRegisteredMembers + 1,
        "explicit test-account ID excluded from public count without changing admin records");
    await Post("/api/auth/send-otp", new { name = "New member", phoneNumber = "+919876543230" }, HttpStatusCode.OK, "new phone OTP with name");
    var verified = await Post("/api/auth/verify-otp", new { phoneNumber = "919876543230", otp = captured.Code("9876543230") }, HttpStatusCode.OK, "verify canonical OTP");
    var token = verified["verificationToken"]!.GetValue<string>();
    Check((await statisticsService.GetAsync(default)) == beforeRegistrationStatistics, "OTP-only pending registration does not increase public count");
    await Json(await http.PostAsync("/api/users/register", Registration("9876543230", token, "Tampered name")), HttpStatusCode.BadRequest, "name-bound OTP rejects tampering before saving files");
    var registered = await Json(await http.PostAsync("/api/users/register", Registration("9876543230", token)), HttpStatusCode.OK, "TC01: new registration");
    Check(registered["mobileVerified"]!.GetValue<bool>(), "OTP registration reports a verified mobile");
    Check(!registered.ContainsKey("memberId") && !registered.ContainsKey("displayMemberId") &&
        !registered.ContainsKey("idCardUrl") && registered["approvalStatus"]!.GetValue<string>() == "Pending",
        "pending registration response exposes no member ID or card URL");
    var newId = registered["userId"]!.GetValue<string>();
    var afterRegistrationStatistics = await Json(await http.GetAsync("/api/statistics"), HttpStatusCode.OK, "statistics refreshed after registration");
    Check(afterRegistrationStatistics["totalRegisteredMembers"]!.GetValue<long>() == beforeRegistrationStatistics.TotalRegisteredMembers + 1,
        "pending registration still counts as a registered member without claiming verification");
    var memberToken = registered["token"]!.GetValue<string>();
    var pendingLogin = await Post("/api/users/login", new { phone = "9876543230", password = "NewMember@123" },
        HttpStatusCode.OK, "pending member can sign in to check status");
    Check(pendingLogin["memberId"]!.GetValue<string>() == "" && pendingLogin["displayMemberId"]!.GetValue<string>() == "",
        "pending login cannot disclose member ID");
    var registeredBefore = (await users.Find(u => u.Id == newId).SingleAsync()).ToJson();
    var newAccount = await users.Find(u => u.Id == newId).SingleAsync();
    var aadhaarProtector = new AadhaarNumberService(new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(settings).Build());
    Check(newAccount.AadhaarPhotoPath == "" && newAccount.AadhaarNumberCiphertext.StartsWith("v1:") &&
        !newAccount.AadhaarNumberCiphertext.Contains("234567890124") &&
        aadhaarProtector.Mask(newAccount.AadhaarNumberCiphertext) == "•••• •••• 0124",
        "new member stores only encrypted Aadhaar number and displays only its last four digits");
    Check(newAccount.MobileVerified == true, "OTP registration stores verified mobile ownership");
    Check(System.Text.RegularExpressions.Regex.IsMatch(newAccount.MemberId, @"^WTS-CHN-[0-9]{4,}$") && newAccount.IdSchemeVersion == 2,
        "new registration stores a district-scoped, immutable server ID");
    Check(newAccount.ConsentAcceptedAt.HasValue && newAccount.ConsentPolicyVersion == "2026-11-13" && !newAccount.ApprovedAt.HasValue,
        "new registration records policy version and timestamp without automatic approval");
    var displayId = newAccount.MemberId;
    Check((await http.GetAsync($"/api/users/verify/{Uri.EscapeDataString(displayId)}")).StatusCode == HttpStatusCode.NotFound,
        "public verification does not disclose pending member ID");
    await Json(await http.GetAsync($"/api/users/verify/{legacy.MemberId}"), HttpStatusCode.OK, "existing year-based verification remains compatible");
    var uploadSnapshots = new[] { newAccount.ProfilePhotoPath }.ToDictionary(
        p => Path.Combine(runRoot, "wwwroot", p.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)),
        p => File.ReadAllBytes(Path.Combine(runRoot, "wwwroot", p.TrimStart('/').Replace('/', Path.DirectorySeparatorChar))));
    foreach (var item in new[] { ("9876543230", "Different name", "NewMember@123"), ("9876543230", "New member", "Different@123"),
        ("+919876543230", "Prefix", "Different@123"), ("919876543230", "Prefix2", "Different@123") })
    {
        var duplicate = await Json(await http.PostAsync("/api/users/register", Registration(item.Item1, "", item.Item2, item.Item3)),
            HttpStatusCode.Conflict, "TC02/03/04/06: direct duplicate blocked");
        Check(duplicate["code"]!.GetValue<string>() == "mobile_already_registered", "controlled duplicate error code");
    }
    await Post("/api/auth/send-otp", new { phoneNumber = "+919876543230" }, HttpStatusCode.Conflict, "duplicate blocked before OTP");
    await Post("/api/otp/send", new { phoneNumber = "919876543230" }, HttpStatusCode.Conflict, "legacy OTP route cannot bypass duplicate check");
    await Post("/api/auth/verify-otp", new { phoneNumber = "9876543230", otp = captured.Code("9876543230") }, HttpStatusCode.Conflict, "verification rechecks duplicate registration");
    Check((await statisticsService.GetAsync(default)).TotalRegisteredMembers == beforeRegistrationStatistics.TotalRegisteredMembers + 1,
        "duplicate registration attempts cannot increase public count");
    Check(await users.CountDocumentsAsync(u => u.CanonicalMobile == "9876543230") == 1 &&
        registeredBefore == (await users.Find(u => u.Id == newId).SingleAsync()).ToJson(), "TC11/14/17: duplicate attempts preserve entire account");
    Check(uploadSnapshots.All(p => File.ReadAllBytes(p.Key).SequenceEqual(p.Value)), "TC17: passport file bytes remain intact");

    var login = await Post("/api/users/login", new { phone = "919876543210", password = "Original@123" }, HttpStatusCode.OK, "TC07: correct legacy password and normalized phone");
    Check(login["userId"]!.GetValue<string>() == legacy.Id && !login.ContainsKey("passwordHash"), "login resolves correct account without hash disclosure");
    await Post("/api/users/login", new { phone = "+919876543210", password = "Wrong@123" }, HttpStatusCode.Unauthorized, "TC08: wrong password");
    await Post("/api/users/login", new { phone = "9876543299", password = "Wrong@123" }, HttpStatusCode.Unauthorized, "TC09: unknown phone");

    async Task<HttpResponseMessage> GetWithToken(string path, string? bearer)
    {
        var message = new HttpRequestMessage(HttpMethod.Get, path);
        if (bearer != null) message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return await http.SendAsync(message);
    }
    foreach (var route in new[] { $"/api/users/{newId}/id-card", $"/api/idcards/{newId}" })
        Check((await GetWithToken(route, memberToken)).StatusCode == HttpStatusCode.Forbidden,
            "pending member cannot access card through either API route");
    foreach (var route in new[] { $"/api/users/{legacy.Id}/id-card", $"/api/idcards/{legacy.Id}" })
    {
        Check((await GetWithToken(route, memberToken)).StatusCode == HttpStatusCode.Forbidden, "TC15: another member card forbidden on both routes");
        Check((await GetWithToken(route, null)).StatusCode == HttpStatusCode.Unauthorized, "TC15: stored userId alone cannot authorize");
    }
    Check((await GetWithToken($"/api/users/{newId}/id-card", memberToken + "tamper")).StatusCode == HttpStatusCode.Unauthorized, "tampered session rejected");
    foreach (var route in new[] { "/api/users", $"/api/users/{legacy.Id}", "/api/admin/users/export", "/api/dashboard/summary" })
        Check((await GetWithToken(route, memberToken)).StatusCode == HttpStatusCode.Forbidden, "member token cannot access admin route " + route);
    var admin = await Post("/api/admin/login", new { username = "test-admin", password = "IsolatedAdmin@123" }, HttpStatusCode.OK, "admin login preserved");
    var adminToken = admin["token"]!.GetValue<string>();
    var adminPendingPreview = await Json(await GetWithToken($"/api/users/{newId}/id-card", adminToken),
        HttpStatusCode.OK, "admin can preview pending member card");
    Check(adminPendingPreview["isApproved"]!.GetValue<bool>() == false &&
        (await GetWithToken($"/api/users/{newId}/id-card", null)).StatusCode == HttpStatusCode.Unauthorized,
        "pending card preview remains admin-only and unauthenticated access is denied");
    var adminAadhaarView = await Json(await GetWithToken($"/api/admin/users/{newId}", adminToken), HttpStatusCode.OK, "admin sees masked Aadhaar number");
    Check(adminAadhaarView["aadhaarNumberMasked"]!.GetValue<string>() == "•••• •••• 0124" &&
        !adminAadhaarView.ToJsonString().Contains("234567890124") &&
        !adminAadhaarView.ToJsonString().Contains(newAccount.AadhaarNumberCiphertext),
        "admin API masks Aadhaar and never returns plaintext or ciphertext");
    Check((await GetWithToken($"/api/admin/users/{newId}", memberToken)).StatusCode == HttpStatusCode.Forbidden,
        "normal member cannot access admin Aadhaar view");
    async Task<HttpResponseMessage> Reveal(string? token, string password, string memberId = "")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/users/{(memberId == "" ? newId : memberId)}/aadhaar/reveal")
            { Content = JsonContent.Create(new { password }) };
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await http.SendAsync(request);
    }
    Check((await Reveal(null, "IsolatedAdmin@123")).StatusCode == HttpStatusCode.Unauthorized &&
        (await Reveal(memberToken, "IsolatedAdmin@123")).StatusCode == HttpStatusCode.Forbidden,
        "anonymous and member tokens cannot reveal Aadhaar");
    var wrongReveal = await Reveal(adminToken, "Wrong@123");
    Check(wrongReveal.StatusCode == HttpStatusCode.Unauthorized &&
        !(await wrongReveal.Content.ReadAsStringAsync()).Contains("234567890124"),
        "admin reveal requires fresh correct password");
    var revealed = await Reveal(adminToken, "IsolatedAdmin@123");
    var revealedBody = await Json(revealed, HttpStatusCode.OK, "admin reveals synthetic Aadhaar after reauthentication");
    Check(revealedBody["aadhaarNumber"]!.GetValue<string>() == "234567890124" &&
        revealed.Headers.CacheControl?.NoStore == true,
        "reveal returns decryptable synthetic number with no-store header");
    var revealAudits = await context.AadhaarRevealAudits.Find(a => a.MemberRecordId == newId).ToListAsync();
    Check(revealAudits.Count == 2 && revealAudits.Any(a => a.Result == "Revealed") &&
        revealAudits.Any(a => a.Result == "Reauthentication failed") &&
        revealAudits.All(a => a.AdminName == "test-admin" && a.TimestampUtc <= DateTime.UtcNow &&
            !a.ToBsonDocument().ToJson().Contains("234567890124")),
        "audit records success and failed reauthentication without Aadhaar data");
    Check((await Reveal(adminToken, "IsolatedAdmin@123", legacy.Id!)).StatusCode == HttpStatusCode.NotFound,
        "legacy image-only records cannot invent missing numbers");
    var publicContent = await Json(await http.GetAsync("/api/website-content"), HttpStatusCode.OK, "public content includes the original constitution");
    Check(publicContent["aboutMarkdown"]!.GetValue<string>().Contains("விதி 30"), "constitution is preserved before first edit");
    async Task<HttpResponseMessage> PutContent(string? bearer, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/website-content") { Content = JsonContent.Create(body) };
        if (bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return await http.SendAsync(request);
    }
    var galleryUpdate = new { section = "gallery", revision = 0, items = new[] { new { id = "synthetic-gallery", title = "Synthetic gallery", description = "Test only", imagePath = "", order = 0 } } };
    Check((await PutContent(null, galleryUpdate)).StatusCode == HttpStatusCode.Unauthorized, "public content edit denied");
    Check((await PutContent(memberToken, galleryUpdate)).StatusCode == HttpStatusCode.Forbidden, "member content edit denied");
    Check((await PutContent(adminToken, new { section = "gallery", revision = 0, items = new[] {
        new { id = "synthetic-xss", title = "<script>alert(1)</script>", description = "Test only", imagePath = "", order = 0 }
    } })).StatusCode == HttpStatusCode.BadRequest, "admin content rejects HTML markup before persistence");
    await Json(await PutContent(adminToken, galleryUpdate), HttpStatusCode.OK, "admin content edit saved");
    var savedContent = await Json(await http.GetAsync("/api/website-content"), HttpStatusCode.OK, "public content edit persisted");
    Check(savedContent["gallery"]!.AsArray().Single()!["title"]!.GetValue<string>() == "Synthetic gallery" &&
        savedContent["aboutMarkdown"]!.GetValue<string>() == publicContent["aboutMarkdown"]!.GetValue<string>(), "gallery edit preserves full About document");
    var historyResponse = await GetWithToken("/api/website-content/history", adminToken);
    Check(historyResponse.StatusCode == HttpStatusCode.OK, "content history available to admin");
    var history = JsonNode.Parse(await historyResponse.Content.ReadAsStringAsync())!.AsArray();
    Check(history.Count == 1, "content history records one edit");
    using (var contentImage = new MultipartFormDataContent())
    {
        var source = await File.ReadAllBytesAsync(Path.Combine(runRoot, "wwwroot", newAccount.ProfilePhotoPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
        var bytes = new ByteArrayContent(source);
        bytes.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        contentImage.Add(bytes, "image", "gallery.png");
        using var upload = new HttpRequestMessage(HttpMethod.Post, "/api/website-content/images") { Content = contentImage };
        upload.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var imageResult = await Json(await http.SendAsync(upload), HttpStatusCode.OK, "admin uploads public gallery image into protected storage");
        var imagePath = imageResult["imagePath"]!.GetValue<string>();
        Check(imagePath.StartsWith("/uploads/content-"), "gallery image receives isolated content reference");
        await Json(await PutContent(adminToken, new { section = "gallery", revision = 1, items = new[] {
            new { id = "synthetic-gallery", title = "Updated synthetic gallery", description = "Reordered", imagePath, order = 2 }
        } }), HttpStatusCode.OK, "admin updates gallery image and order");
        Check((await http.GetAsync($"/api/website-content/images/{Path.GetFileName(imagePath)}")).StatusCode == HttpStatusCode.OK,
            "public can view only referenced gallery image");
        Check((await http.GetAsync($"/api/website-content/images/{Path.GetFileName(legacy.AadhaarPhotoPath)}")).StatusCode == HttpStatusCode.NotFound,
            "private Aadhaar path cannot be served as public website content");
    }
    var originalAbout = publicContent["aboutMarkdown"]!.GetValue<string>();
    var editedAbout = originalAbout.Replace("# வெல்டிங் தொழிலாளர்கள் சங்கம்", "# வெல்டிங் தொழிலாளர்கள் சங்கம் – சோதனை");
    Check(editedAbout != originalAbout, "synthetic About heading fixture matches the original constitution");
    Check((await PutContent(adminToken, new { section = "about", revision = 2, aboutMarkdown = "Only a summary", aboutContact = "Synthetic contact" })).StatusCode == HttpStatusCode.BadRequest,
        "admin cannot remove the constitution through content editing");
    await Json(await PutContent(adminToken, new { section = "about", revision = 2, aboutMarkdown = editedAbout, aboutContact = "Synthetic contact" }),
        HttpStatusCode.OK, "admin saves About contact while preserving constitution");
    await Json(await PutContent(adminToken, new { section = "products", revision = 3, items = new[] {
        new { id = "synthetic-product", title = "Synthetic welding product", description = "Test only", imagePath = "", order = 1 }
    } }), HttpStatusCode.OK, "admin saves Products content");
    var allContent = await Json(await http.GetAsync("/api/website-content"), HttpStatusCode.OK, "public About and Products are persistent");
    Check(allContent["aboutMarkdown"]!.GetValue<string>() == editedAbout.Trim() &&
        allContent["aboutContact"]!.GetValue<string>() == "Synthetic contact" &&
        allContent["products"]!.AsArray().Single()!["title"]!.GetValue<string>() == "Synthetic welding product",
        "About, Gallery and Products coexist without losing the full constitution");
    var photoRoute = $"/api/admin/users/{newId}/profile-photo";
    using (var unauthorizedPhoto = new MultipartFormDataContent())
    {
        unauthorizedPhoto.Add(new ByteArrayContent(uploadSnapshots.Values.Last()), "photo", "test.png");
        Check((await http.PostAsync(photoRoute, unauthorizedPhoto)).StatusCode == HttpStatusCode.Unauthorized, "anonymous photo replacement denied");
    }
    foreach (var officer in new[] { "president", "secretary", "treasurer" })
    {
        var route = $"/api/users/{newId}/officer-signatures/{officer}";
        Check((await GetWithToken(route, null)).StatusCode == HttpStatusCode.Unauthorized,
            $"anonymous {officer} signature denied");
        Check((await GetWithToken(route, login["token"]!.GetValue<string>())).StatusCode == HttpStatusCode.Forbidden,
            $"cross-member {officer} signature denied");
        Check((await GetWithToken(route, memberToken)).StatusCode == HttpStatusCode.Forbidden,
            $"pending member {officer} signature denied");
        Check((await GetWithToken(route, adminToken)).StatusCode == HttpStatusCode.OK,
            $"admin {officer} signature access");
        Check((await GetWithToken($"/Assets/OfficerSignatures/state-{officer}-signature.png", null)).StatusCode == HttpStatusCode.NotFound,
            $"raw {officer} signature is not a static asset");
        Check((await GetWithToken($"/Assets/OfficerSignatures/state-{officer}-signature-blue.png", null)).StatusCode == HttpStatusCode.NotFound,
            $"blue {officer} signature is not a static asset");
    }
    var pendingDashboard = await Json(await GetWithToken("/api/dashboard/summary", adminToken), HttpStatusCode.OK, "admin dashboard pending count");
    Check(pendingDashboard["pendingApprovalCount"]!.GetValue<long>() >= 1, "new registration notifies admin exactly through pending count");
    var pendingList = JsonNode.Parse(await (await GetWithToken("/api/admin/pending-approvals", adminToken)).Content.ReadAsStringAsync())!.AsArray();
    Check(pendingList.Count(member => member!["id"]!.GetValue<string>() == newId) == 1,
        "pending approval list contains registration once");
    Check((await http.PutAsJsonAsync($"/api/admin/users/{newId}/approval", new { approved = true })).StatusCode == HttpStatusCode.Unauthorized,
        "anonymous approval is denied");
    using (var approval = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{newId}/approval"))
    {
        approval.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        approval.Content = JsonContent.Create(new { approved = true });
        await Json(await http.SendAsync(approval), HttpStatusCode.OK, "admin approves member");
    }
    var approvedCard = await Json(await GetWithToken($"/api/users/{newId}/id-card", memberToken), HttpStatusCode.OK, "approved card preview");
    Check(approvedCard["memberId"]!.GetValue<string>() == newAccount.MemberId &&
        !approvedCard.ContainsKey("aadhaarPhotoPath") && !approvedCard.ContainsKey("passwordHash"), "approved card exposes only safe fields");
    Check((await http.GetAsync($"/api/users/verify/{Uri.EscapeDataString(displayId)}")).StatusCode == HttpStatusCode.OK,
        "public verification starts only after approval");
    Check((await GetWithToken($"/api/idcards/{newId}", memberToken)).StatusCode == HttpStatusCode.OK,
        "alternate card route starts only after approval");
    foreach (var officer in new[] { "president", "secretary", "treasurer" })
    {
        var signed = await GetWithToken($"/api/users/{newId}/officer-signatures/{officer}", memberToken);
        Check(signed.StatusCode == HttpStatusCode.OK && signed.Headers.CacheControl?.NoStore == true &&
            (await signed.Content.ReadAsByteArrayAsync()).SequenceEqual(
                File.ReadAllBytes(Path.Combine(signatureSource, $"state-{officer}-signature-blue.png"))),
            $"approved owner {officer} signature is exact and no-store");
    }
    Check(approvedCard["isApproved"]!.GetValue<bool>() && approvedCard["verificationStatus"]!.GetValue<string>() == "Verified",
        "admin approval alone enables VERIFIED badge");
    Check((await statisticsService.GetAsync(default)).TotalRegisteredMembers == beforeRegistrationStatistics.TotalRegisteredMembers + 1,
        "approved member enters verified public count");
    Check(approvedCard["expiresAt"]!.GetValue<DateTime>() == CardValidity.Expiry(newAccount.CreatedAt),
        "card expiry uses persisted registration date plus three calendar years");
    var approvedSnapshot = (await users.Find(u => u.Id == newId).SingleAsync()).ToJson();
    if (officialIds)
    {
        var oldCard = await Json(await GetWithToken($"/api/idcards/{legacy.Id}", adminToken), HttpStatusCode.OK, "existing ID card alias on alternate route");
        Check(oldCard["displayMemberId"]!.GetValue<string>() == existingView.DisplayMemberId && oldCard["memberId"]!.GetValue<string>() == legacy.MemberId,
            "both private card contracts preserve stored ID and expose the existing-member alias");
        foreach (var ambiguous in new[] { "WTS-TEST-REG/2026-FD48EF", identityFixtures[4].MemberId })
        {
            var conflict = await Json(await http.GetAsync($"/api/users/verify/{Uri.EscapeDataString(ambiguous)}"), HttpStatusCode.Conflict, "ambiguous lookup fails closed");
            Check(conflict["code"]!.GetValue<string>() == "ambiguous_member_id" && !conflict.ContainsKey("name"), "conflict discloses no candidate identities");
        }
        var printed = await Json(await http.GetAsync($"/api/users/verify/{identityFixtures[0].MemberId}"), HttpStatusCode.OK, "original printed ID survives a suffix collision");
        Check(printed["memberId"]!.GetValue<string>() == identityFixtures[0].MemberId, "exact old identity is never redirected to a colliding alias");
    }
    var missingBefore = (await users.Find(u => u.Id == identityFixtures[6].Id).SingleAsync()).ToJson();
    var missingCard = await Json(await GetWithToken($"/api/users/{identityFixtures[6].Id}/id-card", adminToken), HttpStatusCode.OK, "legacy missing-ID card read");
    Check(missingCard["memberId"]!.GetValue<string>() == "" && missingCard["displayMemberId"]!.GetValue<string>() == "" &&
        missingBefore == (await users.Find(u => u.Id == identityFixtures[6].Id).SingleAsync()).ToJson(), "missing IDs stay missing after authenticated reads");
    Check((await GetWithToken(legacy.AadhaarPhotoPath, null)).StatusCode == HttpStatusCode.Unauthorized &&
        (await GetWithToken(newAccount.ProfilePhotoPath, null)).StatusCode == HttpStatusCode.Unauthorized, "uploaded documents are never public static files");
    Check((await GetWithToken(legacy.AadhaarPhotoPath, memberToken)).StatusCode == HttpStatusCode.Forbidden,
        "member cannot fetch private Aadhaar even with a valid session");
    Check((await GetWithToken(newAccount.ProfilePhotoPath, login["token"]!.GetValue<string>())).StatusCode == HttpStatusCode.Forbidden,
        "another member cannot fetch passport image");
    var ownerPhoto = await GetWithToken(newAccount.ProfilePhotoPath, memberToken);
    Check(ownerPhoto.IsSuccessStatusCode && ownerPhoto.Headers.CacheControl?.NoStore == true &&
        (await ownerPhoto.Content.ReadAsByteArrayAsync()).SequenceEqual(uploadSnapshots.Values.Last()), "owner passport fetch is authenticated, intact and no-store");
    Check((await GetWithToken(legacy.AadhaarPhotoPath, adminToken)).IsSuccessStatusCode &&
        (await GetWithToken(newAccount.ProfilePhotoPath, adminToken)).IsSuccessStatusCode, "admin document access remains functional");
    using (var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/districts"))
    {
        preflight.Headers.Add("Origin", "http://192.168.1.22:5173"); preflight.Headers.Add("Access-Control-Request-Method", "GET");
        var cors = await http.SendAsync(preflight);
        Check(cors.Headers.TryGetValues("Access-Control-Allow-Origin", out var origins) && origins.Single() == "http://192.168.1.22:5173",
            "LAN browser preflight permits the development host");
    }
    var list = await GetWithToken("/api/users", adminToken);
    Check(list.IsSuccessStatusCode && !(await list.Content.ReadAsStringAsync()).Contains("passwordHash"), "TC13: admin user list");
    var export = await GetWithToken("/api/admin/users/export", adminToken);
    Check(export.IsSuccessStatusCode && export.Content.Headers.ContentType!.MediaType == "text/csv" &&
        (await export.Content.ReadAsStringAsync()).Contains("WTS-2024-LEGACY"), "TC13: Excel-compatible export preserves member IDs");
    Check((await GetWithToken($"/api/users/{legacy.Id}/id-card", adminToken)).IsSuccessStatusCode, "admin ID card preview preserved");

    await Post("/api/users/forgot-password/send-otp", new { phone = "+919876543210" }, HttpStatusCode.OK, "existing password reset send");
    var resetProof = await Post("/api/auth/verify-otp", new { phoneNumber = "9876543210", otp = captured.Code("9876543210"), purpose = "password-reset" }, HttpStatusCode.OK, "password reset OTP purpose");
    await Post("/api/users/forgot-password/reset", new { phone = "919876543210", verificationToken = resetProof["verificationToken"]!.GetValue<string>(), newPassword = "ResetSecure@123" }, HttpStatusCode.OK, "TC10: reset intended legacy account");
    var afterReset = await users.Find(u => u.Id == legacy.Id).SingleAsync();
    Check(afterReset.MemberId == legacy.MemberId && afterReset.PasswordHash != legacy.PasswordHash &&
        afterReset.AadhaarPhotoPath == legacy.AadhaarPhotoPath && afterReset.ProfilePhotoPath == legacy.ProfilePhotoPath &&
        approvedSnapshot == (await users.Find(u => u.Id == newId).SingleAsync()).ToJson(), "TC10/11/17: reset preserves Member ID/documents and other account");
    var legacyLogin = await Post("/api/users/login", new { phone = "9876543210", password = "ResetSecure@123" }, HttpStatusCode.OK, "reset password authenticates");
    var legacyToken = legacyLogin["token"]!.GetValue<string>();

    // Exercise the real limiter instead of disabling it for tests. Wait for the next window once.
    Console.WriteLine("Waiting 61 seconds for the authentication rate limit window before remaining OTP tests.");
    await Task.Delay(TimeSpan.FromSeconds(61));
    await Post("/api/users/forgot-password/send-otp", new { phone = "9876543211" }, HttpStatusCode.OK, "legacy passwordless setup OTP");
    var setup = await Post("/api/auth/verify-otp", new { phoneNumber = "+919876543211", otp = captured.Code("9876543211"), purpose = "password-reset" }, HttpStatusCode.OK, "legacy setup verification");
    await Post("/api/users/forgot-password/reset", new { phone = "9876543211", verificationToken = setup["verificationToken"]!.GetValue<string>(), newPassword = "LegacySetup@123" }, HttpStatusCode.OK, "passwordless account setup");
    var oldLogin = await Post("/api/users/login", new { phone = "9876543211", password = "LegacySetup@123" }, HttpStatusCode.OK, "legacy setup login");
    Check(oldLogin["memberId"]!.GetValue<string>() == noPassword.MemberId, "legacy setup retains Member ID");
    await Json(await http.PostAsync("/api/users/register", Registration("9876543240", "forged")), HttpStatusCode.BadRequest, "TC16: unverified phone rejected");
    await Post("/api/auth/send-otp", new { phoneNumber = "9876543240" }, HttpStatusCode.OK, "mismatch OTP send");
    var mismatch = await Post("/api/auth/verify-otp", new { phoneNumber = "9876543240", otp = captured.Code("9876543240") }, HttpStatusCode.OK, "mismatch OTP verify");
    await Json(await http.PostAsync("/api/users/register", Registration("9876543241", mismatch["verificationToken"]!.GetValue<string>())), HttpStatusCode.BadRequest, "TC16: mismatched verified phone rejected");
    await Post("/api/users/forgot-password/reset", new { phone = "9876543211", verificationToken = mismatch["verificationToken"]!.GetValue<string>(), newPassword = "Attack@123" }, HttpStatusCode.BadRequest, "registration proof cannot reset password");
    Check(await users.CountDocumentsAsync(u => u.CanonicalMobile == "9876543240" || u.CanonicalMobile == "9876543241") == 0, "unverified attempts created no accounts");
    await Post("/api/auth/send-otp", new { phoneNumber = "9876543250" }, HttpStatusCode.OK, "cooldown OTP send");
    await Post("/api/auth/send-otp", new { phoneNumber = "+919876543250" }, HttpStatusCode.TooManyRequests, "normalized phone cooldown");
    if (args.Contains("--browser"))
    {
        var fixturePath = Path.Combine(runRoot, "browser-fixture.json");
        await File.WriteAllTextAsync(fixturePath, System.Text.Json.JsonSerializer.Serialize(new {
            root, runRoot, captureKey, apiUrl = http.BaseAddress!.ToString().TrimEnd('/'),
            phone = "9876543230", password = "NewMember@123", otherId = legacy.Id, memberId = newAccount.MemberId, displayId,
            legacyId = legacy.MemberId, legacyDisplayId = officialIds ? existingView.DisplayMemberId : legacy.MemberId,
            adminToken, imagePath = uploadSnapshots.Keys.Last(), userId = newId, officialIds,
            officeAddress = "Synthetic test office, Test street\nTest district 600001",
            statisticsMemberCount = (await statisticsService.GetAsync(default)).TotalRegisteredMembers
        }));
        var browserStart = new ProcessStartInfo("node") { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        browserStart.ArgumentList.Add(Path.Combine(root, "tests", "browser-security.mjs"));
        browserStart.ArgumentList.Add(fixturePath);
        using var browser = Process.Start(browserStart)!;
        var browserOutput = browser.StandardOutput.ReadToEndAsync();
        var browserError = browser.StandardError.ReadToEndAsync();
        await browser.WaitForExitAsync();
        Console.Write(await browserOutput);
        Console.Error.Write(await browserError);
        Check(browser.ExitCode == 0, "TC03/12/15: browser duplicate guidance, ID card PDF download, reload, ownership");
    }
    var limited = false;
    for (var i = 0; i < 25; i++)
    {
        var response = await http.PostAsJsonAsync("/api/auth/send-otp", new { phoneNumber = "9876543230" });
        if (response.StatusCode == HttpStatusCode.TooManyRequests) { limited = true; break; }
    }
    Check(limited, "registration account enumeration is IP rate limited");
    var statisticsLimited = false;
    for (var i = 0; i < 31; i++)
    {
        using var response = await http.GetAsync("/api/statistics");
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            statisticsLimited = response.Headers.RetryAfter is not null;
            break;
        }
    }
    Check(statisticsLimited, "public statistics have a separate rate limit and Retry-After");
    var adminLimited = false;
    for (var i = 0; i < 11; i++)
    {
        using var response = await http.PostAsJsonAsync("/api/admin/login", new { username = "test-admin", password = "wrong" });
        if (response.StatusCode == HttpStatusCode.TooManyRequests) { adminLimited = response.Headers.RetryAfter is not null; break; }
        Check(response.StatusCode == HttpStatusCode.Unauthorized, "incorrect admin password never authorizes");
    }
    Check(adminLimited, "admin password guessing has a separate rate limit");
    var verificationLimited = false;
    for (var i = 0; i < 31; i++)
    {
        using var response = await http.GetAsync("/api/users/verify/NO-SUCH-ID");
        if (response.StatusCode == HttpStatusCode.TooManyRequests) { verificationLimited = response.Headers.RetryAfter is not null; break; }
    }
    Check(verificationLimited, "public verification lookup has a separate rate limit");
    var districtForAdminTest = await context.Districts.Find(d => d.Name == "Chennai").SingleAsync();
    var testGroupLink = "https://chat.whatsapp.com/synthetic-staging-test-only";
    using (var memberUpdate = new HttpRequestMessage(HttpMethod.Put, $"/api/districts/{districtForAdminTest.Id}")
    {
        Content = JsonContent.Create(new { name = "Chennai", whatsAppGroupLink = testGroupLink }),
        Headers = { Authorization = new AuthenticationHeaderValue("Bearer", memberToken) }
    })
        Check((await http.SendAsync(memberUpdate)).StatusCode == HttpStatusCode.Forbidden,
            "member cannot manage district links");
    using (var adminUpdate = new HttpRequestMessage(HttpMethod.Put, $"/api/districts/{districtForAdminTest.Id}")
    {
        Content = JsonContent.Create(new { name = "Chennai", whatsAppGroupLink = testGroupLink }),
        Headers = { Authorization = new AuthenticationHeaderValue("Bearer", adminToken) }
    })
        Check((await http.SendAsync(adminUpdate)).StatusCode == HttpStatusCode.NoContent,
            "admin can manage district links");
    Check((await context.Districts.Find(d => d.Id == districtForAdminTest.Id).SingleAsync()).WhatsAppGroupLink == testGroupLink,
        "district management persists only in the isolated test database");
    using (var disabledApi = new IsolatedApi(runRoot,
        new Dictionary<string, string?>(settings) { ["Authentication:OtpEnabled"] = "false" }, captured))
    using (var disabledHttp = disabledApi.CreateClient())
    {
        var options = await Json(await disabledHttp.GetAsync("/api/auth/registration-options"), HttpStatusCode.OK,
            "disabled registration options");
        Check(options["otpEnabled"]!.GetValue<bool>() == false, "backend publishes disabled registration OTP mode");
        await Json(await disabledHttp.PostAsJsonAsync("/api/auth/send-otp", new { phoneNumber = "9876543270" }),
            HttpStatusCode.Conflict, "disabled registration cannot send OTP");
        await Json(await disabledHttp.PostAsJsonAsync("/api/otp/verify", new { phoneNumber = "9876543270", otp = "123456" }),
            HttpStatusCode.Conflict, "legacy route cannot verify registration OTP while disabled");
        await Json(await disabledHttp.PostAsync("/api/users/register", Registration("9876543270", name: "")),
            HttpStatusCode.BadRequest, "disabled OTP still validates registration fields");
        await Json(await disabledHttp.PostAsync("/api/users/register", Registration("9876543270", consent: false)),
            HttpStatusCode.BadRequest, "direct API registration rejects missing policy acceptance");
        Check(!await users.Find(u => u.CanonicalMobile == "9876543270").AnyAsync(), "missing consent writes no member");
        var unverifiedRegistration = await Json(await disabledHttp.PostAsync("/api/users/register", Registration("9876543270")),
            HttpStatusCode.OK, "registration without OTP");
        Check(unverifiedRegistration["mobileVerified"]!.GetValue<bool>() == false, "registration reports unverified mobile");
        var unverified = await users.Find(u => u.Id == unverifiedRegistration["userId"]!.GetValue<string>()).SingleAsync();
        Check(unverified.MobileVerified == false, "registration persists unverified mobile");
        await Json(await disabledHttp.PostAsync("/api/users/register", Registration("+919876543270")),
            HttpStatusCode.Conflict, "disabled OTP retains duplicate-mobile check");
        await Json(await disabledHttp.PostAsJsonAsync("/api/users/login", new { phone = "9876543270", password = "NewMember@123" }),
            HttpStatusCode.OK, "unverified mobile can still use password login");
        var recoverySend = await Json(await disabledHttp.PostAsJsonAsync("/api/users/forgot-password/send-otp", new { phone = "9876543270" }),
            HttpStatusCode.OK, "unverified mobile recovery send has generic response");
        Check(!recoverySend.ContainsKey("developmentOtp"), "unverified mobile receives no password recovery OTP");
        await Json(await disabledHttp.PostAsJsonAsync("/api/auth/verify-otp", new { phoneNumber = "9876543270", otp = "123456", purpose = "password-reset" }),
            HttpStatusCode.BadRequest, "unverified mobile cannot verify password reset OTP");
        await Json(await disabledHttp.PostAsJsonAsync("/api/users/forgot-password/reset", new { phone = "9876543270", verificationToken = "forged", newPassword = "Changed@123" }),
            HttpStatusCode.BadRequest, "unverified mobile cannot reset password");
        Check((await users.Find(u => u.Id == unverified.Id).SingleAsync()).PasswordHash == unverified.PasswordHash,
            "blocked recovery preserves unverified account password");
    }
    var failedUploads = new FailingPassportUpload();
    using (var failingApi = new IsolatedApi(runRoot,
        new Dictionary<string, string?>(settings) { ["Authentication:OtpEnabled"] = "false" }, captured, failedUploads))
    using (var failingHttp = failingApi.CreateClient())
    {
        var failedRegistration = await failingHttp.PostAsync("/api/users/register", Registration("9876543288"));
        Check(failedRegistration.StatusCode == HttpStatusCode.InternalServerError,
            "failed passport upload blocks registration");
        Check(failedUploads.SaveCount == 1 && failedUploads.Deleted.Count == 0,
            "failed registration creates no private upload reference");
        Check(!await users.Find(u => u.CanonicalMobile == "9876543288").AnyAsync(),
            "failed upload creates no member record");
    }
    var configuredDistricts = await context.Districts.Find(_ => true).Project(d => d.Name).ToListAsync();
    Check(configuredDistricts.Count == 38 && configuredDistricts.All(name => DistrictMemberCode.TryGet(name, out _)) &&
        DistrictMemberCode.All.Values.Distinct().Count() == DistrictMemberCode.All.Count &&
        DistrictMemberCode.All["Ariyalur"] == "ALR" && DistrictMemberCode.All["Coimbatore"] == "CBE",
        "all configured districts have unique stable ID codes");
    Check(!DistrictMemberCode.TryGet("Unknown", out _), "unknown districts cannot receive guessed codes");
    Check(CardValidity.Expiry(new DateTime(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc)) == new DateTime(2029, 11, 12, 12, 0, 0, DateTimeKind.Utc) &&
        CardValidity.Expiry(new DateTime(2024, 2, 29, 12, 0, 0, DateTimeKind.Utc)) == new DateTime(2027, 2, 28, 12, 0, 0, DateTimeKind.Utc),
        "calendar-year expiry handles ordinary and leap-day dates");
    // Corrections exercise a synthetic registration in this isolated database only.
    var editRoute = $"/api/admin/users/{newId}";
    var edit = new EditMemberRequest { Name = "Corrected synthetic member", PhoneNumber = "9876543260",
        Location = "Legacy location", Posting = "ஊர் தலைவர்", DateOfBirth = new DateTime(1991, 2, 3, 0, 0, 0, DateTimeKind.Utc),
        BloodGroup = "A+", FatherName = "Corrected father", PermanentAddress = "Corrected test address",
        Pincode = "600002", District = "Madurai" };
    async Task<HttpResponseMessage> PutEdit(string target, object body, string? bearer)
    {
        using var message = new HttpRequestMessage(HttpMethod.Put, target) { Content = JsonContent.Create(body) };
        if (bearer is not null) message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return await http.SendAsync(message);
    }
    Check((await PutEdit(editRoute, edit, null)).StatusCode == HttpStatusCode.Unauthorized,
        "unauthenticated member edit rejected");
    Check((await PutEdit(editRoute, edit, memberToken)).StatusCode == HttpStatusCode.Forbidden,
        "member token cannot edit even its own record");
    Check((await PutEdit(editRoute, new { edit.Name, edit.PhoneNumber, edit.Location, posting = "<script>alert(1)</script>", edit.DateOfBirth,
        edit.BloodGroup, edit.FatherName, edit.PermanentAddress, edit.Pincode, edit.District }, adminToken)).StatusCode == HttpStatusCode.BadRequest,
        "posting rejects HTML/script input on the server");
    Check((await PutEdit($"/api/admin/users/{legacy.Id}", edit, memberToken)).StatusCode == HttpStatusCode.Forbidden,
        "member token cannot edit another member record");
    Check((await PutEdit(editRoute, new { edit.Name, edit.PhoneNumber, edit.Location, edit.DateOfBirth,
        edit.BloodGroup, edit.FatherName, edit.PermanentAddress, pincode = "bad", edit.District }, adminToken)).StatusCode == HttpStatusCode.BadRequest,
        "invalid edit rejected");
    Check((await PutEdit(editRoute, new { edit.Name, edit.PhoneNumber, edit.Location, edit.DateOfBirth,
        edit.BloodGroup, edit.FatherName, edit.PermanentAddress, edit.Pincode, edit.District, memberId = legacy.MemberId }, adminToken)).StatusCode == HttpStatusCode.BadRequest,
        "request cannot replace member identity");
    edit.PhoneNumber = "9876543210";
    Check((await PutEdit(editRoute, edit, adminToken)).StatusCode == HttpStatusCode.Conflict,
        "duplicate mobile edit rejected");
    edit.PhoneNumber = "9876543260";
    await users.UpdateOneAsync(u => u.Id == newId, Builders<UserRegistration>.Update.Unset(u => u.Posting));
    var beforeEdit = await users.Find(u => u.Id == newId).SingleAsync();
    Check(beforeEdit.Posting == "MEMBER", "historical records without Posting display the MEMBER default");
    await Json(await PutEdit(editRoute, edit, adminToken), HttpStatusCode.OK, "admin saves corrected member details");
    var afterEdit = await users.Find(u => u.Id == newId).SingleAsync();
    Check(afterEdit.Name == edit.Name && afterEdit.District == edit.District && afterEdit.PhoneNumber == edit.PhoneNumber &&
        afterEdit.CanonicalMobile == edit.PhoneNumber && afterEdit.Location == edit.Location && afterEdit.Posting == edit.Posting &&
        afterEdit.MemberId == beforeEdit.MemberId && afterEdit.IdSchemeVersion == beforeEdit.IdSchemeVersion &&
        afterEdit.CreatedAt == beforeEdit.CreatedAt && afterEdit.CardExpiresAt == beforeEdit.CardExpiresAt &&
        afterEdit.ApprovedAt == beforeEdit.ApprovedAt && afterEdit.PasswordHash == beforeEdit.PasswordHash &&
        afterEdit.AadhaarPhotoPath == beforeEdit.AadhaarPhotoPath && afterEdit.ProfilePhotoPath == beforeEdit.ProfilePhotoPath &&
        afterEdit.VerificationStatus == beforeEdit.VerificationStatus && afterEdit.ConsentAcceptedAt == beforeEdit.ConsentAcceptedAt &&
        afterEdit.ConsentPolicyVersion == beforeEdit.ConsentPolicyVersion,
        "edit updates allowed fields and preserves identity, expiry, approval, consent and documents");
    Check(afterEdit.EditHistory.Count == beforeEdit.EditHistory.Count + 1 && afterEdit.EditHistory[^1].AdminId == "test-admin" &&
        afterEdit.EditHistory[^1].MemberId == beforeEdit.MemberId &&
        afterEdit.EditHistory[^1].Changes[nameof(UserRegistration.Name)].NewValue == edit.Name &&
        afterEdit.EditHistory[^1].Changes[nameof(UserRegistration.Posting)].NewValue == edit.Posting &&
        afterEdit.EditHistory[^1].Timestamp > beforeEdit.CreatedAt,
        "atomic audit records admin, member, changes and timestamp");
    var detailAfterEdit = await Json(await GetWithToken($"/api/users/{newId}", adminToken), HttpStatusCode.OK, "admin refreshed member view");
    var cardAfterEdit = await Json(await GetWithToken($"/api/users/{newId}/id-card", adminToken), HttpStatusCode.OK, "admin refreshed card preview");
    Check(detailAfterEdit["name"]!.GetValue<string>() == edit.Name && cardAfterEdit["name"]!.GetValue<string>() == edit.Name &&
        detailAfterEdit["posting"]!.GetValue<string>() == edit.Posting && cardAfterEdit["posting"]!.GetValue<string>() == edit.Posting &&
        cardAfterEdit["district"]!.GetValue<string>() == edit.District &&
        cardAfterEdit["memberId"]!.GetValue<string>() == beforeEdit.MemberId &&
        cardAfterEdit["expiresAt"]!.GetValue<DateTime>() == beforeEdit.CardExpiresAt &&
        cardAfterEdit["isApproved"]!.GetValue<bool>() == beforeEdit.ApprovedAt.HasValue,
        "view and card export source reflect edits without changing ID, expiry or approval");
    Check((await userService.GetUsersAsync("Madurai")).Any(u => u.Id == newId && u.Name == edit.Name && u.Posting == edit.Posting),
        "admin user list reflects corrected district, name and posting");
    if (args.Contains("--admin-edit-browser"))
    {
        var browserPending = await userService.CreateAsync(new UserRegistration {
            PhoneNumber = "9888888821", District = "Chennai", Name = "Browser Pending Synthetic Member", ConsentAccepted = true
        });
        var browserRejected = await userService.CreateAsync(new UserRegistration {
            PhoneNumber = "9888888822", District = "Chennai", Name = "Browser Rejected Synthetic Member", ConsentAccepted = true
        });
        var browserCorrection = await userService.CreateAsync(new UserRegistration {
            PhoneNumber = "9888888823", District = "Chennai", Name = "Browser Correction Synthetic Member", ConsentAccepted = true
        });
        var fixturePath = Path.Combine(runRoot, "admin-edit-browser-fixture.json");
        await File.WriteAllTextAsync(fixturePath, System.Text.Json.JsonSerializer.Serialize(new {
            root, runRoot, apiUrl = http.BaseAddress!.ToString().TrimEnd('/'),
            adminToken, memberToken, userId = newId, otherId = legacy.Id, pendingId = browserPending.Id,
            rejectedId = browserRejected.Id, correctionId = browserCorrection.Id,
            photoPath = uploadSnapshots.Keys.Last()
        }));
        var browserStart = new ProcessStartInfo("node") { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        browserStart.ArgumentList.Add(Path.Combine(root, "tests", "browser-admin-edit.mjs"));
        browserStart.ArgumentList.Add(fixturePath);
        using var browser = Process.Start(browserStart)!;
        var browserOutput = browser.StandardOutput.ReadToEndAsync();
        var browserError = browser.StandardError.ReadToEndAsync();
        await browser.WaitForExitAsync();
        Console.Write(await browserOutput);
        Console.Error.Write(await browserError);
        Check(browser.ExitCode == 0, "browser completes Admin Users View Edit Save and updated PDF/PNG export");
        var browserApproved = await users.Find(u => u.Id == browserPending.Id).SingleAsync();
        Check(browserApproved.ApprovedAt.HasValue && browserApproved.ApprovalDecidedBy == "test-admin" &&
            browserApproved.EditHistory[^1].Changes["ApprovalStatus"].NewValue == "Approved",
            "browser dashboard approval persists actor and audit without duplicate notification");
        var browserRejectedAfter = await users.Find(u => u.Id == browserRejected.Id).SingleAsync();
        var browserCorrectionAfter = await users.Find(u => u.Id == browserCorrection.Id).SingleAsync();
        Check(UserService.GetApprovalStatus(browserRejectedAfter) == "Rejected" &&
            UserService.GetApprovalStatus(browserCorrectionAfter) == "Correction requested" &&
            browserRejectedAfter.EditHistory[^1].Changes["ApprovalStatus"].NewValue == "Rejected" &&
            browserCorrectionAfter.EditHistory[^1].Changes["ApprovalStatus"].NewValue == "Correction requested",
            "quick preview rejection and correction persist status and audit");
        var browserEdited = await users.Find(u => u.Id == newId).SingleAsync();
        Check(browserEdited.Name == "Browser Edited Synthetic Member" && browserEdited.District == "Coimbatore" &&
            browserEdited.Posting == "மாவட்ட தலைவர்" &&
            browserEdited.MemberId == beforeEdit.MemberId && browserEdited.CardExpiresAt == beforeEdit.CardExpiresAt &&
            browserEdited.ApprovedAt == beforeEdit.ApprovedAt && browserEdited.EditHistory.Count == afterEdit.EditHistory.Count + 3 &&
            browserEdited.EditHistory[^3].Changes[nameof(UserRegistration.Name)].NewValue == browserEdited.Name &&
            browserEdited.PermanentAddress == "Quick preview updated synthetic address",
            "browser save and crop persist audit entries without changing identity or expiry");
    }
    var beforePhoto = await users.Find(u => u.Id == newId).SingleAsync();
    using (var blankBitmap = new SKBitmap(600, 750))
    using (var blankCanvas = new SKCanvas(blankBitmap))
    {
        blankCanvas.Clear(SKColors.White);
        using var blankPicture = SKImage.FromBitmap(blankBitmap);
        using var encodedBlank = blankPicture.Encode(SKEncodedImageFormat.Png, 100);
        var blankBytes = encodedBlank.ToArray();
        using var blankStream = new MemoryStream(blankBytes);
        var blankFile = new FormFile(blankStream, 0, blankBytes.Length, "image", "blank.png") { Headers = new HeaderDictionary(), ContentType = "image/png" };
        Check((await ImageSuitability.CheckAsync(blankFile, true, default)).Error?.Contains("blank") == true,
            "blank passport image is rejected after decoding");
    }
    using (var corruptStream = new MemoryStream(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3 }))
    {
        var corruptFile = new FormFile(corruptStream, 0, corruptStream.Length, "image", "corrupt.png") { Headers = new HeaderDictionary(), ContentType = "image/png" };
        Check((await ImageSuitability.CheckAsync(corruptFile, false, default)).Error?.Contains("damaged") == true,
            "malformed document is rejected after signature check");
    }
    using (var replacement = new MultipartFormDataContent())
    {
        var bytes = await File.ReadAllBytesAsync(Path.Combine(runRoot, "wwwroot", beforePhoto.ProfilePhotoPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
        var image = new ByteArrayContent(bytes);
        var extension = Path.GetExtension(beforePhoto.ProfilePhotoPath);
        image.Headers.ContentType = new MediaTypeHeaderValue(extension is ".jpg" or ".jpeg" ? "image/jpeg" : extension == ".webp" ? "image/webp" : "image/png");
        replacement.Add(image, "photo", "replacement" + extension);
        using var request = new HttpRequestMessage(HttpMethod.Post, photoRoute) { Content = replacement };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        await Json(await http.SendAsync(request), HttpStatusCode.OK, "admin replaces synthetic passport photo");
    }
    var afterPhoto = await users.Find(u => u.Id == newId).SingleAsync();
    var cardPhoto = await Json(await GetWithToken($"/api/users/{newId}/id-card", adminToken), HttpStatusCode.OK, "card uses replacement passport photo");
    Check(afterPhoto.ProfilePhotoPath != beforePhoto.ProfilePhotoPath && cardPhoto["profilePhotoPath"]!.GetValue<string>() == afterPhoto.ProfilePhotoPath &&
        afterPhoto.MemberId == beforePhoto.MemberId && afterPhoto.CreatedAt == beforePhoto.CreatedAt && afterPhoto.CardExpiresAt == beforePhoto.CardExpiresAt &&
        afterPhoto.ApprovedAt == beforePhoto.ApprovedAt && afterPhoto.EditHistory.Count == beforePhoto.EditHistory.Count + 1,
        "photo replacement updates card and audit without changing identity, approval or validity");
    var reviewRoute = $"/api/admin/users/{legacy.Id}/document-review";
    Check((await PutEdit(reviewRoute, new { kind = "aadhaar", status = "Correction requested", correction = "Synthetic clearer document needed" }, memberToken)).StatusCode == HttpStatusCode.Forbidden,
        "member cannot set an admin document review status");
    Check((await PutEdit($"/api/admin/users/{newId}/document-review", new { kind = "aadhaar", status = "Correction requested" }, adminToken)).StatusCode == HttpStatusCode.BadRequest,
        "number-only registrations cannot be sent to Aadhaar image review");
    await Json(await PutEdit(reviewRoute, new { kind = "aadhaar", status = "Correction requested", correction = "Synthetic clearer document needed" }, adminToken),
        HttpStatusCode.OK, "admin requests historical Aadhaar image correction");
    var correctionStatus = await Json(await GetWithToken($"/api/users/{legacy.Id}/document-status", legacyToken), HttpStatusCode.OK,
        "member sees requested correction status");
    Check(correctionStatus["aadhaarReviewStatus"]!.GetValue<string>() == "Correction requested" &&
        correctionStatus["hasLegacyAadhaarImage"]!.GetValue<bool>() &&
        correctionStatus["documentCorrectionRequest"]!.GetValue<string>() == "Synthetic clearer document needed",
        "historical image correction guidance reaches only its owner");
    var numberOnlyStatus = await Json(await GetWithToken($"/api/users/{newId}/document-status", memberToken), HttpStatusCode.OK,
        "new member sees number-only document status");
    Check(!numberOnlyStatus["hasLegacyAadhaarImage"]!.GetValue<bool>(), "new registration has no Aadhaar image correction control");
    Check((await GetWithToken($"/api/users/{legacy.Id}/document-status", memberToken)).StatusCode == HttpStatusCode.Forbidden,
        "member cannot see another member's document review");
    var beforeCorrection = await users.Find(u => u.Id == legacy.Id).SingleAsync();
    using (var correction = new MultipartFormDataContent())
    {
        var image = new ByteArrayContent(uploadSnapshots.Values.First());
        image.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        correction.Add(image, "image", "synthetic-correction.png");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/users/{legacy.Id}/document-correction/aadhaar") { Content = correction };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", legacyToken);
        await Json(await http.SendAsync(request), HttpStatusCode.OK, "historical member supplies requested Aadhaar correction");
    }
    var corrected = await users.Find(u => u.Id == legacy.Id).SingleAsync();
    Check(corrected.AadhaarPhotoPath != beforeCorrection.AadhaarPhotoPath && corrected.AadhaarReviewStatus == "Needs review" &&
        corrected.MemberId == beforeCorrection.MemberId && corrected.CardExpiresAt == beforeCorrection.CardExpiresAt &&
        corrected.EditHistory.Count == beforeCorrection.EditHistory.Count + 1,
        "corrected Aadhaar is private, returns to review, and preserves member identity with audit");
    using (var forbiddenCorrection = new MultipartFormDataContent())
    {
        var image = new ByteArrayContent(uploadSnapshots.Values.First());
        image.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        forbiddenCorrection.Add(image, "image", "synthetic-correction.png");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/users/{newId}/document-correction/aadhaar") { Content = forbiddenCorrection };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", memberToken);
        Check((await http.SendAsync(request)).StatusCode == HttpStatusCode.BadRequest,
            "number-only member cannot upload an Aadhaar image through correction API");
    }
    var decisionRoute = $"/api/admin/users/{newId}/approval-decision";
    Check((await PutEdit(decisionRoute, new { decision = "Rejected" }, memberToken)).StatusCode == HttpStatusCode.Forbidden,
        "member cannot reject or approve registration");
    await Json(await PutEdit(decisionRoute, new { decision = "Rejected" }, adminToken), HttpStatusCode.OK,
        "admin rejects synthetic member");
    var rejected = await users.Find(u => u.Id == newId).SingleAsync();
    Check(rejected.ApprovalDecidedBy == "test-admin" && rejected.ApprovalDecidedAt.HasValue &&
        UserService.GetApprovalStatus(rejected) == "Rejected" && rejected.EditHistory[^1].Changes["ApprovalStatus"].NewValue == "Rejected",
        "rejection records actor, time and audit");
    foreach (var route in new[] { $"/api/users/{newId}/id-card", $"/api/idcards/{newId}" })
        Check((await GetWithToken(route, memberToken)).StatusCode == HttpStatusCode.Forbidden,
            "rejected member cannot access card");
    await Json(await PutEdit(decisionRoute, new { decision = "Correction requested", note = "Synthetic clearer document needed" }, adminToken),
        HttpStatusCode.OK, "admin requests member correction");
    var correctionDecision = await users.Find(u => u.Id == newId).SingleAsync();
    Check(correctionDecision.VerificationStatus == "Correction requested" && correctionDecision.DocumentCorrectionRequest.Length > 0 &&
        correctionDecision.ProfilePhotoReviewStatus == "Correction requested" && correctionDecision.AadhaarReviewStatus == "Number provided",
        "correction decision requests passport changes without asking number-only members for Aadhaar images");
    Check((await GetWithToken($"/api/users/{newId}/id-card", memberToken)).StatusCode == HttpStatusCode.Forbidden,
        "correction-requested member cannot access card");
    var parallelApprovals = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => PutEdit(decisionRoute, new { decision = "Approved" }, adminToken)));
    Check(parallelApprovals.Count(response => response.StatusCode == HttpStatusCode.OK) == 1 &&
        parallelApprovals.Count(response => response.StatusCode == HttpStatusCode.Conflict) == 1,
        "concurrent approvals allow exactly one decision and audit");
    Check((await PutEdit(decisionRoute, new { decision = "Approved" }, adminToken)).StatusCode == HttpStatusCode.Conflict,
        "duplicate approval cannot be recorded twice");
    Check((await GetWithToken($"/api/users/{newId}/id-card", memberToken)).StatusCode == HttpStatusCode.OK,
        "member regains card access after approval");
    var historicalDistrictId = Seed("9888888899", "WTS-ALR-0001");
    historicalDistrictId.CanonicalMobile = historicalDistrictId.PhoneNumber;
    historicalDistrictId.District = "Ariyalur";
    await users.InsertOneAsync(historicalDistrictId);
    var issuer = new UserService(context);
    var simultaneous = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => issuer.CreateAsync(new UserRegistration {
        PhoneNumber = $"98888888{i:D2}", District = "Ariyalur", Name = "Synthetic concurrent member", ConsentAccepted = true })));
    Check(simultaneous.Select(u => u.MemberId).Distinct().Count() == 8 &&
        simultaneous.All(u => u.MemberId.StartsWith("WTS-ALR-", StringComparison.Ordinal)) &&
        simultaneous.Select(u => u.MemberId).Order().SequenceEqual(Enumerable.Range(2, 8).Select(i => $"WTS-ALR-{i:D4}")) &&
        (await users.Find(u => u.Id == historicalDistrictId.Id).SingleAsync()).MemberId == "WTS-ALR-0001",
        "eight parallel registrations skip a historical ID and receive unique atomic Ariyalur numbers");
    using (var invalidApi = new IsolatedApi(runRoot,
        new Dictionary<string, string?>(settings) { ["Authentication:OtpEnabled"] = "false" }, captured))
    using (var invalidHttp = invalidApi.CreateClient())
    {
        foreach (var invalidNumber in new string?[] { null, "", "2345678901", "2345678901245", "23456789012A", "234567890125" })
        {
            using var result = await invalidHttp.PostAsync("/api/users/register", Registration("9876543298", aadhaarNumber: invalidNumber));
            Check(result.StatusCode == HttpStatusCode.BadRequest, "missing, malformed or checksum-invalid Aadhaar number rejected by API");
        }
        Check(!await users.Find(u => u.CanonicalMobile == "9876543298").AnyAsync(),
            "invalid Aadhaar numbers create no member or ID");
    }
    Console.WriteLine($"ALL {passed} CHECKS PASSED. Isolated database: {dbName}");
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    Environment.ExitCode = 1;
}
finally
{
    if (bridge is not null) await bridge.DisposeAsync();
    if (api is not null) await api.DisposeAsync();
    await File.WriteAllLinesAsync(Path.Combine(runRoot, "api.log"), log);
    if (retainDatabase)
        Console.WriteLine("Retained the isolated regression database for review; no database or users were deleted.");
    else
    {
        if (!dbName.StartsWith("WTS_SecurityTests_", StringComparison.Ordinal) || dbName.Length != "WTS_SecurityTests_".Length + 32)
            throw new InvalidOperationException("Refusing unsafe test database cleanup");
        await mongo.DropDatabaseAsync(dbName);
        Console.WriteLine("Removed only the isolated test database; existing application data was not modified.");
    }
}

sealed class DisabledSms : ISmsSender
{
    public Task<bool> SendOtpAsync(string phoneNumber, string otp, CancellationToken cancellationToken = default) => Task.FromResult(false);
}
sealed class FailingPassportUpload : IPrivateUploadStorage
{
    public int SaveCount;
    public List<string> Deleted = [];
    public Task<string> SaveAsync(IFormFile file, string prefix, CancellationToken cancellationToken = default)
    {
        SaveCount++;
        throw new IOException("Synthetic upload failure");
    }
    public Task<PrivateUpload?> ReadAsync(string filename, CancellationToken cancellationToken = default) =>
        Task.FromResult<PrivateUpload?>(null);
    public Task DeleteAsync(string reference, CancellationToken cancellationToken = default)
    {
        Deleted.Add(reference);
        return Task.CompletedTask;
    }
}
sealed class TestClock : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan time) => _now += time;
}
