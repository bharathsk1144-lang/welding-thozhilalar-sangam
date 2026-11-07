using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WeldingThozhilalarSangam.Api.DTOs;
using WeldingThozhilalarSangam.Api.Services;

namespace WeldingThozhilalarSangam.Api.Controllers;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")]
public sealed class AuthController(IWebHostEnvironment environment, OtpService otpService, UserService userService, IConfiguration configuration) : ControllerBase
{
    [HttpGet("registration-options")]
    public IActionResult RegistrationOptions()
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(new { otpEnabled = configuration.GetValue("Authentication:OtpEnabled", true) });
    }

    [HttpPost("send-otp")]
    public async Task<IActionResult> SendOtp(SendOtpRequest? request, CancellationToken cancellationToken)
    {
        if (!configuration.GetValue("Authentication:OtpEnabled", true))
            return Conflict(new { message = "Registration OTP is disabled." });
        if (!MobileNumber.TryNormalize(request?.PhoneNumber, out var phone))
            return BadRequest(new { message = MobileNumber.InvalidMessage });
        if (request!.Name is not null && (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 100))
            return BadRequest(new { message = "Name must contain between 1 and 100 characters." });
        if (await userService.PhoneNumberExistsAsync(phone))
            return Conflict(new { code = "mobile_already_registered", message = MobileNumber.DuplicateMessage });
        var result = await otpService.SendOtpAsync(phone, environment.IsDevelopment(), cancellationToken, registrationName: request.Name);
        return Ok(new { message = "OTP sent successfully.", developmentOtp = result.DevelopmentOtp });
    }

    [HttpPost("verify-otp")]
    public async Task<IActionResult> VerifyOtp(VerifyOtpRequest? request)
    {
        if (request?.Purpose == OtpService.Registration && !configuration.GetValue("Authentication:OtpEnabled", true))
            return Conflict(new { message = "Registration OTP is disabled." });
        if (request is null || string.IsNullOrWhiteSpace(request.Otp) ||
            !MobileNumber.TryNormalize(request.PhoneNumber, out var phone))
            return BadRequest(new { message = "Valid phone number and OTP are required." });
        if (request.Purpose == OtpService.Registration && await userService.PhoneNumberExistsAsync(phone))
            return Conflict(new { code = "mobile_already_registered", message = MobileNumber.DuplicateMessage });
        if (request.Purpose == OtpService.PasswordReset &&
            (await userService.GetByPhoneNumberAsync(phone))?.MobileVerified == false)
            return BadRequest(new { message = "Invalid or expired OTP." });
        var token = otpService.VerifyOtp(phone, request.Otp, request.Purpose);
        return token is null ? BadRequest(new { message = "Invalid or expired OTP." }) : Ok(new VerifyOtpResponse(token));
    }
}
