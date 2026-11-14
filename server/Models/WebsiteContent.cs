using MongoDB.Bson.Serialization.Attributes;

namespace WeldingThozhilalarSangam.Api.Models;

public sealed class WebsiteContent
{
    [BsonId] public string Id { get; set; } = "website";
    public string AboutMarkdown { get; set; } = string.Empty;
    public string AboutContact { get; set; } = string.Empty;
    public List<WebsiteItem> Gallery { get; set; } = [];
    public List<WebsiteItem> Products { get; set; } = [];
    public int Revision { get; set; }
    public List<WebsiteContentChange> History { get; set; } = [];
}

public sealed class WebsiteItem
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ImagePath { get; set; } = string.Empty;
    public int Order { get; set; }
}

public sealed class WebsiteContentChange
{
    public string Editor { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string Section { get; set; } = string.Empty;
    public int Revision { get; set; }
}
