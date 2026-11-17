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
    private readonly AadhaarNumberService _aadhaarNumbers;

    public AdminController(AdminAuthService adminAuthService, UserService userService, AadhaarNumberService aadhaarNumbers)
    {
        _adminAuthService = adminAuthService;
        _userService = userService;
        _aadhaarNumbers = aadhaarNumbers;
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

        return Ok(new DashboardDto(totalUsers, paymentPendingUsers, districtCounts) {
            PendingApprovalCount = await _userService.CountPendingApprovalsAsync()
        });
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
            UserService.IsApproved(user) ? "Verified" : UserService.GetApprovalStatus(user),
            user.VerifiedAt,
            string.IsNullOrWhiteSpace(user.VerifiedBy) ? "System" : user.VerifiedBy,
            user.ConsentAccepted,
            user.ConsentAcceptedAt,
            user.ProfilePhotoReviewStatus,
            user.AadhaarReviewStatus,
            user.DocumentCorrectionRequest) { Posting = PostingPolicy.Display(user.Posting),
                AadhaarNumberMasked = _aadhaarNumbers.Mask(user.AadhaarNumberCiphertext),
                ApprovalDecidedAt = user.ApprovalDecidedAt, ApprovalDecidedBy = user.ApprovalDecidedBy }));
    }

    [Authorize]
    [HttpPut("users/{id}/approval")]
    public async Task<IActionResult> SetApproval(string id, [FromBody] ApprovalRequest request)
    {
        var actor = User.Identity?.Name ?? "Admin";
        var user = await _userService.SetApprovalAsync(id, request.Approved, actor);
        return user is null ? NotFound() : Ok(new { approved = UserService.IsApproved(user), approvedAt = user.ApprovedAt });
    }

    public sealed record ApprovalRequest(bool Approved);

    [Authorize(Roles = "Admin")]
    [HttpGet("pending-approvals")]
    public async Task<IActionResult> PendingApprovals() => Ok(await _userService.GetPendingApprovalsAsync());

    public sealed record ApprovalDecisionRequest(string Decision, string? Note);

    [Authorize(Roles = "Admin")]
    [HttpPut("users/{id}/approval-decision")]
    public async Task<IActionResult> DecideApproval(string id, [FromBody] ApprovalDecisionRequest? request)
    {
        if (!ObjectId.TryParse(id, out _) || request is null ||
            request.Decision is not ("Approved" or "Rejected" or "Correction requested") ||
            (request.Note?.Length ?? 0) > 400 || (request.Note?.Contains('<') ?? false) ||
            (request.Note?.Contains('>') ?? false)) return BadRequest(new { message = "Invalid approval decision." });
        var member = await _userService.DecideApprovalAsync(id, request.Decision,
            User.FindFirstValue(ClaimTypes.Name) ?? "Admin", request.Note?.Trim() ?? "");
        return member is null ? NotFound() : Ok(new { status = UserService.GetApprovalStatus(member), member.ApprovalDecidedAt,
            member.ApprovalDecidedBy });
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("users/{id}/profile-photo")]
    [RequestSizeLimit(4 * 1024 * 1024)]
    public async Task<IActionResult> ReplaceProfilePhoto(string id, IFormFile? photo, [FromServices] IPrivateUploadStorage uploads)
    {
        if (!ObjectId.TryParse(id, out _)) return BadRequest(new { message = "Invalid member record ID." });
        var error = await UploadValidation.ValidateAsync(photo, 3 * 1024 * 1024, "Passport photo");
        if (error is not null) return BadRequest(new { message = error });
        var (imageError, _) = await ImageSuitability.CheckAsync(photo!, true, HttpContext.RequestAborted);
        if (imageError is not null) return BadRequest(new { message = imageError });
        if (await _userService.GetByIdAsync(id) is null) return NotFound();
        var path = await uploads.SaveAsync(photo!, "profile", HttpContext.RequestAborted);
        try
        {
            var member = await _userService.ReplaceProfilePhotoAsync(id, path, User.FindFirstValue(ClaimTypes.Name) ?? "Admin");
            return Ok(new { message = "Photo updated. Review its suitability before approval.", profilePhotoPath = member!.ProfilePhotoPath });
        }
        catch { await uploads.DeleteAsync(path); throw; }
    }

    public sealed record DocumentReviewRequest(string Kind, string Status, string? Correction);

    [Authorize(Roles = "Admin")]
    [HttpPut("users/{id}/document-review")]
    public async Task<IActionResult> ReviewDocument(string id, [FromBody] DocumentReviewRequest request)
    {
        if (!ObjectId.TryParse(id, out _) || request.Kind is not ("aadhaar" or "profile") ||
            request.Status is not ("Needs review" or "Accepted" or "Correction requested") ||
            (request.Correction?.Length ?? 0) > 400)
            return BadRequest(new { message = "Invalid document review." });
        var current = await _userService.GetByIdAsync(id);
        if (current is null) return NotFound();
        if (request.Kind == "aadhaar" && (!string.IsNullOrEmpty(current.AadhaarNumberCiphertext) || string.IsNullOrEmpty(current.AadhaarPhotoPath)))
            return BadRequest(new { message = "Aadhaar image review is available only for historical image records." });
        var member = await _userService.SetDocumentReviewAsync(id, request.Kind, request.Status,
            request.Status == "Correction requested" ? request.Correction?.Trim() ?? "Please upload a clearer image." : string.Empty,
            User.FindFirstValue(ClaimTypes.Name) ?? "Admin");
        return member is null ? NotFound() : Ok(new { message = "Review status saved." });
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("users/{id}")]
    public async Task<IActionResult> EditUser(string id, [FromBody] EditMemberRequest? request, [FromServices] DistrictService districts)
    {
        if (!ObjectId.TryParse(id, out _)) return BadRequest(new { message = "Invalid member record ID." });
        if (request is null) return BadRequest(new { message = "Member details are required." });
        if (request.UnexpectedFields is { Count: > 0 })
            return BadRequest(new { message = "Only member contact and registration details can be edited here." });
        if (!PostingPolicy.TryNormalize(request.Posting, out _))
            return BadRequest(new { message = "Posting must be a short, plain-text responsibility." });
        if (!MobileNumber.TryNormalize(request.PhoneNumber, out _))
            return BadRequest(new { message = MobileNumber.InvalidMessage });
        if (new[] { request.Name, request.BloodGroup, request.FatherName,
            request.PermanentAddress, request.Pincode, request.District }.Any(string.IsNullOrWhiteSpace))
            return BadRequest(new { message = "Required fields are missing." });
        if (request.Name.Trim().Length > 100 || request.FatherName.Trim().Length > 100 ||
            (request.Location?.Trim().Length ?? 0) > 150 || request.PermanentAddress.Trim().Length > 500)
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
                Csv(UserService.GetApprovalStatus(user)),
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
