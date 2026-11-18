using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace WeldingThozhilalarSangam.Api.Models;

public sealed class AadhaarRevealAudit
{
    [BsonId]
    public ObjectId Id { get; set; } = ObjectId.GenerateNewId();
    public string MemberRecordId { get; set; } = string.Empty;
    public string AdminName { get; set; } = string.Empty;
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string Result { get; set; } = string.Empty;
}
