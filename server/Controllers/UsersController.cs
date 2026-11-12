using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;
using System.Collections.Concurrent;
using WeldingThozhilalarSangam.Api.DTOs;
using WeldingThozhilalarSangam.Api.Models;
using WeldingThozhilalarSangam.Api.Services;

namespace WeldingThozhilalarSangam.Api.Controllers;

[ApiController]
[Route("api/users")]
public sealed class UsersController : ControllerBase
{
    private static readonly ConcurrentDictionary<string, LoginAttempt> LoginAttempts = new();
    private static readonly TimeSpan LoginLockoutWindow = TimeSpan.FromMinutes(5);
    private const int MaxLoginAttempts = 5;
    private readonly IWebHostEnvironment _environment;
    private readonly IPrivateUploadStorage _uploads;
    private readonly ILogger<UsersController> _logger;
    private readonly OtpService _otpService;
    private readonly bool _registrationOtpEnabled;
    private readonly DistrictService _districtService;
    private readonly UserService _userService;
    private readonly UserSessionService _sessions;
    private readonly PasswordHasher<UserRegistration> _passwordHasher = new();

    public UsersController(
        IWebHostEnvironment environment,
        OtpService otpService,
        DistrictService districtService,
        UserService userService,
        UserSessionService sessions,
        IPrivateUploadStorage uploads,
        ILogger<UsersController> logger,
        IConfiguration configuration)
    {
        _environment = environment;
        _uploads = uploads;
        _logger = logger;
        _otpService = otpService;
        _registrationOtpEnabled = configuration.GetValue("Authentication:OtpEnabled", true);
        _districtService = districtService;
        _userService = userService;
        _sessions = sessions;
    }

    [HttpPost("register")]
    [EnableRateLimiting("auth")]
    [RequestSizeLimit(12 * 1024 * 1024)]
    public async Task<IActionResult> Register([FromForm] RegisterUserRequest request)
    {
        if (!MobileNumber.TryNormalize(request.PhoneNumber, out var phone))
            return BadRequest(new { message = MobileNumber.InvalidMessage });
        request.PhoneNumber = phone;
        // Check before validation, OTP consumption, file writes, or Member ID generation.
        if (await _userService.PhoneNumberExistsAsync(phone))
            return Conflict(new { code = "mobile_already_registered", message = MobileNumber.DuplicateMessage });

        var validationError = await ValidateRegistrationAsync(request);
        if (validationError is not null)
        {
            return BadRequest(new { message = validationError });
        }

        if (_registrationOtpEnabled && !_otpService.ConsumeVerificationToken(request.VerificationToken, request.PhoneNumber, registrationName: request.Name))
        {
            return BadRequest(new { message = "OTP verification is required before registration." });
        }

        string? aadhaarPath = null;
        string? profilePath = null;
        UserRegistration user;
        try
        {
            aadhaarPath = await _uploads.SaveAsync(request.AadhaarPhoto!, "aadhaar", HttpContext.RequestAborted);
            profilePath = await _uploads.SaveAsync(request.ProfilePhoto!, "profile", HttpContext.RequestAborted);

        user = new UserRegistration
        {
            Username = string.IsNullOrWhiteSpace(request.Username) ? request.PhoneNumber.Trim() : request.Username.Trim(),
            PhoneNumber = request.PhoneNumber.Trim(),
            MobileVerified = _registrationOtpEnabled,
            PasswordHash = HashPassword(request.Password),
            Location = request.Location.Trim(),
            Name = request.Name.Trim(),
            DateOfBirth = request.DateOfBirth,
            BloodGroup = request.BloodGroup.Trim(),
            FatherName = request.FatherName.Trim(),
            PermanentAddress = request.PermanentAddress.Trim(),
            Pincode = request.Pincode.Trim(),
            District = request.District.Trim(),
            AadhaarPhotoPath = aadhaarPath,
            ProfilePhotoPath = profilePath,
            ConsentAccepted = request.ConsentAccepted
        };

            await _userService.CreateAsync(user);
        }
        catch
        {
            await TryDeleteAsync(profilePath);
            await TryDeleteAsync(aadhaarPath);
            throw;
        }
        var session = _sessions.Create(user);

        var memberIdView = await _userService.PresentAsync(new MemberIdView(user.MemberId));
        return Ok(new
        {
            message = "Registration completed successfully. Your Membership ID Card is ready.",
            userId = user.Id,
            memberId = user.MemberId,
            mobileVerified = user.MobileVerified == true,
            displayMemberId = memberIdView.DisplayMemberId,
            memberIdDisplayWarning = memberIdView.MemberIdDisplayWarning,
            token = session.Token,
            expiresAt = session.ExpiresAt,
            idCardUrl = $"/id-card/{user.Id}",
            idCardApiUrl = $"/api/users/{user.Id}/id-card"
        });
    }

    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Login(UserLoginRequest? request)
    {
        if (request is null)
        {
            return BadRequest(new { message = "Request body is required." });
        }

        if (!MobileNumber.TryNormalize(GetPhoneNumber(request.PhoneNumber, request.Phone), out var phoneNumber))
        {
            return BadRequest(new { message = MobileNumber.InvalidMessage });
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { message = "Password is required." });
        }

        if (IsLoginLocked(phoneNumber))
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, new { message = "Too many login attempts. Please try again after 5 minutes." });
        }

        var user = await _userService.GetByPhoneNumberAsync(phoneNumber);
        if (user is null)
        {
            TrackFailedLogin(phoneNumber);
            return Unauthorized(new { message = "Invalid mobile number or password." });
        }

        if (string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            return BadRequest(new { message = "Password not set for this account. Please use Forgot Password to create a password." });
        }

        var passwordResult = PasswordVerificationResult.Failed;
        try { passwordResult = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password); }
        catch (FormatException) { /* A malformed legacy hash must never authenticate. */ }
        if (passwordResult == PasswordVerificationResult.Failed)
        {
            TrackFailedLogin(phoneNumber);
            return Unauthorized(new { message = "Invalid mobile number or password." });
        }

        ClearFailedLogin(phoneNumber);

        if (string.IsNullOrWhiteSpace(user.MemberId))
        {
            user = await _userService.GetIdCardUserAsync(user.Id ?? string.Empty) ?? user;
        }

        var session = _sessions.Create(user);
        return Ok(await _userService.PresentAsync(new UserLoginResponse(
            user.Id ?? string.Empty,
            user.MemberId,
            user.Name,
            user.PhoneNumber,
            user.District,
            session.Token,
            session.ExpiresAt)));
    }

    [HttpPost("forgot-password/send-otp")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> ForgotPasswordSendOtp(ForgotPasswordSendOtpRequest? request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { message = "Request body is required." });
        }

        if (!MobileNumber.TryNormalize(GetPhoneNumber(request.PhoneNumber, request.Phone), out var phoneNumber))
        {
            return BadRequest(new { message = MobileNumber.InvalidMessage });
        }

        var recoveryUser = await _userService.GetByPhoneNumberAsync(phoneNumber);
        if (recoveryUser is null || recoveryUser.MobileVerified == false)
        {
            return Ok(new { message = "If this mobile number is registered and verified, an OTP has been sent." });
        }

        var otpResult = await _otpService.SendOtpAsync(phoneNumber, _environment.IsDevelopment(), cancellationToken, OtpService.PasswordReset);
        return Ok(new { message = "If this mobile number is registered and verified, an OTP has been sent.", developmentOtp = otpResult.DevelopmentOtp });
    }

    [HttpPost("forgot-password/reset")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> ForgotPasswordReset(ForgotPasswordResetRequest? request)
    {
        if (request is null)
        {
            return BadRequest(new { message = "Request body is required." });
        }

        if (!MobileNumber.TryNormalize(GetPhoneNumber(request.PhoneNumber, request.Phone), out var phoneNumber))
        {
            return BadRequest(new { message = MobileNumber.InvalidMessage });
        }

        if (!IsValidPassword(request.NewPassword))
        {
            return BadRequest(new { message = "Password must be at least 8 characters." });
        }

        var user = await _userService.GetByPhoneNumberAsync(phoneNumber);
        if (user?.MobileVerified == false)
            return BadRequest(new { message = "OTP verification is required before resetting password." });

        if (!_otpService.ConsumeVerificationToken(request.VerificationToken, phoneNumber, OtpService.PasswordReset))
        {
            return BadRequest(new { message = "OTP verification is required before resetting password." });
        }

        if (user is null || string.IsNullOrWhiteSpace(user.Id))
        {
            return NotFound(new { message = "No registration found for this phone number. Please register first." });
        }

        await _userService.UpdatePasswordHashAsync(user.Id, _passwordHasher.HashPassword(user, request.NewPassword));
        ClearFailedLogin(phoneNumber);

        return Ok(new { message = "Password reset successfully. Please login." });
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> GetUsers()
    {
        return Ok(await _userService.GetUsersAsync(null));
    }

    [Authorize]
    [HttpGet("{id}")]
    public async Task<IActionResult> GetUserById(string id)
    {
        var user = await _userService.GetByIdAsync(id);
        if (user is null)
        {
            return NotFound(new { message = "User not found." });
        }

        return Ok(await _userService.PresentAsync(ToDetailsDto(user)));
    }

    [Authorize(Policy = "MemberOrAdmin")]
    [HttpGet("{id}/id-card")]
    public async Task<IActionResult> GetUserIdCard(string id)
    {
        if (!User.IsInRole("Admin") && User.FindFirstValue(ClaimTypes.NameIdentifier) != id) return Forbid();
        var user = await _userService.GetIdCardUserAsync(id);
        if (user is null)
        {
            return NotFound(new { message = "User not found." });
        }

        return Ok(await _userService.PresentAsync(ToIdCardDto(user)));
    }

    [Authorize(Policy = "MemberOrAdmin")]
    [HttpGet("{id}/officer-signatures/{officer}")]
    public async Task<IActionResult> GetOfficerSignature(string id, string officer)
    {
        if (!User.IsInRole("Admin") && User.FindFirstValue(ClaimTypes.NameIdentifier) != id) return Forbid();
        if (await _userService.GetIdCardUserAsync(id) is null) return NotFound();
        var filename = officer switch
        {
            "president" => "state-president-signature-blue.png",
            "secretary" => "state-secretary-signature-blue.png",
            "treasurer" => "state-treasurer-signature-blue.png",
            _ => null
        };
        if (filename is null) return NotFound();
        var path = Path.Combine(_environment.ContentRootPath, "Assets", "OfficerSignatures", filename);
        if (!System.IO.File.Exists(path)) return NotFound();
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        return File(await System.IO.File.ReadAllBytesAsync(path, HttpContext.RequestAborted), "image/png");
    }

    [HttpGet("verify/{**memberId}")]
    [EnableRateLimiting("public-verification")]
    public async Task<IActionResult> VerifyMember(string memberId)
    {
        if (string.IsNullOrWhiteSpace(memberId))
        {
            return BadRequest(new { message = "Member ID is required." });
        }

        var user = await _userService.GetByMemberIdAsync(memberId);
        // Encoded slash values can remain escaped in ASP.NET route parameters.
        // Prefer the exact legacy value before trying the official-number form.
        if (user is null && memberId.Contains("%2F", StringComparison.OrdinalIgnoreCase))
            user = await _userService.GetByMemberIdAsync(Uri.UnescapeDataString(memberId));
        if (user is null || string.IsNullOrWhiteSpace(user.MemberId))
        {
            return NotFound(new { message = "No active member found for this Member ID." });
        }

        var issueDate = user.CreatedAt;
        return Ok(await _userService.PresentAsync(new MemberVerificationDto(
            user.Name,
            user.MemberId,
            user.District,
            GetMemberStatus(user),
            GetVerificationStatus(user),
            $"{(user.CardExpiresAt ?? CardValidity.Expiry(issueDate)):dd MMM yyyy}",
            user.ApprovedAt.HasValue ? "Verified Welding Thozhilalar Sangam member." : "Membership approval pending.")));
    }

    [Authorize]
    [HttpGet("by-district/{district}")]
    public async Task<IActionResult> GetUsersByDistrict(string district)
    {
        if (string.IsNullOrWhiteSpace(district))
        {
            return BadRequest(new { message = "District is required." });
        }

        return Ok(await _userService.GetUsersAsync(district));
    }

    private async Task<string?> ValidateRegistrationAsync(RegisterUserRequest request)
    {
        if (!request.HasJoinedWhatsAppGroup)
        {
            return "KINDLY JOIN WHATSAPP GROUP";
        }

        if (!request.ConsentAccepted)
        {
            return "மேலே உள்ள சங்கத்தின் விதிமுறைகளைப் படித்து ஒப்புக்கொள்ளவும்.";
        }

        if (string.IsNullOrWhiteSpace(request.Name) ||
            string.IsNullOrWhiteSpace(request.PhoneNumber) ||
            string.IsNullOrWhiteSpace(request.Password) ||
            string.IsNullOrWhiteSpace(request.Location) ||
            string.IsNullOrWhiteSpace(request.BloodGroup) ||
            string.IsNullOrWhiteSpace(request.FatherName) ||
            string.IsNullOrWhiteSpace(request.PermanentAddress) ||
            string.IsNullOrWhiteSpace(request.District) ||
            string.IsNullOrWhiteSpace(request.Pincode))
        {
            return "Required fields are missing.";
        }

        if (!IsValidPassword(request.Password))
        {
            return "Password must be at least 8 characters.";
        }

        var phoneNumber = request.PhoneNumber.Trim();
        if (phoneNumber.Length != 10 || !phoneNumber.All(char.IsDigit))
        {
            return "Phone number must be 10 digits.";
        }

        var pincode = request.Pincode.Trim();
        if (pincode.Length != 6 || !pincode.All(char.IsDigit))
        {
            return "Pincode must be 6 digits.";
        }

        if (request.DateOfBirth == default || request.DateOfBirth.Date >= DateTime.UtcNow.Date)
        {
            return "A valid date of birth is required.";
        }

        if (await _districtService.GetByNameAsync(request.District.Trim()) is null)
        {
            return "Selected district is invalid.";
        }

        var aadhaarError = await UploadValidation.ValidateAsync(request.AadhaarPhoto, 5 * 1024 * 1024, "Aadhaar card photo");
        if (aadhaarError is not null)
        {
            return aadhaarError;
        }

        var profileError = await UploadValidation.ValidateAsync(request.ProfilePhoto, 3 * 1024 * 1024, "Passport Size Photo");
        if (profileError is not null)
        {
            return profileError;
        }

        return null;
    }

    private string HashPassword(string password)
    {
        return _passwordHasher.HashPassword(new UserRegistration(), password);
    }

    private static bool IsValidPassword(string password)
    {
        return !string.IsNullOrWhiteSpace(password) && password.Length >= 8;
    }

    private static string GetPhoneNumber(string phoneNumber, string phone)
    {
        return string.IsNullOrWhiteSpace(phoneNumber) ? phone.Trim() : phoneNumber.Trim();
    }

    private static bool IsLoginLocked(string phoneNumber)
    {
        if (!LoginAttempts.TryGetValue(phoneNumber, out var attempt))
        {
            return false;
        }

        if (attempt.LockedUntil is null || attempt.LockedUntil <= DateTime.UtcNow)
        {
            if (attempt.LockedUntil <= DateTime.UtcNow)
            {
                LoginAttempts.TryRemove(phoneNumber, out _);
            }

            return false;
        }

        return true;
    }

    private static void TrackFailedLogin(string phoneNumber)
    {
        LoginAttempts.AddOrUpdate(
            phoneNumber,
            _ => new LoginAttempt(1, null),
            (_, current) =>
            {
                var count = current.Count + 1;
                return count >= MaxLoginAttempts
                    ? new LoginAttempt(count, DateTime.UtcNow.Add(LoginLockoutWindow))
                    : new LoginAttempt(count, null);
            });
    }

    private async Task TryDeleteAsync(string? reference)
    {
        if (reference is null) return;
        try { await _uploads.DeleteAsync(reference, CancellationToken.None); }
        catch (Exception ex) { _logger.LogWarning("Registration upload cleanup failed ({ExceptionType}).", ex.GetType().Name); }
    }

    private static void ClearFailedLogin(string phoneNumber)
    {
        LoginAttempts.TryRemove(phoneNumber, out _);
    }

    private static UserDetailsDto ToDetailsDto(UserRegistration user)
    {
        return new UserDetailsDto(
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
            GetMemberStatus(user),
            GetVerificationStatus(user),
            user.VerifiedAt,
            string.IsNullOrWhiteSpace(user.VerifiedBy) ? "System" : user.VerifiedBy,
            user.ConsentAccepted,
            user.ConsentAcceptedAt);
    }

    private static IdCardDto ToIdCardDto(UserRegistration user)
    {
        var issueDate = user.CreatedAt;
        return new IdCardDto(
            user.Id ?? string.Empty,
            user.Name,
            user.FatherName,
            user.DateOfBirth,
            user.BloodGroup,
            user.District,
            user.PhoneNumber,
            user.PermanentAddress,
            user.MemberId,
            issueDate,
            $"{(user.CardExpiresAt ?? CardValidity.Expiry(issueDate)):dd MMM yyyy}",
            GetMemberStatus(user),
            GetVerificationStatus(user),
            user.VerifiedAt,
            string.IsNullOrWhiteSpace(user.VerifiedBy) ? "System" : user.VerifiedBy,
            user.ProfilePhotoPath,
            user.CardExpiresAt ?? CardValidity.Expiry(issueDate),
            user.ApprovedAt.HasValue);
    }

    private static string GetMemberStatus(UserRegistration user)
    {
        return string.IsNullOrWhiteSpace(user.Status) ? "Active Member" : user.Status;
    }

    private static string GetVerificationStatus(UserRegistration user)
    {
        return user.ApprovedAt.HasValue ? "Verified" : "Pending";
    }

    private sealed record LoginAttempt(int Count, DateTime? LockedUntil);
}
