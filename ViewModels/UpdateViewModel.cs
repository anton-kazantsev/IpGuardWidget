using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Threading;

namespace IpGuard;

public enum UpdateState
{
    Hidden,
    Available,
    Downloading,
    Ready,
    Error
}

/// <summary>
/// State of the update banner and the "what's new" panel. The card itself keeps
/// <see cref="WidgetViewModel"/> as its DataContext; the banner and the panel get this one.
/// </summary>
public sealed class UpdateViewModel : ObservableObject, IDisposable
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    /// <summary>Shorter than this the average speed is still noise: no ETA in the subtitle yet.</summary>
    private static readonly TimeSpan MinEtaElapsed = TimeSpan.FromSeconds(0.5);

    private readonly SettingsStore _store;
    private readonly WidgetViewModel _widget;
    private readonly UpdateService _service = new();
    private readonly DispatcherTimer _firstCheck;
    private readonly DispatcherTimer _timer;

    private CancellationTokenSource? _cts;
    private UpdateInfo? _info;
    private string? _downloadedPath;

    private UpdateState _state = UpdateState.Hidden;
    private bool _showBanner;
    private bool _showWhatsNew;
    private bool _isChecking;
    private string _updateTitle = "";
    private string _updateSubtitle = "";
    private double _progressValue;
    private bool _showProgress;
    private string _whatsNewTitle = "";
    private string _whatsNewDate = "";
    private string _releaseUrl = "";

    public UpdateViewModel(SettingsStore store, WidgetViewModel widget)
    {
        _store = store;
        _widget = widget;

        _firstCheck = new DispatcherTimer { Interval = FirstCheckDelay };
        _firstCheck.Tick += async (_, _) =>
        {
            _firstCheck.Stop();
            await CheckAsync(silent: true);
        };

        _timer = new DispatcherTimer { Interval = CheckInterval };
        _timer.Tick += async (_, _) => await CheckAsync(silent: true);
    }

    /// <summary>Raised when ShowBanner / ShowWhatsNew change: the window re-measures itself.</summary>
    public event EventHandler? LayoutChanged;

    /// <summary>Raised when the new exe is in place and the app should exit and be restarted.</summary>
    public event EventHandler? RestartRequested;

    public ObservableCollection<string> Changes { get; } = new();

    public bool ShowBanner
    {
        get => _showBanner;
        private set
        {
            if (SetProperty(ref _showBanner, value))
            {
                LayoutChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public bool ShowWhatsNew
    {
        get => _showWhatsNew;
        private set
        {
            if (SetProperty(ref _showWhatsNew, value))
            {
                LayoutChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public bool IsChecking { get => _isChecking; private set => SetProperty(ref _isChecking, value); }

    /// <summary>Banner state: the XAML switches icons, buttons and colours on it.</summary>
    public UpdateState State => _state;

    public string UpdateTitle { get => _updateTitle; private set => SetProperty(ref _updateTitle, value); }
    public string UpdateSubtitle { get => _updateSubtitle; private set => SetProperty(ref _updateSubtitle, value); }
    public double ProgressValue { get => _progressValue; private set => SetProperty(ref _progressValue, value); }
    public bool ShowProgress { get => _showProgress; private set => SetProperty(ref _showProgress, value); }
    public string WhatsNewTitle { get => _whatsNewTitle; private set => SetProperty(ref _whatsNewTitle, value); }
    public string WhatsNewDate { get => _whatsNewDate; private set => SetProperty(ref _whatsNewDate, value); }
    public string ReleaseUrl { get => _releaseUrl; private set => SetProperty(ref _releaseUrl, value); }

    /// <summary>True when an update is known: the footer check button is highlighted in accent.</summary>
    public bool HasUpdate => _state is UpdateState.Available or UpdateState.Downloading or UpdateState.Ready;

    /// <summary>First check 3 s after start, then every 6 hours.</summary>
    public void Initialize()
    {
        _firstCheck.Stop();
        _firstCheck.Start();
        _timer.Stop();
        _timer.Start();
    }

    public async Task CheckAsync(bool silent)
    {
        if (_isChecking)
        {
            return;
        }

        IsChecking = true;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var result = await _service.CheckAsync(timeout.Token);

            switch (result.Status)
            {
                case UpdateCheckStatus.UpdateAvailable when result.Info is { } info:
                    Apply(info);
                    break;
                case UpdateCheckStatus.UpToDate:
                    if (!silent)
                    {
                        _widget.FlashFooter($"Обновлений нет · v{UpdateService.ShortVersion(UpdateService.CurrentVersion)}");
                    }

                    break;
                // Failed: the service already logged the reason. Never a banner, never a warning
                // inside the card — an offline GitHub is not the user's problem.
            }
        }
        finally
        {
            IsChecking = false;
        }
    }

    /// <summary>«Скачать» / «Отмена» / «Перезапустить» / «Повторить» в баннере и в панели.</summary>
    public async Task PrimaryActionAsync()
    {
        switch (_state)
        {
            case UpdateState.Downloading:
                _cts?.Cancel();
                break;
            case UpdateState.Ready:
                Restart();
                break;
            case UpdateState.Error when _downloadedPath is { } path && File.Exists(path):
                Restart();
                break;
            default:
                await StartDownloadAsync();
                break;
        }
    }

    public void ToggleWhatsNew()
    {
        if (!ShowBanner)
        {
            return;
        }

        ShowWhatsNew = !ShowWhatsNew;
    }

    public void HideWhatsNew() => ShowWhatsNew = false;

    public void Postpone()
    {
        if (_state == UpdateState.Downloading)
        {
            return;
        }

        if (_info is { } info)
        {
            _store.Current.PostponedUpdateTag = info.Tag;
            _store.Save();
            Log.Info($"Update: {info.Tag} postponed until the next version");
        }

        HideWhatsNew();
        ShowBanner = false;
        SetState(UpdateState.Hidden);
    }

    public void Restart()
    {
        if (_downloadedPath is null || !File.Exists(_downloadedPath))
        {
            SetError("Не удалось обновить приложение", "Файл обновления не найден — скачай заново");
            return;
        }

        if (SelfUpdate.TryApplyAndRestart(_downloadedPath, out var error))
        {
            RestartRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        SetError("Не удалось обновить приложение", error ?? "Попробуй ещё раз");
    }

    private async Task StartDownloadAsync()
    {
        if (_info is not { } info || _state == UpdateState.Downloading)
        {
            return;
        }

        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        _downloadedPath = null;
        ProgressValue = 0;
        UpdateTitle = "Скачивание 0 %";
        UpdateSubtitle = DownloadSubtitle(0, info.SizeBytes, TimeSpan.Zero);
        ShowProgress = true;
        SetState(UpdateState.Downloading);

        var progress = new Progress<DownloadProgress>(p =>
        {
            ProgressValue = p.Percent;
            UpdateTitle = $"Скачивание {p.Percent:0} %";
            UpdateSubtitle = DownloadSubtitle(p.ReceivedBytes, p.TotalBytes, p.Elapsed);
        });

        try
        {
            _downloadedPath = await _service.DownloadAsync(info, progress, _cts.Token);
            ProgressValue = 100;
            ShowProgress = false;
            UpdateTitle = "Обновление скачано";
            UpdateSubtitle = "Готово к перезапуску · " + UpdateService.FormatSize(info.SizeBytes);
            SetState(UpdateState.Ready);
        }
        catch (OperationCanceledException)
        {
            ProgressValue = 0;
            ShowProgress = false;
            UpdateTitle = AvailableTitle(info);
            UpdateSubtitle = BuildSubtitle(info);
            SetState(UpdateState.Available);
        }
        catch (Exception ex)
        {
            ProgressValue = 0;
            SetError("Не удалось скачать обновление", "Проверьте сеть и повторите");
            Log.Warn("Update: download failed in UI: " + ex.Message);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void Apply(UpdateInfo info)
    {
        // A download in progress (or an already downloaded file) must not be reset by a
        // background re-check that happens to run at the same time.
        if (_state is UpdateState.Downloading or UpdateState.Ready)
        {
            return;
        }

        _info = info;
        _downloadedPath = null;

        Changes.Clear();
        foreach (var line in info.Changes)
        {
            Changes.Add(line);
        }

        UpdateTitle = AvailableTitle(info);
        UpdateSubtitle = BuildSubtitle(info);
        WhatsNewTitle = $"Что нового в {UpdateService.FormatVersion(info.Version)}";
        WhatsNewDate = info.PublishedAt.ToString("d MMMM yyyy", Ru);
        ReleaseUrl = info.ReleaseUrl;
        ShowProgress = false;
        ProgressValue = 0;
        SetState(UpdateState.Available);

        // A postponed tag stays hidden — the check itself still reports the update.
        var postponed = string.Equals(_store.Current.PostponedUpdateTag, info.Tag, StringComparison.OrdinalIgnoreCase);
        HideWhatsNew();
        ShowBanner = !postponed;
    }

    private void SetError(string title, string subtitle)
    {
        ShowProgress = false;
        UpdateTitle = title;
        UpdateSubtitle = subtitle;
        SetState(UpdateState.Error);
        Log.Warn($"Update: {title} — {subtitle}");
    }

    private void SetState(UpdateState state)
    {
        _state = state;
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(HasUpdate));
    }

    private static string AvailableTitle(UpdateInfo info)
        => $"Доступно обновление {UpdateService.FormatVersion(info.Version)}";

    private static string BuildSubtitle(UpdateInfo info)
        => $"{RelativeDate(info.PublishedAt)} · {UpdateService.FormatSize(info.SizeBytes)}";

    /// <summary>
    /// «6,2 из 59,0 МБ · осталось 8 с» — how much of the release is already on disk and, once the
    /// average speed means anything, what is left to wait.
    /// </summary>
    private string DownloadSubtitle(long receivedBytes, long totalBytes, TimeSpan elapsed)
    {
        if (_info is null || totalBytes <= 0)
        {
            return "";
        }

        var text = $"{Megabytes(receivedBytes)} из {UpdateService.FormatSize(totalBytes)}";
        if (receivedBytes <= 0 || receivedBytes >= totalBytes || elapsed < MinEtaElapsed)
        {
            return text;
        }

        // Average speed since the download started, not the last chunk: it stays readable
        // on a link that stutters and needs no per-report history.
        var speed = receivedBytes / elapsed.TotalSeconds;
        if (speed < 1)
        {
            return text;
        }

        var remaining = (totalBytes - receivedBytes) / speed;
        if (remaining > 60)
        {
            return text + " · осталось больше минуты";
        }

        return text + $" · осталось {Math.Max(1, (int)Math.Ceiling(remaining))} с";
    }

    /// <summary>«6,2» — megabytes with one decimal: the unit is written once, after the total.</summary>
    private static string Megabytes(long bytes) => (bytes / 1024d / 1024d).ToString("0.0", Ru);

    private static string RelativeDate(DateTime published)
    {
        var days = (DateTime.Today - published.Date).Days;
        return days switch
        {
            <= 0 => "Вышло сегодня",
            1 => "Вышло вчера",
            <= 30 => $"Вышло {days} {DaysWord(days)} назад",
            _ => "Вышло " + published.ToString("d MMMM yyyy", Ru)
        };
    }

    private static string DaysWord(int days)
    {
        var tail = days % 100;
        if (tail is >= 11 and <= 14)
        {
            return "дней";
        }

        return (days % 10) switch
        {
            1 => "день",
            2 or 3 or 4 => "дня",
            _ => "дней"
        };
    }

    public void Dispose()
    {
        _firstCheck.Stop();
        _timer.Stop();
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }
}
