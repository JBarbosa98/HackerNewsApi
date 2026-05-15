using HackerNewsApi.Models;
using HackerNewsApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace HackerNewsApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class StoriesController : ControllerBase
{
    private readonly IHackerNewsService _service;
    private readonly ILogger<StoriesController> _log;

    public StoriesController(IHackerNewsService service, ILogger<StoriesController> log)
    {
        _service = service;
        _log     = log;
    }

    [HttpGet("best/{n:int}")]
    [ProducesResponseType(typeof(IReadOnlyList<StoryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<IReadOnlyList<StoryDto>>> GetBestStories(
        [FromRoute] int n,
        CancellationToken ct)
    {
        if (n < 1 || n > 500)
            return BadRequest("n must be between 1 and 500.");

        try
        {
            var stories = await _service.GetBestStoriesAsync(n, ct);
            return Ok(stories);
        }
        catch (OperationCanceledException)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Unhandled error while retrieving best {N} stories.", n);
            return StatusCode(StatusCodes.Status500InternalServerError,
                "An error occurred while contacting the Hacker News API.");
        }
    }
}
