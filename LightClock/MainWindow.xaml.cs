using System;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinRT.Interop;

namespace LightClock;

/// <summary>
/// A lightweight, always-on-top, draggable desktop clock widget
/// rendered as transparent text-only overlay with WinUI 3.
/// </summary>
public sealed partial class MainWindow : Window
{
    // ── State ────────────────────────────────────────────────────────────────

    private AppWindow _appWindow = null!;
    private OverlappedPresenter? _presenter;
    private DispatcherTimer _timer = null!;
    private IntPtr _hwnd;

    private bool _use24Hour = true;
    private bool _showSeconds = false;
    private bool _alwaysOnTop = true;
    private string _fontMode = "default";

    // Default widget dimensions (logical pixels at 100% DPI)
    private const int WidgetWidth = 760;
    private const int WidgetHeight = 300;

    // ── P/Invoke ─────────────────────────────────────────────────────────────

    /// <summary>Releases the mouse capture so the next message routes to the OS.</summary>
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ReleaseCapture();

    /// <summary>Sends a message directly to the window procedure (synchronous).</summary>
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    /// <summary>Sets a DWM window attribute (e.g. rounded corners on Windows 11).</summary>
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd, uint dwAttribute, ref int pvAttribute, uint cbAttribute);

    // WM_NCLBUTTONDOWN – simulate a title-bar left-click to enable native OS drag.
    private const uint WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCAPTION = 2;

    // DWMWA_WINDOW_CORNER_PREFERENCE (Windows 11 only; ignored on Windows 10).
    private const uint DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    // ── Constructor ───────────────────────────────────────────────────────────

    public MainWindow()
    {
        this.InitializeComponent();
        SetupWindow();
        SetupClock();
    }

    // ── Window setup ──────────────────────────────────────────────────────────

    private void SetupWindow()
    {
        _hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        // ── Size ─────────────────────────────────────────────────────────────
        _appWindow.Resize(new SizeInt32(WidgetWidth, WidgetHeight));

        // ── Remove title bar: extend XAML content over the entire client area ─
        _appWindow.TitleBar.ExtendsContentIntoTitleBar = true;

        // Hide default caption buttons by making them fully transparent.
        _appWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        _appWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        _appWindow.TitleBar.ButtonForegroundColor = Colors.Transparent;
        _appWindow.TitleBar.ButtonInactiveForegroundColor = Colors.Transparent;
        _appWindow.TitleBar.ButtonHoverBackgroundColor = Colors.Transparent;
        _appWindow.TitleBar.ButtonPressedBackgroundColor = Colors.Transparent;

        // ── Window presenter: borderless, always-on-top, fixed size ──────────
        if (_appWindow.Presenter is OverlappedPresenter op)
        {
            _presenter = op;
            _presenter.IsAlwaysOnTop = _alwaysOnTop;
            _presenter.IsResizable = false;
            _presenter.IsMaximizable = false;
            _presenter.IsMinimizable = false;
            // Remove the window border (thin frame) for a truly floating look.
            _presenter.SetBorderAndTitleBar(false, false);
        }

        // ── Keep default DWM corners for transparent text-only look.
        int roundCorners = DWMWCP_ROUND;
        DwmSetWindowAttribute(_hwnd, DWMWA_WINDOW_CORNER_PREFERENCE,
                              ref roundCorners, sizeof(int));

        // ── Initial position: near top-center of the primary work area ───────
        var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;
        int x = (workArea.Width - WidgetWidth) / 2;
        int y = 40;
        _appWindow.Move(new PointInt32(Math.Max(0, x), Math.Max(0, y)));

        // ── Title (shown in Alt-Tab / taskbar thumbnail) ──────────────────────
        _appWindow.Title = "LightClock";

        // ── Window icon (taskbar, Alt-Tab) ────────────────────────────────────
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(iconPath))
        {
            _appWindow.SetIcon(iconPath);
        }
    }

    // ── Clock ─────────────────────────────────────────────────────────────────

    private void SetupClock()
    {
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += (_, _) => UpdateClock();
        _timer.Start();

        // Draw immediately so the widget looks correct before the first tick.
        ApplyFontSelection();
        UpdateClock();
    }

    private void ApplyFontSelection()
    {
        string displayFont;
        string uiFont;

        switch (_fontMode)
        {
            case "mono":
                displayFont = "Consolas";
                uiFont = "Consolas";
                break;
            case "serif":
                displayFont = "Georgia";
                uiFont = "Georgia";
                break;
            default:
                displayFont = "Segoe UI Variable Display";
                uiFont = "Segoe UI Variable";
                break;
        }

        TimeText.FontFamily = new FontFamily(displayFont);
        AmPmText.FontFamily = new FontFamily(uiFont);
        SecondsText.FontFamily = new FontFamily(uiFont);
        DateText.FontFamily = new FontFamily(uiFont);
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;

        // ── Time ─────────────────────────────────────────────────────────────
        if (_use24Hour)
        {
            TimeText.Text = now.ToString("HH:mm");
            AmPmText.Visibility = Visibility.Collapsed;
        }
        else
        {
            TimeText.Text = now.ToString("hh:mm");
            AmPmText.Text = now.ToString("tt");
            AmPmText.Visibility = Visibility.Visible;
        }

        // ── Seconds ──────────────────────────────────────────────────────────
        SecondsText.Text = now.ToString(":ss");
        SecondsText.Visibility = _showSeconds ? Visibility.Visible : Visibility.Collapsed;

        // ── Date ─────────────────────────────────────────────────────────────
        DateText.Text = now.ToString("dddd, MMMM d", System.Globalization.CultureInfo.CurrentCulture);
    }

    // ── Dragging ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Left-button press on any pixel triggers native OS window dragging via
    /// WM_NCLBUTTONDOWN / HTCAPTION.  Right-button press is left untouched so
    /// the XAML ContextFlyout can handle it normally.
    /// </summary>
    private void RootGrid_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var props = e.GetCurrentPoint(RootGrid).Properties;
        if (props.IsLeftButtonPressed)
        {
            // Release XAML pointer capture so the Win32 move loop can take over.
            ReleaseCapture();
            // Tell the OS this is a title-bar click → it starts the move loop.
            SendMessage(_hwnd, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
        }
    }

    // ── Context menu handlers ─────────────────────────────────────────────────

    private void AlwaysOnTop_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleMenuFlyoutItem item && _presenter is not null)
        {
            _alwaysOnTop = item.IsChecked;
            _presenter.IsAlwaysOnTop = _alwaysOnTop;
        }
    }

    private void ShowSeconds_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleMenuFlyoutItem item)
        {
            _showSeconds = item.IsChecked;
            UpdateClock();
        }
    }

    private void Use24Hour_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleMenuFlyoutItem item)
        {
            _use24Hour = item.IsChecked;
            UpdateClock();
        }
    }

    private void FontOption_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioMenuFlyoutItem item || item.Tag is not string mode)
        {
            return;
        }

        _fontMode = mode;
        ApplyFontSelection();
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Exit();
    }
}
