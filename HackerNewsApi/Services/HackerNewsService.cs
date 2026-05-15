using System.Collections.Concurrent;
using System.Net.Http.Json;
using HackerNewsApi.Models;
using Microsoft.Extensions.Caching.Memory;

namespace HackerNewsApi.Services;

public sealed class HackerNewsService : IHackerNewsService, IDisposable
{
    private static readonly TimeSpan BestIdsCacheDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ItemCacheDuration = TimeSpan.FromMinutes(10);
    private const int MaxConcurrentItemFetches = 20;

    private const string BestStoriesUrl = "https://hacker-news.firebaseio.com/v0/beststories.json";
    private const string ItemUrlTemplate = "https://hacker-news.firebaseio.com/v0/item/{0}.json";

    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;
    private readonly ILogger<HackerNewsService> _logger;

    private readonly ConcurrentDictionary<int, SemaphoreSlim> _itemLocks = new();

    public HackerNewsService(
        HttpClient http,
        IMemoryCache cache,
        ILogger<HackerNewsService> logger)
    {
        _http = http;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<StoryResponse>> GetBestStoriesAsync(
        int n, CancellationToken ct = default)
    {
        var ids = await GetBestIdsAsync(ct);

        var items = await FetchItemsAsync(ids, ct);

        return items
            .OrderByDescending(x => x.Score)
            .Take(n)
            .Select(Map)
            .ToList();
    }

    private async Task<int[]> GetBestIdsAsync(CancellationToken ct)
    {
        const string cacheKey = "best_story_ids";

        if (_cache.TryGetValue(cacheKey, out int[]? ids) && ids is not null)
            return ids;

        _logger.LogInformation("Cache miss - fetching best story IDs from HN.");
        ids = await _http.GetFromJsonAsync<int[]>(BestStoriesUrl, ct)
              ?? Array.Empty<int>();

        _cache.Set(cacheKey, ids, BestIdsCacheDuration);
        return ids;
    }

    private async Task<IReadOnlyList<HackerNewsItem>> FetchItemsAsync(
        int[] ids, CancellationToken ct)
    {
        var bag = new ConcurrentBag<HackerNewsItem>();

        await Parallel.ForEachAsync(
            ids,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = MaxConcurrentItemFetches,
                CancellationToken = ct
            },
            async (id, innerCt) =>
            {
                var item = await GetItemAsync(id, innerCt);
                if (item is not null)
                    bag.Add(item);
            });

        return bag.ToList();
    }

    private async Task<HackerNewsItem?> GetItemAsync(int id, CancellationToken ct)
    {
        var cacheKey = $"item_{id}";

        if (_cache.TryGetValue(cacheKey, out HackerNewsItem? cached))
            return cached;

        var idLock = _itemLocks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));

        await idLock.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(cacheKey, out cached))
                return cached;

            var url = string.Format(ItemUrlTemplate, id);
            var item = await _http.GetFromJsonAsync<HackerNewsItem>(url, ct);

            if (item is not null)
                _cache.Set(cacheKey, item, ItemCacheDuration);

            return item;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch item {Id}.", id);
            return null;
        }
        finally
        {
            idLock.Release();
        }
    }

    private static StoryResponse Map(HackerNewsItem item) => new()
    {
        Title = item.Title ?? string.Empty,
        Uri = item.Url,
        PostedBy = item.By,
        Time = DateTimeOffset.FromUnixTimeSeconds(item.Time),
        Score = item.Score,
        CommentCount = item.Descendants
    };

    public void Dispose()
    {
        foreach (var s in _itemLocks.Values) s.Dispose();
    }
}