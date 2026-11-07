using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WeldingThozhilalarSangam.Api.DTOs;
using WeldingThozhilalarSangam.Api.Services;

namespace WeldingThozhilalarSangam.Api.Controllers;

// Preserve legacy routes while sharing the same duplicate checks and verification rules.
[ApiController]
[Route("api/otp")]
[EnableRateLimiting("auth")]
public sealed class OtpController(IWebHostEnvironment environment, OtpService otpService, UserService userService, IConfiguration configuration) : ControllerBase
{
    [HttpPost("send")]
    public Task<IActionResult> SendOtp(SendOtpRequest? request, CancellationToken cancellationToken) =>
        new AuthController(environment, otpService, userService, configuration).SendOtp(request, cancellationToken);

    [HttpPost("verify")]
    public Task<IActionResult> VerifyOtp(VerifyOtpRequest? request) =>
        new AuthController(environment, otpService, userService, configuration).VerifyOtp(request);
}
