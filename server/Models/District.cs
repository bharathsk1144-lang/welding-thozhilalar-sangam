using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace WeldingThozhilalarSangam.Api.Models;

public sealed class District
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    public string Name { get; set; } = string.Empty;
    public string WhatsAppGroupLink { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
