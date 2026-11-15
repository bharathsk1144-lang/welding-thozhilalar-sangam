using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using WeldingThozhilalarSangam.Api.DTOs;
using WeldingThozhilalarSangam.Api.Services;

namespace WeldingThozhilalarSangam.Api.Controllers;

[ApiController]
[Route("api/idcards")]
[Authorize(Policy = "MemberOrAdmin")]
public sealed class IdCardsController : ControllerBase
{
    private readonly UserService _userService;

    public IdCardsController(UserService userService)
    {
        _userService = userService;
    }

    [HttpGet("{userId}")]
    public async Task<IActionResult> Get(string userId)
    {
        if (!User.IsInRole("Admin") && User.FindFirstValue(ClaimTypes.NameIdentifier) != userId) return Forbid();
        var user = await _userService.GetIdCardUserAsync(userId);
        if (user is null)
        {
            return NotFound(new { message = "User not found." });
        }
        if (!User.IsInRole("Admin") && !UserService.IsApproved(user))
            return StatusCode(StatusCodes.Status403Forbidden, new { message = $"ID card unavailable: {UserService.GetApprovalStatus(user)}." });

        var issueDate = user.CreatedAt;
        var expiresAt = user.CardExpiresAt ?? CardValidity.Expiry(issueDate);
        return Ok(await _userService.PresentAsync(new IdCardDto(
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
            $"{expiresAt:dd MMM yyyy}",
            string.IsNullOrWhiteSpace(user.Status) ? "Active Member" : user.Status,
            UserService.IsApproved(user) ? "Verified" : UserService.GetApprovalStatus(user),
            user.VerifiedAt,
            string.IsNullOrWhiteSpace(user.VerifiedBy) ? "System" : user.VerifiedBy,
            user.ProfilePhotoPath,
            expiresAt,
            UserService.IsApproved(user)) { Posting = PostingPolicy.Display(user.Posting) }));
    }
}
