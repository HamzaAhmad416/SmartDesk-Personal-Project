using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using StackExchange.Redis;
using System.Text.Json;
using SmartDesk.Application.Interfaces;

namespace SmartDesk.Infrastructure.Cache;

/// <summary>
/// WHY REDIS + POLLY TOGETHER?
///
/// Redis is an in-memory store — reads are microseconds vs milliseconds for Cosmos.
/// But Redis is a network call and CAN fail. Without protection, one Redis outage
/// would take down the whole app.
///
/// Polly adds two resilience layers:
///
/// 1. RETRY POLICY — on a transient failure (network blip), retry up to 3 times
///    with exponential backoff: wait 2s, then 4s, then 8s before each retry.
///    Handles short transient errors automatically.
///
/// 2. CIRCUIT BREAKER — if Redis fails 5 times in a row, STOP calling it for 30s.
///    Why? Without this, every request hammers a broken Redis, making it worse.
///    With it, we "fail fast" and return null (cache miss) — app reads from Cosmos instead.
///    After 30s, the circuit "half-opens" and tries Redis again.
///
/// This pattern is EXACTLY what you described at Contour Software on your CV:
/// "applied Polly for retry and circuit-breaker policies, achieving 99.9% message delivery"
/// </summary>
public class RedisCacheService : ICacheService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisCacheService> _logger;
    private readonly AsyncPolicy _policy;

    public RedisCacheService(
        IConnectionMultiplexer redis,
        ILogger<RedisCacheService> logger)
    {
        _redis = redis;
        _logger = logger;
        _policy = BuildPolicy();
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        try
        {
            return await _policy.ExecuteAsync(async () =>
            {
                var db = _redis.GetDatabase();
                var value = await db.StringGetAsync(key);
                if (!value.HasValue) return default;
                return JsonSerializer.Deserialize<T>(value!);
            });
        }
        catch (BrokenCircuitException)
        {
            // Circuit is open — Redis is down. Return null = cache miss.
            // App continues by reading from Cosmos. Graceful degradation.
            _logger.LogWarning("Redis circuit open — cache miss for '{Key}'", key);
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default)
    {
        try
        {
            await _policy.ExecuteAsync(async () =>
            {
                var db = _redis.GetDatabase();
                var json = JsonSerializer.Serialize(value);
                await db.StringSetAsync(key, json, expiry ?? TimeSpan.FromMinutes(10));
            });
        }
        catch (BrokenCircuitException)
        {
            _logger.LogWarning("Redis circuit open — skipping cache write for '{Key}'", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await _policy.ExecuteAsync(async () =>
            {
                var db = _redis.GetDatabase();
                await db.KeyDeleteAsync(key);
            });
        }
        catch (BrokenCircuitException)
        {
            _logger.LogWarning("Redis circuit open — skipping cache remove for '{Key}'", key);
        }
    }

    public async Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default)
    {
        try
        {
            var server = _redis.GetServer(_redis.GetEndPoints().First());
            var keys = server.Keys(pattern: $"{prefix}*").ToArray();
            if (keys.Length > 0)
                await _redis.GetDatabase().KeyDeleteAsync(keys);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove keys with prefix '{Prefix}'", prefix);
        }
    }

    private AsyncPolicy BuildPolicy()
    {
        // Retry: wait 2^attempt seconds between retries (2s, 4s, 8s)
        var retry = Policy
            .Handle<RedisException>()
            .Or<TimeoutException>()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                onRetry: (ex, wait, attempt, _) =>
                    _logger.LogWarning(
                        "Redis retry {Attempt}/3 in {Wait}s — {Message}",
                        attempt, wait.TotalSeconds, ex.Message));

        // Circuit breaker: open after 5 failures, stay open 30 seconds
        var circuitBreaker = Policy
            .Handle<RedisException>()
            .Or<TimeoutException>()
            .CircuitBreakerAsync(
                exceptionsAllowedBeforeBreaking: 5,
                durationOfBreak: TimeSpan.FromSeconds(30),
                onBreak: (ex, duration) =>
                    _logger.LogError(
                        "Redis circuit OPENED for {Duration}s — {Message}",
                        duration.TotalSeconds, ex.Message),
                onReset: () =>
                    _logger.LogInformation("Redis circuit CLOSED — connection restored"));

        // Wrap both: retry is inner, circuit breaker is outer
        return Policy.WrapAsync(retry, circuitBreaker);
    }
}
