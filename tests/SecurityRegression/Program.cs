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
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
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
    MultipartFormDataContent Registration(string phone, string token = "", string name = "New member", string password = "NewMember@123", bool consent = true)
    {
        var form = new MultipartFormDataContent();
        foreach (var field in new Dictionary<string, string> { ["phoneNumber"] = phone, ["verificationToken"] = token,
            ["name"] = name, ["password"] = password, ["location"] = "Test", ["dateOfBirth"] = "1990-01-01",
            ["bloodGroup"] = "O+", ["fatherName"] = "Test father", ["permanentAddress"] = "Isolated test address",
            ["pincode"] = "600001", ["district"] = "Chennai", ["hasJoinedWhatsAppGroup"] = "true", ["consentAccepted"] = consent ? "true" : "false" })
            form.Add(new StringContent(field.Value), field.Key);
        foreach (var field in new[] { "aadhaarPhoto", "profilePhoto" })
        {
            var file = new ByteArrayContent(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aD1sAAAAASUVORK5CYII="));
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
    var newId = registered["userId"]!.GetValue<string>();
    var afterRegistrationStatistics = await Json(await http.GetAsync("/api/statistics"), HttpStatusCode.OK, "statistics refreshed after registration");
    Check(afterRegistrationStatistics["totalRegisteredMembers"]!.GetValue<long>() == beforeRegistrationStatistics.TotalRegisteredMembers + 1,
        "pending registration still counts as a registered member without claiming verification");
    var memberToken = registered["token"]!.GetValue<string>();
    var registeredBefore = (await users.Find(u => u.Id == newId).SingleAsync()).ToJson();
    var newAccount = await users.Find(u => u.Id == newId).SingleAsync();
    Check(newAccount.MobileVerified == true, "OTP registration stores verified mobile ownership");
    Check(System.Text.RegularExpressions.Regex.IsMatch(newAccount.MemberId, @"^WTS-CHN-[0-9]{4,}$") && newAccount.IdSchemeVersion == 2,
        "new registration stores a district-scoped, immutable server ID");
    Check(newAccount.ConsentAcceptedAt.HasValue && newAccount.ConsentPolicyVersion == "2026-11-13" && !newAccount.ApprovedAt.HasValue,
        "new registration records policy version and timestamp without automatic approval");
    var displayId = registered["displayMemberId"]!.GetValue<string>();
    Check(displayId == newAccount.MemberId, "new district member ID is displayed without an official-number alias");
    var publicVerification = await Json(await http.GetAsync($"/api/users/verify/{Uri.EscapeDataString(displayId)}"), HttpStatusCode.OK,
        "new ID public verification supports encoded official separators");
    Check(publicVerification["memberId"]!.GetValue<string>() == newAccount.MemberId && !publicVerification.ContainsKey("phoneNumber"),
        "verification preserves the ID without exposing private card fields");
    await Json(await http.GetAsync($"/api/users/verify/{legacy.MemberId}"), HttpStatusCode.OK, "existing year-based verification remains compatible");
    var uploadSnapshots = new[] { newAccount.AadhaarPhotoPath, newAccount.ProfilePhotoPath }.ToDictionary(
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
    Check(uploadSnapshots.All(p => File.ReadAllBytes(p.Key).SequenceEqual(p.Value)), "TC17: Aadhaar and passport file bytes remain intact");

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
    var card = await Json(await GetWithToken($"/api/users/{newId}/id-card", memberToken), HttpStatusCode.OK, "TC12: registered session loads ID card");
    Check(card["memberId"]!.GetValue<string>() == registered["memberId"]!.GetValue<string>() && card["displayMemberId"]!.GetValue<string>() == displayId &&
        !card.ContainsKey("aadhaarPhotoPath") && !card.ContainsKey("passwordHash"), "private card fields are safe");
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
    foreach (var officer in new[] { "president", "secretary", "treasurer" })
    {
        var route = $"/api/users/{newId}/officer-signatures/{officer}";
        Check((await GetWithToken(route, null)).StatusCode == HttpStatusCode.Unauthorized,
            $"anonymous {officer} signature denied");
        Check((await GetWithToken(route, login["token"]!.GetValue<string>())).StatusCode == HttpStatusCode.Forbidden,
            $"cross-member {officer} signature denied");
        var signed = await GetWithToken(route, memberToken);
        Check(signed.StatusCode == HttpStatusCode.OK && signed.Headers.CacheControl?.NoStore == true &&
            (await signed.Content.ReadAsByteArrayAsync()).SequenceEqual(
                File.ReadAllBytes(Path.Combine(signatureSource, $"state-{officer}-signature-blue.png"))),
            $"owner {officer} signature is exact and no-store");
        Check((await GetWithToken(route, adminToken)).StatusCode == HttpStatusCode.OK,
            $"admin {officer} signature access");
        Check((await GetWithToken($"/Assets/OfficerSignatures/state-{officer}-signature.png", null)).StatusCode == HttpStatusCode.NotFound,
            $"raw {officer} signature is not a static asset");
        Check((await GetWithToken($"/Assets/OfficerSignatures/state-{officer}-signature-blue.png", null)).StatusCode == HttpStatusCode.NotFound,
            $"blue {officer} signature is not a static asset");
    }
    var pendingCard = await Json(await GetWithToken($"/api/users/{newId}/id-card", memberToken), HttpStatusCode.OK, "pending card preview");
    Check(pendingCard["isApproved"]!.GetValue<bool>() == false && pendingCard["verificationStatus"]!.GetValue<string>() == "Pending",
        "registration cannot display VERIFIED badge before admin approval");
    Check((await http.PutAsJsonAsync($"/api/admin/users/{newId}/approval", new { approved = true })).StatusCode == HttpStatusCode.Unauthorized,
        "anonymous approval is denied");
    using (var approval = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/users/{newId}/approval"))
    {
        approval.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        approval.Content = JsonContent.Create(new { approved = true });
        await Json(await http.SendAsync(approval), HttpStatusCode.OK, "admin approves member");
    }
    var approvedCard = await Json(await GetWithToken($"/api/users/{newId}/id-card", memberToken), HttpStatusCode.OK, "approved card preview");
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
    Check((await GetWithToken(newAccount.AadhaarPhotoPath, null)).StatusCode == HttpStatusCode.Unauthorized &&
        (await GetWithToken(newAccount.ProfilePhotoPath, null)).StatusCode == HttpStatusCode.Unauthorized, "uploaded documents are never public static files");
    Check((await GetWithToken(newAccount.AadhaarPhotoPath, memberToken)).StatusCode == HttpStatusCode.Forbidden,
        "member cannot fetch private Aadhaar even with a valid session");
    Check((await GetWithToken(newAccount.ProfilePhotoPath, login["token"]!.GetValue<string>())).StatusCode == HttpStatusCode.Forbidden,
        "another member cannot fetch passport image");
    var ownerPhoto = await GetWithToken(newAccount.ProfilePhotoPath, memberToken);
    Check(ownerPhoto.IsSuccessStatusCode && ownerPhoto.Headers.CacheControl?.NoStore == true &&
        (await ownerPhoto.Content.ReadAsByteArrayAsync()).SequenceEqual(uploadSnapshots.Values.Last()), "owner passport fetch is authenticated, intact and no-store");
    Check((await GetWithToken(newAccount.AadhaarPhotoPath, adminToken)).IsSuccessStatusCode &&
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
    await Post("/api/users/login", new { phone = "9876543210", password = "ResetSecure@123" }, HttpStatusCode.OK, "reset password authenticates");

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
    var failedUploads = new FailingSecondUpload();
    using (var failingApi = new IsolatedApi(runRoot,
        new Dictionary<string, string?>(settings) { ["Authentication:OtpEnabled"] = "false" }, captured, failedUploads))
    using (var failingHttp = failingApi.CreateClient())
    {
        var failedRegistration = await failingHttp.PostAsync("/api/users/register", Registration("9876543288"));
        Check(failedRegistration.StatusCode == HttpStatusCode.InternalServerError,
            "failed second private upload blocks registration");
        Check(failedUploads.SaveCount == 2 && failedUploads.Deleted.Count == 1 &&
            failedUploads.Deleted[0] == "/uploads/synthetic-first.png",
            "failed registration cleans up the first private upload");
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
        Location = "New posting", DateOfBirth = new DateTime(1991, 2, 3, 0, 0, 0, DateTimeKind.Utc),
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
    var beforeEdit = await users.Find(u => u.Id == newId).SingleAsync();
    await Json(await PutEdit(editRoute, edit, adminToken), HttpStatusCode.OK, "admin saves corrected member details");
    var afterEdit = await users.Find(u => u.Id == newId).SingleAsync();
    Check(afterEdit.Name == edit.Name && afterEdit.District == edit.District && afterEdit.PhoneNumber == edit.PhoneNumber &&
        afterEdit.CanonicalMobile == edit.PhoneNumber && afterEdit.Location == edit.Location &&
        afterEdit.MemberId == beforeEdit.MemberId && afterEdit.IdSchemeVersion == beforeEdit.IdSchemeVersion &&
        afterEdit.CreatedAt == beforeEdit.CreatedAt && afterEdit.CardExpiresAt == beforeEdit.CardExpiresAt &&
        afterEdit.ApprovedAt == beforeEdit.ApprovedAt && afterEdit.PasswordHash == beforeEdit.PasswordHash &&
        afterEdit.AadhaarPhotoPath == beforeEdit.AadhaarPhotoPath && afterEdit.ProfilePhotoPath == beforeEdit.ProfilePhotoPath &&
        afterEdit.VerificationStatus == beforeEdit.VerificationStatus && afterEdit.ConsentAcceptedAt == beforeEdit.ConsentAcceptedAt &&
        afterEdit.ConsentPolicyVersion == beforeEdit.ConsentPolicyVersion,
        "edit updates allowed fields and preserves identity, expiry, approval, consent and documents");
    Check(afterEdit.EditHistory.Count == 1 && afterEdit.EditHistory[0].AdminId == "test-admin" &&
        afterEdit.EditHistory[0].MemberId == beforeEdit.MemberId &&
        afterEdit.EditHistory[0].Changes[nameof(UserRegistration.Name)].NewValue == edit.Name &&
        afterEdit.EditHistory[0].Timestamp > beforeEdit.CreatedAt,
        "atomic audit records admin, member, changes and timestamp");
    var detailAfterEdit = await Json(await GetWithToken($"/api/users/{newId}", adminToken), HttpStatusCode.OK, "admin refreshed member view");
    var cardAfterEdit = await Json(await GetWithToken($"/api/users/{newId}/id-card", adminToken), HttpStatusCode.OK, "admin refreshed card preview");
    Check(detailAfterEdit["name"]!.GetValue<string>() == edit.Name && cardAfterEdit["name"]!.GetValue<string>() == edit.Name &&
        cardAfterEdit["district"]!.GetValue<string>() == edit.District &&
        cardAfterEdit["memberId"]!.GetValue<string>() == beforeEdit.MemberId &&
        cardAfterEdit["expiresAt"]!.GetValue<DateTime>() == beforeEdit.CardExpiresAt &&
        cardAfterEdit["isApproved"]!.GetValue<bool>() == beforeEdit.ApprovedAt.HasValue,
        "view and card export source reflect edits without changing ID, expiry or approval");
    Check((await userService.GetUsersAsync("Madurai")).Any(u => u.Id == newId && u.Name == edit.Name),
        "admin user list reflects corrected district and name");
    if (args.Contains("--admin-edit-browser"))
    {
        var fixturePath = Path.Combine(runRoot, "admin-edit-browser-fixture.json");
        await File.WriteAllTextAsync(fixturePath, System.Text.Json.JsonSerializer.Serialize(new {
            root, runRoot, apiUrl = http.BaseAddress!.ToString().TrimEnd('/'),
            adminToken, memberToken, userId = newId, otherId = legacy.Id
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
        var browserEdited = await users.Find(u => u.Id == newId).SingleAsync();
        Check(browserEdited.Name == "Browser Edited Synthetic Member" && browserEdited.District == "Coimbatore" &&
            browserEdited.MemberId == beforeEdit.MemberId && browserEdited.CardExpiresAt == beforeEdit.CardExpiresAt &&
            browserEdited.ApprovedAt == beforeEdit.ApprovedAt && browserEdited.EditHistory.Count == 2 &&
            browserEdited.EditHistory[1].Changes[nameof(UserRegistration.Name)].NewValue == browserEdited.Name,
            "browser save persists edited details and second audit entry without changing identity or expiry");
    }
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
sealed class FailingSecondUpload : IPrivateUploadStorage
{
    public int SaveCount;
    public List<string> Deleted = [];
    public Task<string> SaveAsync(IFormFile file, string prefix, CancellationToken cancellationToken = default)
    {
        SaveCount++;
        if (SaveCount == 2) throw new IOException("Synthetic upload failure");
        return Task.FromResult("/uploads/synthetic-first.png");
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
