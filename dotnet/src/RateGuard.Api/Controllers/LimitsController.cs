using Microsoft.AspNetCore.Mvc;
using RateGuard.Api.Models;
using RateGuard.Api.RateLimiting;

namespace RateGuard.Api.Controllers;

/// <summary>
/// Read and change a client's limits.
/// NOTE: no authentication on purpose (demo). A real system would restrict PUT to admins.
/// </summary>
[ApiController]
[Route("api/limits")]
[Produces("application/json")]
public sealed class LimitsController : ControllerBase
{
    private readonly IRateLimitStore _store;

    public LimitsController(IRateLimitStore store)
    {
        _store = store;
    }

    /// <summary>Current tokens and capacity for a client that has made at least one request.</summary>
    [HttpGet("{clientId}")]
    [ProducesResponseType<LimitStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public ActionResult<LimitStatusResponse> Get(string clientId)
    {
        TokenBucket? bucket = _store.FindBucket(clientId);
        if (bucket is null)
        {
            return Problem(
                title: "Client not found",
                detail: $"No requests have been seen from client '{clientId}'.",
                statusCode: StatusCodes.Status404NotFound);
        }

        return LimitStatusResponse.From(clientId, bucket.GetSnapshot());
    }

    /// <summary>Sets a custom policy for a client. The client gets a fresh, full bucket.</summary>
    /// <remarks>
    /// [ApiController] validates the body's [Range] attributes before this runs
    /// and returns 400 with the errors if they fail.
    /// </remarks>
    [HttpPut("{clientId}")]
    [ProducesResponseType<LimitStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public ActionResult<LimitStatusResponse> Put(string clientId, [FromBody] RateLimitPolicy policy)
    {
        _store.SetPolicy(clientId, policy);

        TokenBucket bucket = _store.GetOrCreateBucket(clientId);
        return LimitStatusResponse.From(clientId, bucket.GetSnapshot());
    }
}
