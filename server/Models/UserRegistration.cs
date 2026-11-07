using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace WeldingThozhilalarSangam.Api.Models;

public sealed class UserRegistration
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    public string Username { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string CanonicalMobile { get; set; } = string.Empty;
    // Null preserves existing members without rewriting their records.
    public bool? MobileVerified { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string PasswordHash { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime DateOfBirth { get; set; }
    public string BloodGroup { get; set; } = string.Empty;
    public string FatherName { get; set; } = string.Empty;
    public string PermanentAddress { get; set; } = string.Empty;
    public string Pincode { get; set; } = string.Empty;
    public string District { get; set; } = string.Empty;
    public string AadhaarPhotoPath { get; set; } = string.Empty;
    public string ProfilePhotoPath { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string PaymentStatus { get; set; } = "Not Required";
    public string PaymentTransactionId { get; set; } = string.Empty;
    public string PaymentProofUrl { get; set; } = string.Empty;
    public DateTime? PaymentSubmittedAt { get; set; }
    public DateTime? PaymentVerifiedAt { get; set; }
    public string PaymentVerifiedBy { get; set; } = string.Empty;
    public string MemberId { get; set; } = string.Empty;
    public string Status { get; set; } = "Active Member";
    public string VerificationStatus { get; set; } = "Verified";
    public DateTime? VerifiedAt { get; set; }
    public string VerifiedBy { get; set; } = string.Empty;
    public bool ConsentAccepted { get; set; }
    public DateTime? ConsentAcceptedAt { get; set; }
}
