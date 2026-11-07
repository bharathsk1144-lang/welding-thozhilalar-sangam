namespace WeldingThozhilalarSangam.Api.DTOs;

public sealed class SendOtpRequest
{
    public string? Name { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
}

public sealed record VerifyOtpRequest(string PhoneNumber, string Otp, string Purpose = "registration");
public sealed record VerifyOtpResponse(string VerificationToken);
