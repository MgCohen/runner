using System.Collections.Concurrent;

namespace Notion.Infrastructure;

// Remembers in memory when a page last started work. One edit can send more than one event.
public sealed class PageCooldown(TimeProvider time) {
    readonly ConcurrentDictionary<string, DateTimeOffset> claims = new(StringComparer.Ordinal);

    // True, and records the time, if the key has no claim in the cooldown period.
    public bool TryClaim(string key, TimeSpan cooldown) {
        var now = time.GetUtcNow();
        while (true) {
            if (claims.TryGetValue(key, out var last)) {
                if (now - last < cooldown) {
                    return false;
                }
                if (claims.TryUpdate(key, now, last)) {
                    return true;
                }
            } else if (claims.TryAdd(key, now)) {
                return true;
            }
        }
    }

    // Deletes the claim, so that a retry can start work again.
    public void Release(string key) => claims.TryRemove(key, out _);
}
