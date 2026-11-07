using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WeldingThozhilalarSangam.Api.DTOs;
using WeldingThozhilalarSangam.Api.Services;

namespace WeldingThozhilalarSangam.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/dashboard")]
public sealed class DashboardController : ControllerBase
{
    private readonly UserService _userService;

    public DashboardController(UserService userService)
    {
        _userService = userService;
    }

    [HttpGet("summary")]
    public async Task<IActionResult> Summary()
    {
        var totalUsers = await _userService.CountAsync();
        var paymentPendingUsers = await _userService.CountPendingPaymentsAsync();
        var districtCounts = await _userService.CountByDistrictAsync();
        return Ok(new DashboardDto(totalUsers, paymentPendingUsers, districtCounts));
    }
}
