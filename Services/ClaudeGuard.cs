using System.Diagnostics;
using System.Windows.Threading;

namespace IpGuard;

public enum ClaudeGuardReason
{
    VpnLost,
    NetworkLost
}

public sealed class ClaudeGuardEventArgs : EventArgs
{
    public ClaudeGuardEventArgs(ClaudeGuardReason reason) => Reason = reason;

    public ClaudeGuardReason Reason { get; }
}

/// <summary>
/// Watches Claude Code and blocks new launches while the VPN is off or the network is gone.
/// A running Claude Code is never stopped: the state change only raises a one-off warning,
/// so the session keeps working and the widget can explain what happened.
/// </summary>
public sealed class ClaudeGuard : IDisposable
{
    private readonly Func<WidgetState> _state;
    private readonly Func<bool> _connectionLost;
    private readonly DispatcherTimer _timer;
    private readonly HashSet<int> _known = new();

    private bool _sawAllowed;
    private bool _blocked;

    public ClaudeGuard(Func<WidgetState> state, Func<bool> connectionLost)
    {
        _state = state;
        _connectionLost = connectionLost;

        RememberRunning();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _timer.Tick += (_, _) => Poll();
        _timer.Start();
    }

    /// <summary>Raised when a launch was stopped, so the widget can show itself.</summary>
    public event EventHandler? BlockedLaunch;

    /// <summary>
    /// Raised once per Allowed → Blocked/Offline transition while at least one `claude` process
    /// is running, so the widget can show itself and warn that the work goes on unprotected.
    /// </summary>
    public event EventHandler<ClaudeGuardEventArgs>? UnsafeDetected;

    /// <summary>True when a `claude` process is running. A failed query counts as "not running".</summary>
    public static bool IsClaudeRunning()
    {
        try
        {
            var processes = Process.GetProcessesByName("claude");
            foreach (var process in processes)
            {
                process.Dispose();
            }

            return processes.Length > 0;
        }
        catch (Exception ex)
        {
            Log.Warn("Claude guard: " + ex.Message);
            return false;
        }
    }

    public void Dispose()
    {
        _timer.Stop();
    }

    /// <summary>
    /// PIDs seen at startup must not be killed: the user may have started them while the
    /// VPN was still on, and a launch that is already running is never stopped by the guard.
    /// </summary>
    private void RememberRunning()
    {
        try
        {
            foreach (var process in Process.GetProcessesByName("claude"))
            {
                using (process)
                {
                    _known.Add(process.Id);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Claude guard: " + ex.Message);
        }
    }

    private void Poll()
    {
        try
        {
            var state = _state();

            // The card keeps the last known IP while the network is gone, so State may still be
            // Allowed: the connection flag is the only sign that the network was lost.
            var networkLost = _connectionLost() || state == WidgetState.Offline;
            var blocked = networkLost || state == WidgetState.Blocked;

            // A state that was already blocked when the app started is not a transition: the
            // user could have launched Claude on purpose with the VPN off.
            var transition = blocked && _sawAllowed && !_blocked;
            var reason = networkLost ? ClaudeGuardReason.NetworkLost : ClaudeGuardReason.VpnLost;

            var running = false;
            var stoppedNew = false;

            var processes = Process.GetProcessesByName("claude");

            // A PID of a dead process must not linger in _known: Windows reuses PIDs, and a fresh
            // `claude` under such a PID would look like a known one and would be spared.
            var alive = new HashSet<int>();
            foreach (var process in processes)
            {
                alive.Add(process.Id);
            }

            _known.IntersectWith(alive);

            foreach (var process in processes)
            {
                using (process)
                {
                    var isNew = _known.Add(process.Id);
                    running = true;

                    // A process that is already running is never stopped, not even on the
                    // transition: the work continues, the widget only shows a warning.
                    if (!blocked || transition || !isNew)
                    {
                        continue;
                    }

                    if (!TryStopHard(process))
                    {
                        continue;
                    }

                    _known.Remove(process.Id);
                    stoppedNew = true;
                }
            }

            _blocked = blocked;
            if (state == WidgetState.Allowed)
            {
                _sawAllowed = true;
            }

            if (transition && running)
            {
                Log.Info("Claude Code is running without VPN/network, warning shown");

                // Raise outside the enumeration: the widget is about to call Show() and Activate().
                UnsafeDetected?.Invoke(this, new ClaudeGuardEventArgs(reason));
            }

            if (stoppedNew)
            {
                Log.Warn("Claude launch blocked: VPN off");
                BlockedLaunch?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Claude guard: " + ex.Message);
        }
    }

    private static bool TryStopHard(Process process)
    {
        try
        {
            process.Kill(true);
            process.WaitForExit(1000);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"Claude guard: kill {process.Id} failed: {ex.Message}");
            return false;
        }
    }
}
