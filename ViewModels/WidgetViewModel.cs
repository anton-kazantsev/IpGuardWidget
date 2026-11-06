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

/// <summary>Colours of the notice in the card: warning (VPN off, launch error) or success.</summary>
public enum NoticeStyle
{
    Warning,
    Success
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

    private static readonly Brush WarningNoticeBackground = Frozen("#0FFFA30A");
    private static readonly Brush WarningNoticeIcon = Frozen("#1FFF9F0A");
    private static readonly Brush WarningNoticeGlyph = Frozen("#FF9F0A");
    private static readonly Brush WarningNoticeTitle = Frozen("#FFB340");
    private static readonly Brush WarningNoticeText = Frozen("#A98F63");
    private static readonly Brush SuccessNoticeBackground = Frozen("#1F34C759");
    private static readonly Brush SuccessNoticeIcon = Frozen("#1F34C759");
    private static readonly Brush SuccessNoticeGlyph = Frozen("#34C759");
    private static readonly Brush SuccessNoticeTitle = Frozen("#4ADE80");
    private static readonly Brush SuccessNoticeText = Frozen("#8FB79A");

    /// <summary>Notice card height when the title fits one line.</summary>
    public const double NoticeSingleLineHeight = 49;

    /// <summary>Notice card height when the title wraps to two lines.</summary>
    public const double NoticeTwoLineHeight = 58;

    private readonly IpCheckService _ipService;
    private readonly string? _simulate;
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _copiedTimer;
    private readonly DispatcherTimer _flashTimer;

    private CancellationTokenSource? _cts;
    private GeoResult? _last;
    private DateTime? _lastChecked;
    private bool _offline;
    private bool _checking;
    private bool _visibleChecking;
    private bool _copied;
    private string? _footerFlash;
    private (string Title, string Body, NoticeStyle Style)? _noticeOverride;
    private bool _noticeSticky;
    private ClaudeGuardReason? _guardReason;

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
    private double _noticeHeight = NoticeSingleLineHeight;
    private string _noticeTitle = "";
    private string _noticeBody = "";
    private Brush _noticeBackground = WarningNoticeBackground;
    private Brush _noticeIconBackground = WarningNoticeIcon;
    private Brush _noticeIconForeground = WarningNoticeGlyph;
    private Brush _noticeTitleBrush = WarningNoticeTitle;
    private Brush _noticeTextBrush = WarningNoticeText;
    private string _noticeGlyph = "!";
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

        _flashTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _flashTimer.Tick += (_, _) =>
        {
            _flashTimer.Stop();
            _footerFlash = null;
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

    /// <summary>
    /// Height of the notice card: <see cref="NoticeSingleLineHeight"/> for a one-line title,
    /// <see cref="NoticeTwoLineHeight"/> when the title wraps. Set from MainWindow.
    /// </summary>
    public double NoticeHeight
    {
        get => _noticeHeight;
        set
        {
            if (SetProperty(ref _noticeHeight, value) && ShowNotice)
            {
                LayoutChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public string NoticeTitle { get => _noticeTitle; private set => SetProperty(ref _noticeTitle, value); }
    public string NoticeBody { get => _noticeBody; private set => SetProperty(ref _noticeBody, value); }
    public Brush NoticeBackground { get => _noticeBackground; private set => SetProperty(ref _noticeBackground, value); }
    public Brush NoticeIconBackground { get => _noticeIconBackground; private set => SetProperty(ref _noticeIconBackground, value); }
    public Brush NoticeIconForeground { get => _noticeIconForeground; private set => SetProperty(ref _noticeIconForeground, value); }
    public Brush NoticeTitleBrush { get => _noticeTitleBrush; private set => SetProperty(ref _noticeTitleBrush, value); }
    public Brush NoticeTextBrush { get => _noticeTextBrush; private set => SetProperty(ref _noticeTextBrush, value); }
    public string NoticeGlyph { get => _noticeGlyph; private set => SetProperty(ref _noticeGlyph, value); }
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

    /// <summary>
    /// True when the last check failed. The card keeps the last known IP and the "НЕТ СЕТИ" pill,
    /// so <see cref="State"/> can stay Allowed; the guard must look at this flag as well.
    /// </summary>
    public bool IsOffline => _offline;

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

        // A background check must not wipe the notice that the guard put on screen.
        if (!_noticeSticky)
        {
            _noticeOverride = null;
        }

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
        _noticeOverride = (title, body, NoticeStyle.Warning);
        _noticeSticky = false;
        _guardReason = null;
        NoticeHeight = NoticeSingleLineHeight;
        UpdatePresentation();
    }

    /// <summary>
    /// Warning notice about Claude Code running without VPN or network. It survives background
    /// IP checks and stays until the state allows launching again or the notice is cleared.
    /// </summary>
    public void SetStoppedNotice(string title, string body, ClaudeGuardReason reason)
    {
        _noticeOverride = (title, body, NoticeStyle.Warning);
        _noticeSticky = true;
        _guardReason = reason;
        UpdatePresentation();
    }

    /// <summary>Success notice shown when the state is allowed again after the guard warned.</summary>
    public void SetRestoredNotice(string title, string body)
    {
        _noticeOverride = (title, body, NoticeStyle.Success);
        _noticeSticky = true;
        _guardReason = null;
        NoticeHeight = NoticeSingleLineHeight;
        UpdatePresentation();
    }

    public void ClearNotice()
    {
        if (_noticeOverride is null)
        {
            return;
        }

        _noticeOverride = null;
        _noticeSticky = false;
        _guardReason = null;
        NoticeHeight = NoticeSingleLineHeight;
        UpdatePresentation();
    }

    public void ShowCopiedHint()
    {
        _copied = true;
        _copiedTimer.Stop();
        _copiedTimer.Start();
        UpdatePresentation();
    }

    /// <summary>Shows a one-off message in the card footer for ~5 s, then the usual text returns.</summary>
    public void FlashFooter(string text)
    {
        _footerFlash = text;
        FooterLeft = text;
        _flashTimer.Stop();
        _flashTimer.Start();
    }

    private void ScheduleNext()
    {
        // 1 s while the result can change quickly or matters at once: blocked / offline /
        // first check in progress, or Claude Code is running (a lost VPN or network must be
        // noticed and the process stopped immediately).
        // 20 s when the VPN is on and Claude Code is not running — just to notice a new IP.
        var fast = IsOffline
                   || State is WidgetState.Blocked or WidgetState.Offline or WidgetState.Checking
                   || ClaudeGuard.IsClaudeRunning();

        _timer.Stop();
        _timer.Interval = TimeSpan.FromSeconds(fast ? 1 : 20);
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
        var noticeStyle = NoticeStyle.Warning;
        if (_noticeOverride is { } notice)
        {
            NoticeTitle = notice.Title;
            NoticeBody = notice.Body;
            noticeStyle = notice.Style;
        }
        else
        {
            NoticeTitle = "Claude Code заблокирован в РФ";
            NoticeBody = "Включи VPN — кнопка включится после смены IP";
        }

        var success = noticeStyle == NoticeStyle.Success;
        NoticeBackground = success ? SuccessNoticeBackground : WarningNoticeBackground;
        NoticeIconBackground = success ? SuccessNoticeIcon : WarningNoticeIcon;
        NoticeIconForeground = success ? SuccessNoticeGlyph : WarningNoticeGlyph;
        NoticeTitleBrush = success ? SuccessNoticeTitle : WarningNoticeTitle;
        NoticeTextBrush = success ? SuccessNoticeText : WarningNoticeText;
        NoticeGlyph = success ? "✓" : "!";

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
        else if (_offline)
        {
            // The network is gone: the last known IP is still on screen, but nothing can be launched.
            ButtonLabel = "Нет соединения";
            IsButtonEnabled = false;
            ShowPromptIcon = false;
            ShowLockIcon = false;
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
        else if (_noticeSticky && _noticeOverride is { Style: NoticeStyle.Warning } &&
                 _guardReason == ClaudeGuardReason.VpnLost)
        {
            // Claude Code keeps working without the VPN: the footer says what is going on.
            // The network case falls through to the usual "Нет сети · данные в HH:MM".
            FooterLeft = "VPN отключён · Claude Code работает";
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
            FooterLeft = "Откроется Terminal";
        }

        FooterLink = !hasData && !checking ? "Повторить" : "Обновить IP";
        IsFooterLinkEnabled = !checking;

        if (_footerFlash is { } flash)
        {
            FooterLeft = flash;
        }

        TooltipText = hasData ? $"IP Guard · {IpAddress} · {PillText}" : "IP Guard";

        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(IsOffline));
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
        _flashTimer.Stop();
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
