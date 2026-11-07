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

        var issueDate = user.CreatedAt == default ? DateTime.UtcNow : user.CreatedAt;
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
            $"{issueDate:yyyy}-{issueDate.AddYears(1):yyyy}",
            string.IsNullOrWhiteSpace(user.Status) ? "Active Member" : user.Status,
            string.IsNullOrWhiteSpace(user.VerificationStatus) ? "Verified" : user.VerificationStatus,
            user.VerifiedAt,
            string.IsNullOrWhiteSpace(user.VerifiedBy) ? "System" : user.VerifiedBy,
            user.ProfilePhotoPath)));
    }
}
