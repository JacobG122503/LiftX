using Microsoft.JSInterop;

namespace LiftX.Services;

/// <summary>
/// Rest countdown. Based on an end timestamp, so it stays right even if iOS pauses the app
/// while the phone is locked. Only the timer widgets subscribe to Tick, so typing in the
/// lift screen isn't re-rendered four times a second.
/// </summary>
public class RestTimer
{
    private readonly AppState _s;
    private readonly IJSInProcessRuntime _js;
    private bool _loopRunning;

    public RestTimer(AppState s, IJSRuntime js)
    {
        _s = s;
        _js = (IJSInProcessRuntime)js;
        if (s.Meta.RestEndsUtc is { } ends)
        {
            if (ends > DateTime.UtcNow)
            {
                EndsUtc = ends;
                Total = Math.Max(1, s.Meta.RestTotal);
                EnsureLoop();
            }
            else if (s.Active != null) FinishedUtc = ends; // ran out while the app was closed
        }
    }

    public DateTime? EndsUtc { get; private set; }
    public int Total { get; private set; }
    public DateTime? FinishedUtc { get; private set; }

    public bool Running => EndsUtc != null;
    public double RemainingExact => EndsUtc is { } e ? Math.Max(0, (e - DateTime.UtcNow).TotalSeconds) : 0;
    public int Remaining => (int)Math.Ceiling(RemainingExact);
    public double Progress => Total > 0 ? Math.Clamp(RemainingExact / Total, 0, 1) : 0;
    /// <summary>Rest ran out and the next set hasn't been checked yet (shows "Done" in the top bar).</summary>
    public bool ShowingDone => FinishedUtc != null && !Running;

    public event Action? Tick;

    public void Start(int seconds)
    {
        Total = Math.Max(1, seconds);
        EndsUtc = DateTime.UtcNow.AddSeconds(Total);
        FinishedUtc = null;
        Persist();
        EnsureLoop();
        Tick?.Invoke();
    }

    public void Add(int delta)
    {
        if (EndsUtc is not { } e) return;
        EndsUtc = e.AddSeconds(delta);
        Total = Math.Max(1, Total + delta);
        if (RemainingExact <= 0) { Stop(); return; }
        Persist();
        Tick?.Invoke();
    }

    public void Stop()
    {
        EndsUtc = null;
        FinishedUtc = null;
        Persist();
        Tick?.Invoke();
    }

    private void Persist()
    {
        _s.Meta.RestEndsUtc = EndsUtc;
        _s.Meta.RestTotal = Total;
        _s.SaveMeta();
    }

    private void EnsureLoop()
    {
        if (_loopRunning) return;
        _loopRunning = true;
        _ = Loop();
    }

    private async Task Loop()
    {
        try
        {
            while (Running)
            {
                await Task.Delay(250);
                if (EndsUtc is { } ends && DateTime.UtcNow >= ends)
                {
                    var overdue = DateTime.UtcNow - ends;
                    EndsUtc = null;
                    FinishedUtc = DateTime.UtcNow;
                    Persist();
                    // Don't blast a beep if the timer ran out ages ago while the phone was locked
                    if (overdue.TotalSeconds < 5 && _s.Data.Settings.TimerSound)
                    {
                        try { _js.InvokeVoid("liftx.beep"); } catch { }
                    }
                }
                Tick?.Invoke();
            }
        }
        finally
        {
            _loopRunning = false;
        }
    }
}
