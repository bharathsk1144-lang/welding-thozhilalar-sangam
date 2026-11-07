using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WeldingThozhilalarSangam.Api.Services;

namespace WeldingThozhilalarSangam.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/statistics")]
[EnableRateLimiting("public-statistics")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class StatisticsController(IPublicStatisticsService statistics, ILogger<StatisticsController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await statistics.GetAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Do not log exception messages that may contain private connection/query details.
            logger.LogWarning("Public statistics unavailable ({FailureType}).", exception.GetType().Name);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = "Membership statistics are temporarily unavailable. Please try again later." });
        }
    }
}
