using HackerNewsApi.Models;

namespace HackerNewsApi.Services;

public interface IHackerNewsService
{
    Task<IReadOnlyList<StoryResponse>> GetBestStoriesAsync(int n, CancellationToken ct = default);
}