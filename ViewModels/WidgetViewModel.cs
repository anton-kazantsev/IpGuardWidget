using System.ComponentModel;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Threading;

namespace IpGuard;

public enum WidgetState
{
    Checking,
    Allowed,
    Blocked,
    Offline
}

public sealed class WidgetViewModel : ObservableObject, IDisposable
{
    private static readonly Brush SuccessPillBackground = Frozen("#1F34C759");
    private static readonly Brush SuccessPillText = Frozen("#4ADE80");
    private static readonly Brush SuccessDot = Frozen("#34C759");
    private static readonly Brush WarningPillBackground = Frozen("#1FFF9F0A");
    private static readonly Brush WarningPillText = Frozen("#FFC24D");
    private static readonly Brush WarningDot = Frozen("#FF9F0A");
    private static readonly Brush NeutralPillBackground = Frozen("#0FFFFFFF");
    private static readonly Brush NeutralPillText = Frozen("#A7A7B0");
    private static readonly Brush NeutralDot = Frozen("#8A8A93");
    private static readonly Brush SuccessBadge = Frozen("#1F34C759");
    private static readonly Brush WarningBadge = Frozen("#1FFF9F0A");
    private static readonly Brush NeutralBadge = Frozen("#0FFFFFFF");
    private static readonly Brush WhiteText = Frozen("#FFFFFF");
    private static readonly Brush DisabledText = Frozen("#71717A");

    private readonly IpCheckService _ipService;
    private readonly string? _simulate;
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _copiedTimer;

    private CancellationTokenSource? _cts;
    private GeoResult? _last;
    private DateTime? _lastChecked;
    private bool _offline;
    private bool _checking;
    private bool _visibleChecking;
    private bool _copied;
    private (string Title, string Body)? _noticeOverride;

    private string _ipAddress = "—";
    private string _ipFamily = "IPv4";
    private string _countryCode = "";
    private string _countryName = "Определяем страну…";
    private string _countrySub = "GeoIP · ipapi.co";
    private string _pillText = "ПРОВЕРКА";
    private Brush _pillBackground = NeutralPillBackground;
    private Brush _pillTextBrush = NeutralPillText;
    private Brush _pillDotBrush = NeutralDot;
    private Brush _badgeBackground = NeutralBadge;
    private bool _showSkeleton = true;
    private bool _showContent;
    private bool _showCheck;
    private bool _showWarn;
    private bool _showSpinner = true;
    private bool _showNotice;
    private string _noticeTitle = "";
    private string _noticeBody = "";
    private bool _isButtonEnabled;
    private bool _showPromptIcon;
    private bool _showLockIcon;
    private string _buttonLabel = "Проверка IP…";
    private Brush _buttonForeground = DisabledText;
    private string _footerLeft = "Запрос к ipapi.co…";
    private string _footerLink = "Обновить IP";
    private bool _isFooterLinkEnabled = true;
    private bool _canCopyIp;
    private string _tooltip = "IP Guard";

    public WidgetViewModel(IpCheckService ipService, string? simulate)
    {
        _ipService = ipService;
        _simulate = simulate;

        _timer = new DispatcherTimer();
        _timer.Tick += Timer_Tick;

        _copiedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.6) };
        _copiedTimer.Tick += (_, _) =>
        {
            _copiedTimer.Stop();
            _copied = false;
            UpdatePresentation();
        };

        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
    }

    public WidgetState State { get; private set; } = WidgetState.Checking;

    /// <summary>Raised when the card needs to change its height (the notice appeared or disappeared).</summary>
    public event EventHandler? LayoutChanged;

    public string IpAddress { get => _ipAddress; private set => SetProperty(ref _ipAddress, value); }
    public string IpFamily { get => _ipFamily; private set => SetProperty(ref _ipFamily, value); }
    public string CountryCode { get => _countryCode; private set => SetProperty(ref _countryCode, value); }
    public string CountryName { get => _countryName; private set => SetProperty(ref _countryName, value); }
    public string CountrySub { get => _countrySub; private set => SetProperty(ref _countrySub, value); }
    public string PillText { get => _pillText; private set => SetProperty(ref _pillText, value); }
    public Brush PillBackground { get => _pillBackground; private set => SetProperty(ref _pillBackground, value); }
    public Brush PillTextBrush { get => _pillTextBrush; private set => SetProperty(ref _pillTextBrush, value); }
    public Brush PillDotBrush { get => _pillDotBrush; private set => SetProperty(ref _pillDotBrush, value); }
    public Brush BadgeBackground { get => _badgeBackground; private set => SetProperty(ref _badgeBackground, value); }
    public bool ShowSkeleton { get => _showSkeleton; private set => SetProperty(ref _showSkeleton, value); }
    public bool ShowContent { get => _showContent; private set => SetProperty(ref _showContent, value); }
    public bool ShowCheck { get => _showCheck; private set => SetProperty(ref _showCheck, value); }
    public bool ShowWarn { get => _showWarn; private set => SetProperty(ref _showWarn, value); }
    public bool ShowSpinner { get => _showSpinner; private set => SetProperty(ref _showSpinner, value); }
    public bool ShowNotice { get => _showNotice; private set => SetProperty(ref _showNotice, value); }
    public string NoticeTitle { get => _noticeTitle; private set => SetProperty(ref _noticeTitle, value); }
    public string NoticeBody { get => _noticeBody; private set => SetProperty(ref _noticeBody, value); }
    public bool IsButtonEnabled { get => _isButtonEnabled; private set => SetProperty(ref _isButtonEnabled, value); }
    public bool ShowPromptIcon { get => _showPromptIcon; private set => SetProperty(ref _showPromptIcon, value); }
    public bool ShowLockIcon { get => _showLockIcon; private set => SetProperty(ref _showLockIcon, value); }
    public string ButtonLabel { get => _buttonLabel; private set => SetProperty(ref _buttonLabel, value); }
    public Brush ButtonForeground { get => _buttonForeground; private set => SetProperty(ref _buttonForeground, value); }
    public string FooterLeft { get => _footerLeft; private set => SetProperty(ref _footerLeft, value); }
    public string FooterLink { get => _footerLink; private set => SetProperty(ref _footerLink, value); }
    public bool IsFooterLinkEnabled { get => _isFooterLinkEnabled; private set => SetProperty(ref _isFooterLinkEnabled, value); }
    public bool CanCopyIp { get => _canCopyIp; private set => SetProperty(ref _canCopyIp, value); }
    public string TooltipText { get => _tooltip; private set => SetProperty(ref _tooltip, value); }

    public DateTime? LastChecked => _lastChecked;
    public bool IsBusy => _checking;

    public void Start() => _ = CheckAsync(showChecking: true);

    public async Task CheckAsync(bool showChecking)
    {
        if (_checking)
        {
            return;
        }

        _checking = true;
        _timer.Stop();
        _copied = false;
        _noticeOverride = null;

        if (showChecking)
        {
            _visibleChecking = true;
        }

        UpdatePresentation();

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        try
        {
            var result = await _ipService.CheckAsync(_simulate, _last, _cts.Token);
            _last = result;
            _offline = false;
            _lastChecked = DateTime.Now;
            Log.Info($"IP {result.Ip} ({result.Family}) — {result.CountryCode} / {result.City} via {result.Source}");
        }
        catch (Exception ex)
        {
            _offline = true;
            Log.Warn("IP check failed: " + ex.Message);
        }
        finally
        {
            _checking = false;
            _visibleChecking = false;
            UpdatePresentation();
            ScheduleNext();
        }
    }

    public async Task RefreshAsync()
    {
        _timer.Stop();
        await CheckAsync(showChecking: true);
    }

    public void SetErrorNotice(string title, string body)
    {
        _noticeOverride = (title, body);
        UpdatePresentation();
    }

    public void ClearNotice()
    {
        if (_noticeOverride is null)
        {
            return;
        }

        _noticeOverride = null;
        UpdatePresentation();
    }

    public void ShowCopiedHint()
    {
        _copied = true;
        _copiedTimer.Stop();
        _copiedTimer.Start();
        UpdatePresentation();
    }

    private void ScheduleNext()
    {
        var interval = _offline
            ? TimeSpan.FromSeconds(30)
            : State == WidgetState.Blocked
                ? TimeSpan.FromSeconds(20)
                : TimeSpan.FromMinutes(5);

        _timer.Stop();
        _timer.Interval = interval;
        _timer.Start();
    }

    private async void Timer_Tick(object? sender, EventArgs e)
    {
        _timer.Stop();
        await CheckAsync(showChecking: false);
    }

    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        if (!e.IsAvailable || _checking)
        {
            return;
        }

        _ = System.Windows.Application.Current?.Dispatcher.InvokeAsync(async () =>
        {
            await CheckAsync(showChecking: false);
        });
    }

    private void UpdatePresentation()
    {
        var checking = _visibleChecking;
        var last = _last;
        var hasData = last is not null;
        var blocked = hasData && string.Equals(last!.CountryCode, "RU", StringComparison.OrdinalIgnoreCase);
        var offlineNoData = !hasData && !checking;
        var allowed = hasData && !blocked;

        State = checking
            ? WidgetState.Checking
            : offlineNoData
                ? WidgetState.Offline
                : blocked
                    ? WidgetState.Blocked
                    : WidgetState.Allowed;

        ShowSkeleton = checking && !hasData;
        ShowContent = !ShowSkeleton;

        if (hasData)
        {
            IpAddress = last!.Ip;
            IpFamily = last.Family;
            CountryCode = last.CountryCode;
            CountryName = Loc.Country(last.CountryCode, last.CountryName);
            CountrySub = Loc.Sub(last.CountryCode, last.City);
        }
        else if (checking)
        {
            IpAddress = "—";
            IpFamily = "IPv4";
            CountryCode = "";
            CountryName = "Определяем страну…";
            CountrySub = "GeoIP · ipapi.co";
        }
        else
        {
            IpAddress = "—";
            IpFamily = "IPv4";
            CountryCode = "";
            CountryName = "Не удалось определить";
            CountrySub = "Проверь соединение";
        }

        CanCopyIp = hasData && !checking;

        if (checking)
        {
            SetPill("ПРОВЕРКА", NeutralPillBackground, NeutralPillText, NeutralDot);
        }
        else if (!hasData || _offline)
        {
            SetPill("НЕТ СЕТИ", NeutralPillBackground, NeutralPillText, NeutralDot);
        }
        else if (blocked)
        {
            SetPill("VPN OFF", WarningPillBackground, WarningPillText, WarningDot);
        }
        else
        {
            SetPill("VPN ON", SuccessPillBackground, SuccessPillText, SuccessDot);
        }

        ShowSpinner = checking;
        ShowCheck = !checking && hasData && !blocked;
        ShowWarn = !checking && (blocked || !hasData);
        BadgeBackground = checking
            ? NeutralBadge
            : blocked || !hasData
                ? WarningBadge
                : SuccessBadge;

        var showNotice = State == WidgetState.Blocked || _noticeOverride is not null;
        if (_noticeOverride is { } notice)
        {
            NoticeTitle = notice.Title;
            NoticeBody = notice.Body;
        }
        else
        {
            NoticeTitle = "Claude Code недоступен из РФ";
            NoticeBody = "Включи VPN — кнопка включится после смены IP";
        }

        var noticeChanged = ShowNotice != showNotice;
        ShowNotice = showNotice;

        if (checking)
        {
            ButtonLabel = "Проверка IP…";
            IsButtonEnabled = false;
            ShowPromptIcon = false;
            ShowLockIcon = false;
            ButtonForeground = DisabledText;
        }
        else if (!hasData)
        {
            ButtonLabel = "Нет соединения";
            IsButtonEnabled = false;
            ShowPromptIcon = false;
            ShowLockIcon = false;
            ButtonForeground = DisabledText;
        }
        else if (blocked)
        {
            ButtonLabel = "Включи VPN";
            IsButtonEnabled = false;
            ShowPromptIcon = false;
            ShowLockIcon = true;
            ButtonForeground = DisabledText;
        }
        else
        {
            ButtonLabel = "Run Claude";
            IsButtonEnabled = true;
            ShowPromptIcon = true;
            ShowLockIcon = false;
            ButtonForeground = WhiteText;
        }

        if (_copied)
        {
            FooterLeft = "IP скопирован";
        }
        else if (checking)
        {
            FooterLeft = "Запрос к ipapi.co…";
        }
        else if (!hasData)
        {
            FooterLeft = "Не удалось проверить IP";
        }
        else if (_offline)
        {
            FooterLeft = _lastChecked is { } checkedAt
                ? $"Нет сети · данные в {checkedAt:HH:mm}"
                : "Нет сети";
        }
        else if (blocked)
        {
            FooterLeft = "Claude Code заблокирован";
        }
        else
        {
            FooterLeft = "Откроет Windows Terminal";
        }

        FooterLink = blocked ? "Проверить снова" : checking || hasData ? "Обновить IP" : "Повторить";
        IsFooterLinkEnabled = !_checking;

        TooltipText = hasData ? $"IP Guard · {IpAddress} · {PillText}" : "IP Guard";

        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(LastChecked));

        if (noticeChanged)
        {
            LayoutChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SetPill(string text, Brush background, Brush textBrush, Brush dot)
    {
        PillText = text;
        PillBackground = background;
        PillTextBrush = textBrush;
        PillDotBrush = dot;
    }

    private static Brush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        brush.Freeze();
        return brush;
    }

    public void Dispose()
    {
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        _timer.Stop();
        _copiedTimer.Stop();
        _cts?.Cancel();
        _cts?.Dispose();
    }
}

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
