using Microsoft.AspNetCore.Mvc;

namespace RateGuard.Api.Controllers;

/// <summary>
/// A sample protected endpoint. It knows nothing about rate limiting: the
/// middleware has already decided before this code runs.
/// </summary>
[ApiController]
[Route("api/resource")]
public sealed class ResourceController : ControllerBase
{
    private readonly TimeProvider _timeProvider;

    public ResourceController(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public IActionResult Get() => Ok(new
    {
        message = "You reached the protected resource.",
        servedAtUtc = _timeProvider.GetUtcNow(),
    });
}
