using Microsoft.Extensions.Caching.Memory;

namespace BackendApi.Security;

public sealed class LoginAttemptService
{
    private static readonly TimeSpan AttemptWindow = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    private const int MaxFailures = 5;

    private readonly IMemoryCache _cache;

    public LoginAttemptService(IMemoryCache cache)
    {
        _cache = cache;
    }

    public bool IsLockedOut(string key, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;

        if (!_cache.TryGetValue<LoginAttemptState>(key, out var state) || state is null)
        {
            return false;
        }

        if (state.LockedUntil is null || state.LockedUntil <= DateTimeOffset.UtcNow)
        {
            return false;
        }

        retryAfter = state.LockedUntil.Value - DateTimeOffset.UtcNow;
        return retryAfter > TimeSpan.Zero;
    }

    public void RegisterFailure(string key)
    {
        var state = _cache.GetOrCreate(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = LockoutDuration;
            return new LoginAttemptState();
        });

        if (state is null)
        {
            return;
        }

        state.Failures += 1;

        if (state.Failures >= MaxFailures)
        {
            state.LockedUntil = DateTimeOffset.UtcNow.Add(LockoutDuration);
        }

        _cache.Set(key, state, state.LockedUntil ?? DateTimeOffset.UtcNow.Add(AttemptWindow));
    }

    public void Reset(string key)
    {
        _cache.Remove(key);
    }

    private sealed class LoginAttemptState
    {
        public int Failures { get; set; }
        public DateTimeOffset? LockedUntil { get; set; }
    }
}
