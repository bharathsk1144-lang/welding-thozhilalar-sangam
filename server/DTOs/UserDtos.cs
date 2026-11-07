using Microsoft.AspNetCore.Http;

namespace WeldingThozhilalarSangam.Api.DTOs;

public sealed class RegisterUserRequest
{
    public string? VerificationToken { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime DateOfBirth { get; set; }
    public string BloodGroup { get; set; } = string.Empty;
    public string FatherName { get; set; } = string.Empty;
    public string PermanentAddress { get; set; } = string.Empty;
    public string Pincode { get; set; } = string.Empty;
    public string District { get; set; } = string.Empty;
    public bool HasJoinedWhatsAppGroup { get; set; }
    public bool ConsentAccepted { get; set; }
    public IFormFile? AadhaarPhoto { get; set; }
    public IFormFile? ProfilePhoto { get; set; }
}

public sealed record UserListItemDto(
    string Id,
    string Name,
    string Username,
    string PhoneNumber,
    string District,
    string MemberId,
    DateTime CreatedAt,
    string IdCardStatus) : IMemberIdView
{
    public string DisplayMemberId { get; set; } = MemberId;
    public string? MemberIdDisplayWarning { get; set; }
}

public sealed record UserDetailsDto(
    string Id,
    string Username,
    string PhoneNumber,
    string Location,
    string Name,
    DateTime DateOfBirth,
    string BloodGroup,
    string FatherName,
    string PermanentAddress,
    string Pincode,
    string District,
    string AadhaarPhotoPath,
    string ProfilePhotoPath,
    DateTime CreatedAt,
    string PaymentStatus,
    string PaymentTransactionId,
    string PaymentProofUrl,
    DateTime? PaymentSubmittedAt,
    DateTime? PaymentVerifiedAt,
    string PaymentVerifiedBy,
    string MemberId,
    string IdCardStatus,
    string Status,
    string VerificationStatus,
    DateTime? VerifiedAt,
    string VerifiedBy,
    bool ConsentAccepted,
    DateTime? ConsentAcceptedAt) : IMemberIdView
{
    public string DisplayMemberId { get; set; } = MemberId;
    public string? MemberIdDisplayWarning { get; set; }
}

public sealed class SubmitPaymentRequest
{
    public string UserId { get; set; } = string.Empty;
    public string PaymentTransactionId { get; set; } = string.Empty;
    public IFormFile? PaymentProof { get; set; }
}

public sealed record PaymentPendingDto(
    string Id,
    string Name,
    string PhoneNumber,
    string District,
    DateTime CreatedAt,
    string PaymentStatus,
    string PaymentTransactionId,
    string PaymentProofUrl,
    DateTime? PaymentSubmittedAt);

public sealed record PaymentActionResponse(string Message, string PaymentStatus, string MemberId) : IMemberIdView
{
    public string DisplayMemberId { get; set; } = MemberId;
    public string? MemberIdDisplayWarning { get; set; }
}

public sealed class UserLoginRequest
{
    public string PhoneNumber { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public sealed record UserLoginResponse(
    string UserId,
    string MemberId,
    string Name,
    string Phone,
    string District,
    string Token,
    DateTime ExpiresAt) : IMemberIdView
{
    public string DisplayMemberId { get; set; } = MemberId;
    public string? MemberIdDisplayWarning { get; set; }
}

public sealed class ForgotPasswordSendOtpRequest
{
    public string PhoneNumber { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
}

public sealed class ForgotPasswordResetRequest
{
    public string PhoneNumber { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string VerificationToken { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public sealed record MemberVerificationDto(
    string Name,
    string MemberId,
    string District,
    string Status,
    string VerificationStatus,
    string Validity,
    string Message) : IMemberIdView
{
    public string DisplayMemberId { get; set; } = MemberId;
    public string? MemberIdDisplayWarning { get; set; }
}

public sealed record IdCardDto(
    string Id,
    string Name,
    string FatherName,
    DateTime DateOfBirth,
    string BloodGroup,
    string District,
    string PhoneNumber,
    string PermanentAddress,
    string MemberId,
    DateTime IssueDate,
    string ValidityYear,
    string Status,
    string VerificationStatus,
    DateTime? VerifiedAt,
    string VerifiedBy,
    string ProfilePhotoPath) : IMemberIdView
{
    public string DisplayMemberId { get; set; } = MemberId;
    public string? MemberIdDisplayWarning { get; set; }
}
