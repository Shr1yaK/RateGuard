using Microsoft.AspNetCore.Mvc;

namespace RateGuard.Api.Controllers;

/// <summary>Liveness check. Not rate limited (the middleware skips /health).</summary>
[ApiController]
[Route("health")]
public sealed class HealthController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Get() => Ok(new { status = "healthy" });
}
