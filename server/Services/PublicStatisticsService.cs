using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using WeldingThozhilalarSangam.Api.Data;
using WeldingThozhilalarSangam.Api.Models;

namespace WeldingThozhilalarSangam.Api.Services;

public sealed record PublicStatistics(long TotalRegisteredMembers, long ConfiguredDistricts);

public interface IPublicStatisticsService
{
    Task<PublicStatistics> GetAsync(CancellationToken cancellationToken);
}

public sealed class PublicStatisticsService(MongoContext context, IOptions<PublicStatisticsSettings> options)
    : IPublicStatisticsService
{
    public static FilterDefinition<UserRegistration> MemberFilter(PublicStatisticsSettings settings)
    {
        if (!settings.IsValid()) throw new InvalidOperationException("Invalid public statistics account classification.");
        if (!settings.ClassificationReviewed) throw new InvalidOperationException("Public statistics require reviewed test-account classification.");
        var filter = Builders<UserRegistration>.Filter;
        var eligible = filter.Eq(u => u.Status, "Active Member") &
            filter.Eq(u => u.VerificationStatus, "Verified") &
            filter.Regex(u => u.MemberId, new BsonRegularExpression(@"\S")) &
            filter.Regex(u => u.CanonicalMobile, new BsonRegularExpression("^[6-9][0-9]{9}$"));
        // Explicit operator-classified test IDs are excluded; no identity heuristics.
        return eligible & filter.Nin(u => u.Id, settings.TestUserIds);
    }

    public async Task<PublicStatistics> GetAsync(CancellationToken cancellationToken)
    {
        var countOptions = new CountOptions { MaxTime = TimeSpan.FromSeconds(5) };
        var members = context.Users.CountDocumentsAsync(MemberFilter(options.Value), countOptions, cancellationToken);
        var districts = context.Districts.CountDocumentsAsync(Builders<District>.Filter.Empty, countOptions, cancellationToken);
        await Task.WhenAll(members, districts);
        return new PublicStatistics(await members, await districts);
    }
}
