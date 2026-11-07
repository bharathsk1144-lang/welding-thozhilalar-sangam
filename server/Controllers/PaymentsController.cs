using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WeldingThozhilalarSangam.Api.DTOs;
using WeldingThozhilalarSangam.Api.Services;

namespace WeldingThozhilalarSangam.Api.Controllers;

[ApiController]
[Route("api/payments")]
public sealed class PaymentsController : ControllerBase
{
    private readonly PrivateUploadStorage _uploads;
    private readonly UserService _userService;

    public PaymentsController(PrivateUploadStorage uploads, UserService userService)
    {
        _uploads = uploads;
        _userService = userService;
    }

    [HttpPost("submit")]
    [Authorize(Policy = "MemberOrAdmin")]
    [RequestSizeLimit(8 * 1024 * 1024)]
    public async Task<IActionResult> Submit([FromForm] SubmitPaymentRequest request)
    {
        if (!User.IsInRole("Admin") && User.FindFirstValue(ClaimTypes.NameIdentifier) != request.UserId) return Forbid();
        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            return BadRequest(new { message = "Registration reference is required." });
        }

        if (string.IsNullOrWhiteSpace(request.PaymentTransactionId))
        {
            return BadRequest(new { message = "Transaction ID / UTR number is required." });
        }

        var user = await _userService.GetByIdAsync(request.UserId.Trim());
        if (user is null)
        {
            return NotFound(new { message = "Registered user was not found." });
        }

        if (user.PaymentStatus == "Verified")
        {
            return BadRequest(new { message = "Payment is already verified for this registration." });
        }

        var proofError = await UploadValidation.ValidateAsync(request.PaymentProof, 5 * 1024 * 1024, "Payment proof", required: false, allowPdf: true);
        if (proofError is not null)
        {
            return BadRequest(new { message = proofError });
        }

        var proofUrl = request.PaymentProof is null ? user.PaymentProofUrl : await SaveUploadAsync(request.PaymentProof);
        var updated = await _userService.SubmitPaymentAsync(user.Id ?? string.Empty, request.PaymentTransactionId, proofUrl);
        if (!updated)
        {
            return NotFound(new { message = "Registered user was not found." });
        }

        return Ok(new { message = "Payment details submitted. Verification is pending.", paymentStatus = "Pending" });
    }

    [Authorize]
    [HttpGet("pending")]
    public async Task<IActionResult> Pending()
    {
        return Ok(await _userService.GetPendingPaymentsAsync());
    }

    [Authorize]
    [HttpPut("{userId}/verify")]
    public async Task<IActionResult> Verify(string userId)
    {
        var adminName = User.FindFirstValue(ClaimTypes.Name) ?? "Admin";
        var user = await _userService.VerifyPaymentAsync(userId, adminName);
        if (user is null)
        {
            return NotFound(new { message = "Registered user was not found." });
        }

        return Ok(await _userService.PresentAsync(new PaymentActionResponse("Payment verified. ID card is enabled.", user.PaymentStatus, user.MemberId)));
    }

    [Authorize]
    [HttpPut("{userId}/reject")]
    public async Task<IActionResult> Reject(string userId)
    {
        var adminName = User.FindFirstValue(ClaimTypes.Name) ?? "Admin";
        var user = await _userService.RejectPaymentAsync(userId, adminName);
        if (user is null)
        {
            return NotFound(new { message = "Registered user was not found." });
        }

        return Ok(await _userService.PresentAsync(new PaymentActionResponse("Payment rejected.", user.PaymentStatus, user.MemberId)));
    }

    private async Task<string> SaveUploadAsync(IFormFile file)
    {
        var uploadRoot = _uploads.RootPath;
        Directory.CreateDirectory(uploadRoot);

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var fileName = $"payment-proof-{Guid.NewGuid():N}{extension}";
        var absolutePath = Path.Combine(uploadRoot, fileName);

        await using var stream = System.IO.File.Create(absolutePath);
        await file.CopyToAsync(stream);

        return $"/uploads/{fileName}";
    }
}
