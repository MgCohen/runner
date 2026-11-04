using System.Collections.Concurrent;

namespace Runner.Handlers;

/// <summary>Remembers in memory when a page last started a session. One instance for each process.</summary>
public sealed class PageCooldown(TimeProvider time)
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _claims = new(StringComparer.Ordinal);

    /// <summary>Returns true and records the time, if the page has no claim in the cooldown period.</summary>
    public bool TryClaim(string pageId, TimeSpan cooldown)
    {
        var now = time.GetUtcNow();
        while (true)
        {
            if (_claims.TryGetValue(pageId, out var last))
            {
                if (now - last < cooldown)
                {
                    return false;
                }
                if (_claims.TryUpdate(pageId, now, last))
                {
                    return true;
                }
            }
            else if (_claims.TryAdd(pageId, now))
            {
                return true;
            }
        }
    }

    /// <summary>Deletes the claim, so that a retry of a failed attempt can start a session.</summary>
    public void Release(string pageId) => _claims.TryRemove(pageId, out _);
}
