using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace IpGuard;

public partial class MainWindow : Window
{
    private const double WindowPadding = 16;
    private const double CardNormalHeight = 277;
    private const double CardNoticeHeight = 340;
    private const double NoticeHostHeight = 63;

    private readonly SettingsStore _store;
    private readonly WidgetViewModel _viewModel;
    private readonly LaunchService _launcher = new();
    private readonly DispatcherTimer _saveTimer;
    private TrayIcon? _tray;
    private bool _exiting;
    private double _targetRight;
    private double _targetBottom;

    public MainWindow(SettingsStore store, string? simulate)
    {
        InitializeComponent();

        _store = store;
        _viewModel = new WidgetViewModel(new IpCheckService(), simulate);
        DataContext = _viewModel;

        Topmost = store.Current.AlwaysOnTop;
        RestorePosition();
        Log.Info($"Restore: {Left:F0},{Top:F0} ({Width:F0}x{Height:F0})");

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SavePlacement();
        };
        LocationChanged += (_, _) =>
        {
            _saveTimer.Stop();
            _saveTimer.Start();
        };

        _viewModel.LayoutChanged += (_, _) => ApplyLayout(animate: true);
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;

        // Windows may nudge a borderless window after it is shown (multi-monitor setups,
        // monitor hot-plug). Re-check the real position once the first layout is done.
        Loaded += (_, _) => EnsureOnScreen();
    }

    private void EnsureOnScreen()
    {
        ApplyTargetPhysical();

        var rect = GetPhysicalWindowRect();
        if (!Monitors.IsPhysicalRectVisible(rect))
        {
            Log.Warn("Window is off-screen after placement, resetting to the primary monitor");
            MoveToDefaultPosition();
            ApplyTargetPhysical();
            SavePlacement();
        }
    }

    private void LogHwnd(string tag)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        if (NativeMethods.GetWindowRect(hwnd, out var rect))
        {
            Log.Info($"{tag}: hwnd={rect.Left},{rect.Top} {rect.Right - rect.Left}x{rect.Bottom - rect.Top}, wpf={Left:F0},{Top:F0} {Width:F0}x{Height:F0}, dpi={VisualTreeHelper.GetDpi(this).DpiScaleX:F2}");
        }
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        _tray = new TrayIcon(handle);
        _tray.LeftClick += ToggleVisibility;
        _tray.MenuRequested += ShowTrayMenu;
        _tray.CommandSelected += OnTrayCommand;
        _tray.SetTooltip(_viewModel.TooltipText);

        EnsureOnScreen();

        _viewModel.Start();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WidgetViewModel.TooltipText))
        {
            _tray?.SetTooltip(_viewModel.TooltipText);
        }
    }

    public void ShowWidget()
    {
        if (!IsVisible)
        {
            Show();
        }

        EnsureOnScreen();

        Topmost = _store.Current.AlwaysOnTop;
        var handle = new WindowInteropHelper(this).Handle;
        NativeMethods.SetForegroundWindow(handle);
        Activate();
    }

    private void ToggleVisibility()
    {
        if (IsVisible)
        {
            Hide();
            SavePlacement();
        }
        else
        {
            ShowWidget();
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_exiting)
        {
            return;
        }

        e.Cancel = true;
        Hide();
        SavePlacement();
    }

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
            SavePlacement();
        }
    }

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || IsFromButton(e.OriginalSource as DependencyObject))
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // the mouse was released before the drag started
        }
    }

    private void Root_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ShowTrayMenu();
    }

    private static bool IsFromButton(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is ButtonBase)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private async void RunButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.State != WidgetState.Allowed)
        {
            return;
        }

        _viewModel.ClearNotice();

        // The guard must not rely on a stale result: verify right before launching.
        if (_viewModel.LastChecked is null ||
            DateTime.Now - _viewModel.LastChecked > TimeSpan.FromSeconds(45))
        {
            await _viewModel.CheckAsync(showChecking: false);
            if (_viewModel.State == WidgetState.Blocked)
            {
                return;
            }
        }

        var outcome = _launcher.Launch(_store.Current);
        if (!outcome.Ok)
        {
            _viewModel.SetErrorNotice(
                outcome.Message ?? "Не удалось запустить",
                outcome.Detail ?? "Проверь настройки в меню трея");
            Log.Warn($"Launch problem: {outcome.Message} — {outcome.Detail}");
        }
    }

    private async void RefreshLink_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.RefreshAsync();
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_viewModel.IpAddress);
            _viewModel.ShowCopiedHint();
        }
        catch (Exception ex)
        {
            Log.Warn("Clipboard: " + ex.Message);
        }
    }

    private void ShowTrayMenu()
    {
        var settings = _store.Current;
        var shell = (settings.Shell ?? "cmd").ToLowerInvariant();

        var items = new List<TrayMenuItem>
        {
            new(1, IsVisible ? "Скрыть" : "Показать", IsDefault: true),
            new(2, "Обновить IP"),
            new(0, "", IsSeparator: true),
            new(3, "Всегда сверху", Checked: Topmost),
            new(4, "Автозапуск", Checked: StartupService.IsEnabled()),
            new(0, "Оболочка", Submenu: new List<TrayMenuItem>
            {
                new(10, "cmd — вкладка остаётся", Checked: shell == "cmd"),
                new(11, "PowerShell", Checked: shell == "pwsh"),
                new(12, "Напрямую claude.exe", Checked: shell == "direct")
            }),
            new(5, "Папка запуска…"),
            new(7, "Сбросить позицию"),
            new(0, "", IsSeparator: true),
            new(6, "Выход")
        };

        _tray?.ShowMenu(items);
    }

    private void OnTrayCommand(int id)
    {
        switch (id)
        {
            case 1:
                ToggleVisibility();
                break;
            case 2:
                _ = _viewModel.RefreshAsync();
                break;
            case 3:
                Topmost = !Topmost;
                _store.Current.AlwaysOnTop = Topmost;
                _store.Save();
                break;
            case 4:
                StartupService.SetEnabled(!StartupService.IsEnabled());
                break;
            case 5:
                PickWorkingDirectory();
                break;
            case 7:
                MoveToDefaultPosition();
                ApplyTargetPhysical();
                SavePlacement();
                break;
            case 6:
                ExitApplication();
                break;
            case 10:
                SetShell("cmd");
                break;
            case 11:
                SetShell("pwsh");
                break;
            case 12:
                SetShell("direct");
                break;
        }
    }

    private void SetShell(string shell)
    {
        _store.Current.Shell = shell;
        _store.Save();
    }

    private void PickWorkingDirectory()
    {
        try
        {
            var current = _store.Current.WorkingDirectory;
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Папка для запуска Claude Code",
                InitialDirectory = Directory.Exists(current)
                    ? current
                    : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            };

            if (dialog.ShowDialog(this) == true)
            {
                _store.Current.WorkingDirectory = dialog.FolderName;
                _store.Save();
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Folder dialog: " + ex.Message);
        }
    }

    private void ExitApplication()
    {
        _exiting = true;
        SavePlacement();
        _viewModel.Dispose();
        _tray?.Dispose();
        _tray = null;
        Application.Current.Shutdown();
    }

    private void RestorePosition()
    {
        var settings = _store.Current;

        if (settings.AnchorRight is { } right && settings.AnchorBottom is { } bottom)
        {
            var scale = Monitors.ScaleAt(right - 1, bottom - 1);
            var candidate = new Rect(
                right - Width * scale,
                bottom - Height * scale,
                Width * scale,
                Height * scale);

            if (Monitors.IsPhysicalRectVisible(candidate))
            {
                SetTarget(right, bottom);
                Log.Info($"Restore: anchor=({right},{bottom}) scale={scale:F2}");
                return;
            }

            Log.Warn($"Restore: anchor=({right},{bottom}) is off-screen, resetting");
        }

        MoveToDefaultPosition();
    }

    private void SetTarget(double right, double bottom)
    {
        _targetRight = right;
        _targetBottom = bottom;

        // Preliminary WPF position; the exact placement happens through SetWindowPos
        // once the HWND exists (it is the only way to be DPI-correct on mixed setups).
        var scale = Monitors.ScaleAt(right - 1, bottom - 1);
        Left = (right - Width * scale) / scale;
        Top = (bottom - Height * scale) / scale;
    }

    private void MoveToDefaultPosition()
    {
        var work = Monitors.PrimaryWorkArea;
        SetTarget(work.Right - 8, work.Bottom - 8);
        Log.Info($"Position target reset to physical ({_targetRight:F0},{_targetBottom:F0})");
    }

    /// <summary>Moves the real HWND to the target bottom-right corner in physical pixels.</summary>
    private void ApplyTargetPhysical()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var scale = Monitors.ScaleAt(_targetRight - 1, _targetBottom - 1);
        var x = (int)Math.Round(_targetRight - Width * scale);
        var y = (int)Math.Round(_targetBottom - Height * scale);

        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);

        LogHwnd("placed");
    }

    private Rect GetPhysicalWindowRect()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero && NativeMethods.GetWindowRect(hwnd, out var rect))
        {
            return new Rect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        }

        var scale = Monitors.ScaleAt(_targetRight - 1, _targetBottom - 1);
        return new Rect(_targetRight - Width * scale, _targetBottom - Height * scale,
            Width * scale, Height * scale);
    }

    private void SavePlacement()
    {
        // Store the physical bottom-right corner of the real window. Window.Left/Top are
        // DIPs and depend on the DPI context Windows gave the window.
        var rect = GetPhysicalWindowRect();
        _store.Current.AnchorRight = rect.Right;
        _store.Current.AnchorBottom = rect.Bottom;
        _store.Current.AlwaysOnTop = Topmost;
        _store.Save();
    }

    private void ApplyLayout(bool animate)
    {
        var cardHeight = _viewModel.ShowNotice ? CardNoticeHeight : CardNormalHeight;
        var windowHeight = cardHeight + WindowPadding * 2;
        var noticeHeight = _viewModel.ShowNotice ? NoticeHostHeight : 0;

        // Anchor the card's bottom edge: the card grows upwards when the notice appears,
        // so the saved bottom-right position stays put across restarts.
        var targetTop = Top + Height - windowHeight;
        targetTop = Monitors.ClampToWorkArea(Left, targetTop, Width, windowHeight).Top;

        if (!animate)
        {
            Card.Height = cardHeight;
            ShadowLayer.Height = cardHeight;
            NoticeHost.Height = noticeHeight;
            Height = windowHeight;
            Top = targetTop;
            return;
        }

        var fromCard = Card.Height;
        var fromShadow = ShadowLayer.Height;
        var fromNotice = NoticeHost.Height;
        var fromWindow = Height;
        var fromTop = Top;

        Log.Info($"Layout: card {fromCard:F0}->{cardHeight:F0}, window {fromWindow:F0}->{windowHeight:F0}, top {fromTop:F0}->{targetTop:F0}");

        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var duration = TimeSpan.FromMilliseconds(260);

        Animate(Card, FrameworkElement.HeightProperty, fromCard, cardHeight, duration, ease);
        Animate(ShadowLayer, FrameworkElement.HeightProperty, fromShadow, cardHeight, duration, ease);
        Animate(NoticeHost, FrameworkElement.HeightProperty, fromNotice, noticeHeight, duration, ease);
        Animate(this, Window.HeightProperty, fromWindow, windowHeight, duration, ease);

        if (Math.Abs(fromTop - targetTop) > 0.5)
        {
            Animate(this, Window.TopProperty, fromTop, targetTop, duration, ease);
        }
    }

    private static void Animate(DependencyObject target, DependencyProperty property,
        double from, double to, TimeSpan duration, IEasingFunction ease)
    {
        if (target is not UIElement element || Math.Abs(from - to) < 0.01)
        {
            target.SetValue(property, to);
            return;
        }

        var animation = new DoubleAnimation(from, to, duration)
        {
            EasingFunction = ease,
            FillBehavior = FillBehavior.HoldEnd
        };

        animation.Completed += (_, _) =>
        {
            element.SetValue(property, to);
            element.BeginAnimation(property, null);
        };

        element.BeginAnimation(property, animation);
    }
}
