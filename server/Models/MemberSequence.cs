using MongoDB.Bson.Serialization.Attributes;

namespace WeldingThozhilalarSangam.Api.Models;

public sealed class MemberSequence
{
    [BsonId]
    public string DistrictCode { get; set; } = string.Empty;
    public long Value { get; set; }
}
