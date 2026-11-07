using MongoDB.Bson;

namespace WeldingThozhilalarSangam.Api.Models;

// Account classification belongs to the operator, never to registration request data.
// No persisted user fields or existing records are changed by these settings.
public sealed class PublicStatisticsSettings
{
    public bool ClassificationReviewed { get; set; }
    public string[] TestUserIds { get; set; } = [];

    public bool IsValid() => TestUserIds is not null && TestUserIds.Length <= 10000 &&
        TestUserIds.All(id => id is not null && id.Length == 24 && ObjectId.TryParse(id, out _));
}
