namespace MusicWebsite.Infrastructure.Media;

/// <summary>
/// Stops a dead lyrics source from taxing every upload.
///
/// Without this, an outage at one source costs its full timeout on every single lookup before the
/// next source is even tried — measured at ~5 seconds per upload during an LRCLIB outage, for a
/// service that was never going to answer. After a few consecutive failures the source is skipped
/// outright for a cooldown, then probed once; one success re-opens it fully. Nothing has to be
/// configured or redeployed either way — it closes when the service dies and reopens when it
/// recovers.
///
/// Registered as a singleton so the state is shared across requests, which is the whole point:
/// request number two should benefit from what request number one discovered.
/// </summary>
public class LyricsSourceCircuitBreaker
{
    /// <summary>Consecutive failures before a source is considered down.</summary>
    private const int FailureThreshold = 3;

    /// <summary>How long to skip a source before probing it again.</summary>
    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(10);

    private readonly Dictionary<string, State> _states = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    /// <summary>True when the source should be skipped entirely right now.</summary>
    public bool IsTripped(string source)
    {
        lock (_gate)
        {
            if (!_states.TryGetValue(source, out var state)) return false;
            if (state.Failures < FailureThreshold) return false;

            if (DateTime.UtcNow >= state.RetryAt)
            {
                // Half-open: let exactly one request through to see whether it recovered. Leaving
                // the count one below the threshold means a single further failure re-trips it
                // immediately rather than costing another three timeouts.
                state.Failures = FailureThreshold - 1;
                return false;
            }

            return true;
        }
    }

    public void RecordSuccess(string source)
    {
        lock (_gate) _states.Remove(source);
    }

    public void RecordFailure(string source)
    {
        lock (_gate)
        {
            if (!_states.TryGetValue(source, out var state))
                _states[source] = state = new State();

            state.Failures++;
            if (state.Failures >= FailureThreshold)
                state.RetryAt = DateTime.UtcNow.Add(Cooldown);
        }
    }

    private sealed class State
    {
        public int Failures;
        public DateTime RetryAt;
    }
}
