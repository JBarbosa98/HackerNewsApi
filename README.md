# Hacker News Best Stories API

A lightweight ASP.NET Core 8 Web API that exposes a single endpoint to retrieve the **best n stories** from [Hacker News](https://news.ycombinator.com/), ranked by score in descending order.

---

## Requirements

| Tool | Minimum version |
|------|----------------|
| [.NET SDK](https://dotnet.microsoft.com/download) | 8.0 |

---

## Running the application

```bash
# 1. Clone the repository
git clone <repo-url>
cd HackerNewsApi

# 2. Restore & run
cd HackerNewsApi
dotnet run
```

The API starts on `http://localhost:5000` (and `https://localhost:5001`).  
Swagger UI is available at `http://localhost:5000/swagger` in Development mode.

### Running with Docker

```bash
docker build -t hackernews-api .
docker run -p 5000:8080 hackernews-api
```

---

## API Reference

### `GET /api/stories/{n}`

Returns the top **n** Hacker News stories ordered by score descending.

| Parameter | Type | Constraints | Description |
|-----------|------|-------------|-------------|
| `n` | integer (path) | 1–500 | Number of stories to return |

#### Example request

```
GET /api/stories/5
```

#### Example response — `200 OK`

```json
[
  {
    "title": "A uBlock Origin update was rejected from the Chrome Web Store",
    "uri": "https://github.com/uBlockOrigin/uBlock-issues/issues/745",
    "postedBy": "ismaildonmez",
    "time": "2019-10-12T13:43:01+00:00",
    "score": 1716,
    "commentCount": 572
  }
]
```

#### Error responses

| Status | Cause |
|--------|-------|
| `400 Bad Request` | `n` is ≤ 0 or > 500 |
| `502 Bad Gateway` | Hacker News API unreachable |
| `500 Internal Server Error` | Unexpected server error |

---

## Running the tests

```bash
cd HackerNewsApi.Tests
dotnet test
```

---

## Architecture & design decisions

### Efficiency — how the API avoids overloading Hacker News

**Two-level in-memory cache**

| Data | Cache key | TTL | Rationale |
|------|-----------|-----|-----------|
| List of best-story IDs | `best_story_ids` | 5 min | HN refreshes rankings infrequently |
| Individual story item | `item_{id}` | 10 min | Story metadata (score, comments) changes slowly |

**Parallel, bounded item fetching**

Individual story items are fetched in parallel using `Parallel.ForEachAsync` with a `MaxDegreeOfParallelism` of 20. This means at most 20 simultaneous HTTP connections to Firebase at any given time, balancing throughput against upstream load.

**Thundering-herd prevention**

A per-item `SemaphoreSlim` (stored in a `ConcurrentDictionary<int, SemaphoreSlim>`) ensures that if many API callers request the same uncached item simultaneously, only **one** outbound HTTP request is made. All other waiters block on the semaphore and then receive the result from the in-memory cache.

**Why we fetch all IDs, not just n**

The Hacker News `/beststories.json` list is ordered by HN's own ranking algorithm (a combination of recency and votes), **not** purely by score. To correctly return the top-n by score we must retrieve every item in the list, sort by score, and then take n. All items are cached individually so subsequent calls are served entirely from memory.

### Project structure

```
HackerNewsApi/
├── Controllers/
│   └── StoriesController.cs     # Thin HTTP layer; validates n, calls service
├── Middleware/
│   └── GlobalExceptionHandler.cs # Catches unhandled exceptions; returns JSON errors
├── Models/
│   ├── HackerNewsItem.cs        # Raw Firebase item DTO
│   └── StoryResponse.cs         # Public API response shape
├── Services/
│   ├── IHackerNewsService.cs    # Abstraction for testability
│   └── HackerNewsService.cs     # Caching + parallel fetching logic
├── Program.cs                   # DI wiring, middleware pipeline
└── appsettings.json

HackerNewsApi.Tests/
├── HackerNewsServiceTests.cs    # Unit tests for service (MockHttp)
└── StoriesControllerTests.cs    # Unit tests for controller (Moq)
```

---

## Assumptions

1. **"Best stories" means the `/beststories.json` list** — HN also exposes `/topstories.json` and `/newstories.json`. The brief explicitly names the beststories endpoint.
2. **n is bounded at 500** — HN returns at most 500 IDs from the beststories endpoint; accepting n > 500 would silently return fewer items than requested.
3. **In-memory cache is sufficient** — a single-instance deployment is assumed. For a horizontally-scaled deployment, a distributed cache (Redis) would be required (see enhancements).
4. **`descendants` maps to `commentCount`** — HN uses `descendants` for the total comment count.
5. **Stories with failed fetches are silently omitted** — partial failures log a warning but do not abort the whole request.

---

## Enhancements given more time

| Area | Enhancement |
|------|-------------|
| **Distributed cache** | Replace `IMemoryCache` with `IDistributedCache` backed by Redis so multiple API instances share a single cache and don't each hammer HN on startup. |
| **Response caching / `ETag`** | Add HTTP response caching headers so load balancers or CDNs can serve repeated identical requests without hitting the app at all. |
| **Background refresh** | Use a hosted `IHostedService` to proactively refresh the best-IDs list and popular items just before their cache entries expire, eliminating cache-miss latency spikes. |
| **Rate limiting** | Add ASP.NET Core 8's built-in rate limiter (`AddRateLimiter`) to protect both the HN upstream and our own API from abuse. |
| **Health checks** | Expose `/health` via `AddHealthChecks()` with an upstream connectivity check so orchestrators (Kubernetes, ECS) can route traffic away from an unhealthy instance. |
| **Observability** | Instrument with OpenTelemetry traces and metrics (cache hit ratio, upstream latency, error rate) and export to Prometheus / Jaeger. |
| **Configuration** | Expose TTLs and concurrency limits via `appsettings.json` / environment variables so they can be tuned without redeployment. |
| **Pagination** | Add optional `offset` parameter so callers can page through results beyond the first n. |
| **Integration tests** | Add `WebApplicationFactory`-based integration tests that spin up the full pipeline against a mocked HN API. |
