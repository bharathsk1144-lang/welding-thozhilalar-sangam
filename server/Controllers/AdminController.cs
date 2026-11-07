using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Globalization;
using System.Text;
using WeldingThozhilalarSangam.Api.DTOs;
using WeldingThozhilalarSangam.Api.Services;

namespace WeldingThozhilalarSangam.Api.Controllers;

[ApiController]
[Route("api/admin")]
public sealed class AdminController : ControllerBase
{
    private readonly AdminAuthService _adminAuthService;
    private readonly UserService _userService;

    public AdminController(AdminAuthService adminAuthService, UserService userService)
    {
        _adminAuthService = adminAuthService;
        _userService = userService;
    }

    [HttpPost("login")]
    [EnableRateLimiting("admin-login")]
    public IActionResult Login(AdminLoginRequest? request)
    {
        if (request is null)
        {
            return BadRequest(new { message = "Request body is required." });
        }

        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { message = "Username and password are required." });
        }

        var login = _adminAuthService.Login(request.Username, request.Password);
        if (login is null)
        {
            return Unauthorized(new { message = "Invalid admin credentials." });
        }

        return Ok(new AdminLoginResponse(login.Value.Token, login.Value.ExpiresAt));
    }

    [Authorize]
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard()
    {
        var totalUsers = await _userService.CountAsync();
        var paymentPendingUsers = await _userService.CountPendingPaymentsAsync();
        var districtCounts = await _userService.CountByDistrictAsync();

        return Ok(new DashboardDto(totalUsers, paymentPendingUsers, districtCounts));
    }

    [Authorize]
    [HttpGet("users")]
    public async Task<IActionResult> Users([FromQuery] string? district)
    {
        return Ok(await _userService.GetUsersAsync(district));
    }

    [Authorize]
    [HttpGet("users/{id}")]
    public async Task<IActionResult> UserDetails(string id)
    {
        var user = await _userService.GetByIdAsync(id);
        if (user is null)
        {
            return NotFound(new { message = "User not found." });
        }

        return Ok(await _userService.PresentAsync(new UserDetailsDto(
            user.Id ?? string.Empty,
            user.Username,
            user.PhoneNumber,
            user.Location,
            user.Name,
            user.DateOfBirth,
            user.BloodGroup,
            user.FatherName,
            user.PermanentAddress,
            user.Pincode,
            user.District,
            user.AadhaarPhotoPath,
            user.ProfilePhotoPath,
            user.CreatedAt,
            user.PaymentStatus,
            user.PaymentTransactionId,
            user.PaymentProofUrl,
            user.PaymentSubmittedAt,
            user.PaymentVerifiedAt,
            user.PaymentVerifiedBy,
            user.MemberId,
            UserService.GetIdCardStatus(user),
            string.IsNullOrWhiteSpace(user.Status) ? "Active Member" : user.Status,
            string.IsNullOrWhiteSpace(user.VerificationStatus) ? "Verified" : user.VerificationStatus,
            user.VerifiedAt,
            string.IsNullOrWhiteSpace(user.VerifiedBy) ? "System" : user.VerifiedBy,
            user.ConsentAccepted,
            user.ConsentAcceptedAt)));
    }

    [Authorize]
    [HttpGet("users/export")]
    public async Task<IActionResult> ExportUsers()
    {
        var users = await _userService.GetUsersAsync(null);
        var csv = new StringBuilder();
        csv.AppendLine("Member ID,Name,Phone,District,DOB,Father Name,Pincode,Registration Date,Status,Verification Status,Display Member ID,Member ID Display Warning");

        foreach (var item in users)
        {
            var user = await _userService.GetByIdAsync(item.Id);
            if (user is null)
            {
                continue;
            }

            csv.AppendLine(string.Join(",", new[]
            {
                Csv(user.MemberId),
                Csv(user.Name),
                Csv(user.PhoneNumber),
                Csv(user.District),
                Csv(user.DateOfBirth == default ? string.Empty : user.DateOfBirth.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                Csv(user.FatherName),
                Csv(user.Pincode),
                Csv(user.CreatedAt == default ? string.Empty : user.CreatedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                Csv(string.IsNullOrWhiteSpace(user.Status) ? "Active Member" : user.Status),
                Csv(string.IsNullOrWhiteSpace(user.VerificationStatus) ? "Verified" : user.VerificationStatus),
                Csv(item.DisplayMemberId),
                Csv(item.MemberIdDisplayWarning ?? string.Empty)
            }));
        }

        var fileName = $"WTS-Members-Export-{DateTime.UtcNow:yyyyMMdd}.csv";
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(), "text/csv", fileName);
    }

    private static string Csv(string value)
    {
        var safe = value ?? string.Empty;
        return $"\"{safe.Replace("\"", "\"\"")}\"";
    }
}
