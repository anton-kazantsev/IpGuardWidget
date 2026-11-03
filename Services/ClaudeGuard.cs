using System.Diagnostics;
using System.Windows.Threading;

namespace IpGuard;

/// <summary>
/// Watches for Claude Code started outside the widget and stops it while the VPN is off.
/// Processes that were already running when the guard started are left alone.
/// </summary>
public sealed class ClaudeGuard : IDisposable
{
    private readonly Func<bool> _isBlocked;
    private readonly DispatcherTimer _timer;
    private readonly HashSet<int> _known = new();

    public ClaudeGuard(Func<bool> isBlocked)
    {
        _isBlocked = isBlocked;

        RememberRunning();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _timer.Tick += (_, _) => Poll();
        _timer.Start();
    }

    /// <summary>Raised when a launch was stopped, so the widget can show itself.</summary>
    public event EventHandler? BlockedLaunch;

    public void Dispose()
    {
        _timer.Stop();
    }

    /// <summary>
    /// PIDs seen at startup must not be killed: the user may have started them while the
    /// VPN was still on.
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
            var blocked = _isBlocked();
            var stopped = false;

            foreach (var process in Process.GetProcessesByName("claude"))
            {
                using (process)
                {
                    if (!_known.Add(process.Id) || !blocked)
                    {
                        continue;
                    }

                    if (!TryStop(process))
                    {
                        continue;
                    }

                    stopped = true;
                }
            }

            if (!stopped)
            {
                return;
            }

            Log.Warn("Claude launch blocked: VPN off");
        }
        catch (Exception ex)
        {
            Log.Warn("Claude guard: " + ex.Message);
            return;
        }

        // Raise outside the enumeration: the widget is about to call Show() and Activate().
        BlockedLaunch?.Invoke(this, EventArgs.Empty);
    }

    private static bool TryStop(Process process)
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
