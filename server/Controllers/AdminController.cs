using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Globalization;
using System.Text;
using System.Security.Claims;
using MongoDB.Bson;
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
            user.ApprovedAt.HasValue ? "Verified" : "Pending",
            user.VerifiedAt,
            string.IsNullOrWhiteSpace(user.VerifiedBy) ? "System" : user.VerifiedBy,
            user.ConsentAccepted,
            user.ConsentAcceptedAt)));
    }

    [Authorize]
    [HttpPut("users/{id}/approval")]
    public async Task<IActionResult> SetApproval(string id, [FromBody] ApprovalRequest request)
    {
        var actor = User.Identity?.Name ?? "Admin";
        var user = await _userService.SetApprovalAsync(id, request.Approved, actor);
        return user is null ? NotFound() : Ok(new { approved = user.ApprovedAt.HasValue, approvedAt = user.ApprovedAt });
    }

    public sealed record ApprovalRequest(bool Approved);

    [Authorize(Roles = "Admin")]
    [HttpPut("users/{id}")]
    public async Task<IActionResult> EditUser(string id, [FromBody] EditMemberRequest? request, [FromServices] DistrictService districts)
    {
        if (!ObjectId.TryParse(id, out _)) return BadRequest(new { message = "Invalid member record ID." });
        if (request is null) return BadRequest(new { message = "Member details are required." });
        if (request.UnexpectedFields is { Count: > 0 })
            return BadRequest(new { message = "Only member contact and registration details can be edited here." });
        if (!MobileNumber.TryNormalize(request.PhoneNumber, out _))
            return BadRequest(new { message = MobileNumber.InvalidMessage });
        if (new[] { request.Name, request.Location, request.BloodGroup, request.FatherName,
            request.PermanentAddress, request.Pincode, request.District }.Any(string.IsNullOrWhiteSpace))
            return BadRequest(new { message = "Required fields are missing." });
        if (request.Name.Trim().Length > 100 || request.FatherName.Trim().Length > 100 ||
            request.Location.Trim().Length > 150 || request.PermanentAddress.Trim().Length > 500)
            return BadRequest(new { message = "A text field exceeds its allowed length." });
        if (!new[] { "A+", "A-", "B+", "B-", "AB+", "AB-", "O+", "O-" }.Contains(request.BloodGroup.Trim()))
            return BadRequest(new { message = "Selected blood group is invalid." });
        if (request.Pincode.Trim().Length != 6 || !request.Pincode.Trim().All(c => c is >= '0' and <= '9'))
            return BadRequest(new { message = "Pincode must be 6 digits." });
        if (request.DateOfBirth == default || request.DateOfBirth.Date >= DateTime.UtcNow.Date)
            return BadRequest(new { message = "A valid date of birth is required." });
        if (await districts.GetByNameAsync(request.District) is null)
            return BadRequest(new { message = "Selected district is invalid." });

        var updated = await _userService.EditMemberAsync(id, request, User.FindFirstValue(ClaimTypes.Name) ?? "Admin");
        if (updated is null) return NotFound(new { message = "User not found." });
        return Ok(new { message = "Member details saved successfully." });
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
                Csv(user.ApprovedAt.HasValue ? "Verified" : "Pending"),
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
