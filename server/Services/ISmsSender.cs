namespace WeldingThozhilalarSangam.Api.Services;

public interface ISmsSender
{
    Task<bool> SendOtpAsync(string phoneNumber, string otp, CancellationToken cancellationToken = default);
}
