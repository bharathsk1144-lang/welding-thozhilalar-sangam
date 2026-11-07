namespace WeldingThozhilalarSangam.Api.Models;

public sealed class SmsSettings
{
    public string Provider { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string SenderId { get; set; } = string.Empty;
    public string TemplateId { get; set; } = string.Empty;
    public bool Enabled { get; set; }

    public bool HasCredentials =>
        !string.IsNullOrWhiteSpace(Provider) &&
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(SenderId) &&
        !string.IsNullOrWhiteSpace(TemplateId);
}
