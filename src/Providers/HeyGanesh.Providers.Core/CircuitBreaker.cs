namespace HeyGanesh.Providers.Core;

/// <summary>
/// Deterministic per-provider circuit breaker. State transitions are explicit and
/// testable via an injectable clock.
/// </summary>
public sealed class CircuitBreaker
{
    public enum State { Closed, Open, HalfOpen }

    private readonly int _failureThreshold;
    private readonly TimeSpan _recoveryTimeout;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _lock = new();

    private State _state = State.Closed;
    private int _consecutiveFailures;
    private DateTimeOffset _openedAt;

    /// <summary>Current state (thread-safe read).</summary>
    public State CurrentState
    {
        get
        {
            lock (_lock)
            {
                TransitionIfNeeded();
                return _state;
            }
        }
    }

    /// <summary>Number of consecutive failures recorded (while closed or half-open).</summary>
    public int ConsecutiveFailures
    {
        get { lock (_lock) { return _consecutiveFailures; } }
    }

    public CircuitBreaker(int failureThreshold = 5, TimeSpan? recoveryTimeout = null, Func<DateTimeOffset>? clock = null)
    {
        if (failureThreshold < 1) throw new ArgumentOutOfRangeException(nameof(failureThreshold), "Must be >= 1.");
        _failureThreshold = failureThreshold;
        _recoveryTimeout = recoveryTimeout ?? TimeSpan.FromSeconds(30);
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Returns true when a call is allowed to proceed. In Open state, returns false
    /// until the recovery timeout has elapsed, at which point transitions to HalfOpen
    /// and allows one probe call.
    /// </summary>
    public bool IsAllowed()
    {
        lock (_lock)
        {
            TransitionIfNeeded();
            return _state != State.Open;
        }
    }

    /// <summary>Records a successful call. Resets failure count and closes the circuit.</summary>
    public void RecordSuccess()
    {
        lock (_lock)
        {
            _consecutiveFailures = 0;
            _state = State.Closed;
        }
    }

    /// <summary>
    /// Records a failed call. Increments the consecutive failure count and opens the
    /// circuit when the threshold is reached.
    /// </summary>
    public void RecordFailure()
    {
        lock (_lock)
        {
            _consecutiveFailures++;
            if (_state == State.HalfOpen)
            {
                // Probe failed — reopen.
                _state = State.Open;
                _openedAt = _clock();
            }
            else if (_consecutiveFailures >= _failureThreshold)
            {
                _state = State.Open;
                _openedAt = _clock();
            }
        }
    }

    /// <summary>Manually resets the circuit breaker to Closed.</summary>
    public void Reset()
    {
        lock (_lock)
        {
            _state = State.Closed;
            _consecutiveFailures = 0;
        }
    }

    private void TransitionIfNeeded()
    {
        if (_state == State.Open)
        {
            var elapsed = _clock() - _openedAt;
            if (elapsed >= _recoveryTimeout)
            {
                _state = State.HalfOpen;
                _consecutiveFailures = 0;
            }
        }
    }
}