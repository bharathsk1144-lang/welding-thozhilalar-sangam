using MongoDB.Driver;
using MongoDB.Bson;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;
using WeldingThozhilalarSangam.Api.DTOs;
using WeldingThozhilalarSangam.Api.Data;
using WeldingThozhilalarSangam.Api.Models;

namespace WeldingThozhilalarSangam.Api.Services;

public sealed class UserService
{
    private readonly MongoContext _context;
    private readonly MembershipSettings _membership;

    public UserService(MongoContext context, IOptions<MembershipSettings>? membership = null)
    {
        _context = context;
        _membership = membership?.Value ?? new MembershipSettings();
    }

    public async Task<UserRegistration> CreateAsync(UserRegistration user)
    {
        user.PhoneNumber = MobileNumber.Normalize(user.PhoneNumber);
        user.CanonicalMobile = user.PhoneNumber;
        if (await PhoneNumberExistsAsync(user.PhoneNumber)) throw new DuplicateMobileException();
        user.District = user.District.Trim();
        user.CreatedAt = DateTime.UtcNow;
        user.MemberId = MemberIdFormat.Generate(user);
        user.PaymentStatus = string.IsNullOrWhiteSpace(user.PaymentStatus) ? "Not Required" : user.PaymentStatus;
        user.Status = string.IsNullOrWhiteSpace(user.Status) ? "Active Member" : user.Status;
        user.VerificationStatus = string.IsNullOrWhiteSpace(user.VerificationStatus) ? "Verified" : user.VerificationStatus;
        user.VerifiedAt ??= DateTime.UtcNow;
        user.VerifiedBy = string.IsNullOrWhiteSpace(user.VerifiedBy) ? "System" : user.VerifiedBy;
        user.ConsentAcceptedAt = user.ConsentAccepted ? DateTime.UtcNow : user.ConsentAcceptedAt;
        try
        {
            await _context.Users.InsertOneAsync(user);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            throw new DuplicateMobileException();
        }
        return user;
    }

    public async Task<IReadOnlyList<UserListItemDto>> GetUsersAsync(string? district)
    {
        var filter = string.IsNullOrWhiteSpace(district)
            ? Builders<UserRegistration>.Filter.Empty
            : Builders<UserRegistration>.Filter.Eq(u => u.District, district.Trim());

        var users = await _context.Users.Find(filter).SortByDescending(u => u.CreatedAt).ToListAsync();

        var views = users.Select(u => new UserListItemDto(
            u.Id ?? string.Empty,
            u.Name,
            u.Username,
            u.PhoneNumber,
            u.District,
            u.MemberId,
            u.CreatedAt,
            GetIdCardStatus(u))).ToList();
        return await PresentManyAsync(views);
    }

    public async Task<UserRegistration?> GetByIdAsync(string id)
    {
        return await _context.Users.Find(u => u.Id == id).FirstOrDefaultAsync();
    }

    public async Task<UserRegistration?> GetByPhoneNumberAsync(string phoneNumber)
    {
        var matches = await _context.Users.Find(PhoneFilter(phoneNumber)).Limit(2).ToListAsync();
        if (matches.Count > 1) throw new AmbiguousMobileException();
        return matches.SingleOrDefault();
    }

    public async Task<UserRegistration?> GetByMemberIdAsync(string memberId)
    {
        var normalizedMemberId = memberId.Trim();
        var exact = await _context.Users.Find(u => u.MemberId == normalizedMemberId).Limit(2).ToListAsync();
        if (exact.Count > 1) throw new AmbiguousMemberIdException();
        if (exact.Count == 1) return exact[0]; // Old printed IDs always take precedence.
        if (!MemberIdFormat.TryParseDisplay(normalizedMemberId, _membership.OrganizationRegistrationNumber, out var suffix)) return null;
        var identities = await _context.Users.Find(SuffixFilter(suffix)).Project(u => new { u.Id, u.MemberId }).ToListAsync();
        var candidates = identities.Where(candidate => MemberIdFormat.TryGetSuffix(candidate.MemberId, out _) &&
            MemberIdFormat.Display(candidate.MemberId, _membership.OrganizationRegistrationNumber) == normalizedMemberId).Take(2).ToArray();
        if (candidates.Length > 1) throw new AmbiguousMemberIdException();
        return candidates.SingleOrDefault() is { } identity ? await GetByIdAsync(identity.Id!) : null;
    }

    public async Task<T> PresentAsync<T>(T view) where T : IMemberIdView
    {
        await PresentManyAsync(new[] { view });
        return view;
    }

    public async Task<IReadOnlyList<T>> PresentManyAsync<T>(IReadOnlyList<T> views) where T : IMemberIdView
    {
        var number = _membership.OrganizationRegistrationNumber;
        foreach (var view in views) { view.DisplayMemberId = view.MemberId; view.MemberIdDisplayWarning = null; }
        if (string.IsNullOrWhiteSpace(number)) return views;
        var suffixes = views.Select(v => MemberIdFormat.TryGetSuffix(v.MemberId, out var suffix) ? suffix : null)
            .OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        if (suffixes.Length > 0)
        {
            // Global identity projection also catches collisions across district-filtered lists.
            // Never load passwords/documents for display formatting or persist the derived value.
            var filter = Builders<UserRegistration>.Filter.Or(suffixes.Select(SuffixFilter));
            var ids = await _context.Users.Find(filter).Project(u => u.MemberId).ToListAsync();
            foreach (var id in ids)
                if (MemberIdFormat.TryGetSuffix(id, out var suffix)) counts[suffix] = counts.GetValueOrDefault(suffix) + 1;
        }
        foreach (var view in views)
        {
            if (string.IsNullOrWhiteSpace(view.MemberId)) continue;
            if (!MemberIdFormat.TryGetSuffix(view.MemberId, out var suffix))
                view.MemberIdDisplayWarning = "Stored Member ID needs review before an official display ID can be derived.";
            else if (counts.GetValueOrDefault(suffix) != 1)
                view.MemberIdDisplayWarning = "Display ID is ambiguous. The original Member ID is retained; contact the Sangam for review.";
            else view.DisplayMemberId = MemberIdFormat.Display(view.MemberId, number);
        }
        return views;
    }

    private static FilterDefinition<UserRegistration> SuffixFilter(string suffix) =>
        Builders<UserRegistration>.Filter.Regex(u => u.MemberId, new BsonRegularExpression($"^WTS-.+-{Regex.Escape(suffix)}$"));

    public async Task<bool> UpdatePasswordHashAsync(string userId, string passwordHash)
    {
        var result = await _context.Users.UpdateOneAsync(
            u => u.Id == userId,
            Builders<UserRegistration>.Update.Set(u => u.PasswordHash, passwordHash));

        return result.MatchedCount > 0;
    }

    public async Task<IReadOnlyList<PaymentPendingDto>> GetPendingPaymentsAsync()
    {
        var filter = Builders<UserRegistration>.Filter.Eq(u => u.PaymentStatus, "Pending") &
            Builders<UserRegistration>.Filter.Ne(u => u.PaymentSubmittedAt, null);

        var users = await _context.Users.Find(filter).SortByDescending(u => u.PaymentSubmittedAt).ToListAsync();

        return users.Select(u => new PaymentPendingDto(
            u.Id ?? string.Empty,
            u.Name,
            u.PhoneNumber,
            u.District,
            u.CreatedAt,
            u.PaymentStatus,
            u.PaymentTransactionId,
            u.PaymentProofUrl,
            u.PaymentSubmittedAt)).ToList();
    }

    public async Task<long> CountPendingPaymentsAsync()
    {
        var filter = Builders<UserRegistration>.Filter.Eq(u => u.PaymentStatus, "Pending") &
            Builders<UserRegistration>.Filter.Ne(u => u.PaymentSubmittedAt, null);

        return await _context.Users.CountDocumentsAsync(filter);
    }

    public async Task<bool> SubmitPaymentAsync(string userId, string transactionId, string proofUrl)
    {
        var update = Builders<UserRegistration>.Update
            .Set(u => u.PaymentStatus, "Pending")
            .Set(u => u.PaymentTransactionId, transactionId.Trim())
            .Set(u => u.PaymentProofUrl, proofUrl)
            .Set(u => u.PaymentSubmittedAt, DateTime.UtcNow)
            .Unset(u => u.PaymentVerifiedAt)
            .Set(u => u.PaymentVerifiedBy, string.Empty);

        var result = await _context.Users.UpdateOneAsync(u => u.Id == userId, update);
        return result.MatchedCount > 0;
    }

    public async Task<UserRegistration?> VerifyPaymentAsync(string userId, string verifiedBy)
    {
        var user = await GetByIdAsync(userId);
        if (user is null)
        {
            return null;
        }

        var update = Builders<UserRegistration>.Update
            .Set(u => u.PaymentStatus, "Verified")
            .Set(u => u.PaymentVerifiedAt, DateTime.UtcNow)
            .Set(u => u.PaymentVerifiedBy, verifiedBy);

        await _context.Users.UpdateOneAsync(u => u.Id == userId, update);
        return await GetByIdAsync(userId);
    }

    public async Task<UserRegistration?> RejectPaymentAsync(string userId, string verifiedBy)
    {
        var user = await GetByIdAsync(userId);
        if (user is null)
        {
            return null;
        }

        var update = Builders<UserRegistration>.Update
            .Set(u => u.PaymentStatus, "Rejected")
            .Set(u => u.PaymentVerifiedAt, DateTime.UtcNow)
            .Set(u => u.PaymentVerifiedBy, verifiedBy);

        await _context.Users.UpdateOneAsync(u => u.Id == userId, update);
        return await GetByIdAsync(userId);
    }

    public async Task<UserRegistration?> GetIdCardUserAsync(string userId)
    {
        return await GetByIdAsync(userId);
    }

    public async Task<bool> PhoneNumberExistsAsync(string phoneNumber)
    {
        return await _context.Users.Find(PhoneFilter(phoneNumber)).AnyAsync();
    }

    private static FilterDefinition<UserRegistration> PhoneFilter(string phoneNumber)
    {
        var mobile = MobileNumber.Normalize(phoneNumber);
        // Preserve reads of legacy records until the additive canonical-field migration is applied.
        return Builders<UserRegistration>.Filter.Eq(u => u.CanonicalMobile, mobile) |
            Builders<UserRegistration>.Filter.Regex(u => u.PhoneNumber,
                new BsonRegularExpression($@"^\s*(?:\+?91)?{mobile}\s*$"));
    }

    public async Task<long> CountAsync()
    {
        return await _context.Users.CountDocumentsAsync(_ => true);
    }

    public async Task<IReadOnlyList<DistrictCountDto>> CountByDistrictAsync()
    {
        var districts = await _context.Users
            .Find(Builders<UserRegistration>.Filter.Empty)
            .Project(user => user.District)
            .ToListAsync();

        return districts
            .GroupBy(district => district)
            .Select(group => new DistrictCountDto(group.Key, group.LongCount()))
            .OrderByDescending(item => item.Count)
            .ToList();
    }

    public static string GetIdCardStatus(UserRegistration user)
    {
        return string.IsNullOrWhiteSpace(user.MemberId) ? "Pending" : "Ready";
    }
}
