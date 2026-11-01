using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace IpGuard;

public partial class App : Application
{
    private const string MutexName = @"Local\IpGuardWidget.SingleInstance";
    private const string ShowEventName = @"Local\IpGuardWidget.Show";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _showWait;
    private MainWindow? _window;

    public static SettingsStore Store { get; private set; } = null!;
    public static string? Simulate { get; private set; }
    public static bool StartHidden { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("Unhandled: " + (args.ExceptionObject as Exception)?.ToString());

        ParseArguments(e.Args);

        Store = new SettingsStore();
        Store.Load();

        NativeMethods.TryEnableDarkMenus();

        _mutex = new Mutex(true, MutexName, out bool isFirstInstance);
        if (!isFirstInstance)
        {
            // Bring the already running widget to front, then exit.
            try
            {
                if (EventWaitHandle.TryOpenExisting(ShowEventName, out var existing))
                {
                    existing.Set();
                    existing.Dispose();
                }
            }
            catch
            {
                // ignored - the other instance may be shutting down
            }

            Shutdown(0);
            return;
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _showWait = ThreadPool.RegisterWaitForSingleObject(
            _showEvent,
            (_, _) => Dispatcher.BeginInvoke(() => _window?.ShowWidget()),
            null,
            Timeout.Infinite,
            false);

        _window = new MainWindow(Store, Simulate);
        MainWindow = _window;

        if (!StartHidden)
        {
            _window.Show();
        }
        else
        {
            // Create the HWND so the tray icon and bindings work while hidden.
            new System.Windows.Interop.WindowInteropHelper(_window).EnsureHandle();
        }
    }

    private static void ParseArguments(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.Equals("--tray", StringComparison.OrdinalIgnoreCase))
            {
                StartHidden = true;
            }
            else if (arg.Equals("--simulate", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                Simulate = args[++i];
            }
            else if (arg.StartsWith("--simulate=", StringComparison.OrdinalIgnoreCase))
            {
                Simulate = arg["--simulate=".Length..];
            }
        }

        if (!string.IsNullOrWhiteSpace(Simulate))
        {
            Log.Info("Simulation mode: " + Simulate);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Dispatcher exception: " + e.Exception);
        // Keep the widget alive: a failed network call or a broken binding must not kill it.
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _showWait?.Unregister(null);
            _showEvent?.Dispose();
            if (_mutex is not null)
            {
                try { _mutex.ReleaseMutex(); } catch { }
                _mutex.Dispose();
            }
        }
        catch
        {
            // ignored
        }

        base.OnExit(e);
    }
}
