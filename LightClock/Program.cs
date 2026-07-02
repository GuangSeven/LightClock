using System.Runtime.InteropServices;
using System.Text;

namespace LightClock;

internal static class Program
{
    private const string WindowClassName = "LightClockWin32Class";
    private const string WindowTitle = "LightClock";

    private const int WindowWidth = 900;
    private const int WindowHeight = 320;

    private const int TimerId = 1;
    private const int TimerIntervalMs = 1000;

    private const uint WsPopup = 0x80000000;
    private const uint WsVisible = 0x10000000;

    private const uint WsExTopmost = 0x00000008;
    private const uint WsExToolWindow = 0x00000080;
    private const uint WsExLayered = 0x00080000;

    private const uint ImageIcon = 1;
    private const uint LrLoadFromFile = 0x00000010;
    private const int IdcArrow = 32512;
    private const uint SpiGetWorkArea = 0x0030;
    private const uint TpmLeftAlign = 0x0000;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmReturnCmd = 0x0100;

    private const int SwpNomove = 0x0002;
    private const int SwpNosize = 0x0001;
    private const int SwpShowWindow = 0x0040;
    private const int SwpHideWindow = 0x0080;
    private const int SwpNoactivate = 0x0010;
    private const int SwpNozorder = 0x0004;

    private const uint LwaColorKey = 0x00000001;
    private const uint LwaAlpha = 0x00000002;
    private const byte InitialAlpha = 255;
    private const uint UlwAlpha = 0x00000002;
    private const byte AcSrcOver = 0;
    private const byte AcSrcAlpha = 1;
    private const uint DibRgbColors = 0;
    private const uint BiRgb = 0;
    private const int BitsPixel = 32;
    private const uint MfString = 0x00000000;
    private const uint MfSeparator = 0x00000800;
    private const uint MfChecked = 0x00000008;
    private const uint MfUnchecked = 0x00000000;

    private const uint DtCenter = 0x00000001;
    private const uint DtVCenter = 0x00000004;
    private const uint DtSingleLine = 0x00000020;
    private const int TransparentBkMode = 1;  // TRANSPARENT — don't fill text background
    private const int OpaqueBkMode = 2;       // OPAQUE — fill text background with current background color

    private const int CmdToggleTopMost = 1001;
    private const int CmdExit = 1002;
    private const int CmdLanguageEn = 1003;
    private const int CmdLanguageZh = 1004;
    private const int CmdFontSegoUi = 1010;
    private const int CmdFontConsolas = 1011;
    private const int CmdFontCascadiaCode = 1012;
    private const int CmdFontMicrosoftYaHei = 1013;
    private const int CmdFontCustom = 1014;  // opens an input dialog
    private const int CmdToggleAutoStart = 1020;
    private const int CmdToggleWallpaperColor = 1030;  // toggle Material-You-style wallpaper color extraction

    private const int WmCreate = 0x0001;
    private const int WmDestroy = 0x0002;
    private const int WmPaint = 0x000F;
    private const int WmClose = 0x0010;
    private const int WmCommand = 0x0111;
    private const int WmTimer = 0x0113;
    private const int WmEraseBkgnd = 0x0014;
    private const int WmRButtonUp = 0x0205;
    private const int WmNCRButtonUp = 0x00A5;
    private const int WmNCHitTest = 0x0084;
    private const int WmDpiChanged = 0x02E0;
    private const int WmDisplayChange = 0x007E;
    private const int WmWindowPosChanging = 0x0046;

    private const int HtCaption = 2;

    private const uint DefaultCharset = 1;   // DEFAULT_CHARSET - lets GDI pick the best charset for the current locale
    private const uint OutTtPrecis = 0x04;   // OUT_TT_PRECIS - prefer TrueType
    private const uint ClipDefaultPrecis = 0x00;
    private const uint ProofQuality = 0x02;  // PROOF_QUALITY - smooth edges
    private const uint AntialiasedQuality = 0x04;  // ANTIALIASED_QUALITY - smooth edges (more compatible than PROOF_QUALITY for some fonts)
    private const uint CleartypeQuality = 0x05;  // CLEARTYPE_QUALITY - best for LCD
    private const uint PitchAndFamilySwiss = 0x22;  // VARIABLE_PITCH (0x02) | FF_SWISS (0x20) — matches Segoe UI's category
    private const uint PitchAndFamilyModern = 0x31;  // FIXED_PITCH (0x01) | FF_MODERN (0x30) — used for monospaced time font

    private static readonly IntPtr HwndTopmost = new(-1);
    private static readonly IntPtr HwndNotTopmost = new(-2);

    // Default text color (used when wallpaper-color extraction is disabled).
    private static readonly uint DefaultTextColor = Rgb(195, 236, 244);

    // Current text color. Changed by the wallpaper-color feature; otherwise equals DefaultTextColor.
    // Marked as non-readonly because the wallpaper extractor updates it.
    private static uint _textColor = DefaultTextColor;

    private static readonly WndProcDelegate WndProcRef = WndProc;
    private static bool _alwaysOnTop = true;

    // Current display language: 'en' or 'zh'. Affects date format and day/month names.
    private static string _language = "en";

    // Current time font name. Default is Consolas (monospaced). User can switch to other fonts
    // via the right-click menu. Selected name is stored as a literal Win32 font face name.
    private static string _timeFontName = "Consolas";
    private static uint _timeFontPitchAndFamily = PitchAndFamilyModern;  // matches Consolas by default

    // Whether to launch LightClock at Windows startup (HKCU Run key).
    private static bool _autoStart = false;

    // Whether to extract text color from the current desktop wallpaper (Material You style).
    // When enabled, _textColor is recomputed periodically from the wallpaper image.
    private static bool _useWallpaperColor = false;

    // Path to the persistent settings file: %APPDATA%/LightClock/settings.json
    // Lazily computed because Environment.GetFolderPath isn't available on all targets.
    private static string? _settingsPath;

    private static string _dateText = string.Empty;
    private static string _timeText = string.Empty;

    // Cached GDI fonts (created once, reused across paints). Avoids leaking GDI handles by recreating fonts every timer tick.
    private static IntPtr _dateFontHandle = IntPtr.Zero;
    private static IntPtr _timeFontHandle = IntPtr.Zero;

    // Loaded icon handle. Tracked so we can free it on exit instead of leaking it.
    private static IntPtr _hIcon = IntPtr.Zero;
    private static int _dpi = 96;

    // Win11 PerMonitorV2 sends WM_DPICHANGED synchronously during CreateWindowEx (Win10 does not).
    // We must NOT call UpdateLayeredWindow before the window is fully initialized — doing so
    // causes DWM to access uninitialized state and crash on Win11. This flag gates RenderAndPresent
    // so it only runs after CreateWindowEx returns.
    private static bool _windowReady = false;

    // Cached 32-bit ARGB DIB section + its DC + old bitmap, used for per-pixel-alpha rendering.
    // Recreated only when window size or DPI changes.
    private static IntPtr _memDc = IntPtr.Zero;
    private static IntPtr _dib = IntPtr.Zero;
    private static IntPtr _oldDib = IntPtr.Zero;
    private static int _dibWidth;
    private static int _dibHeight;
    // IMPORTANT: _dibBits is the RAW POINTER to the DIB section's pixel memory. We must zero
    // this memory directly before each render (not a C# byte[] copy), because GDI's DrawText
    // writes to this same memory. The previous implementation kept a byte[] copy and cleared
    // that copy instead, leaving the real DIB memory untouched — so successive paints stacked
    // on top of each other (digits never cleared → overlap bug).
    private static IntPtr _dibBits = IntPtr.Zero;
    private static byte[]? _dibBitsBacking;

    private const int DpiDateFontHeight = 56;
    private const int DpiTimeFontHeight = 168;

    // Wallpaper color re-sampling interval. We don't re-extract every second (too expensive);
    // instead we sample once on startup and re-sample when this many timer ticks have elapsed.
    private const int WallpaperResampleIntervalTicks = 300;  // 300 seconds = 5 minutes
    private static int _wallpaperResampleCounter = 0;

    [STAThread]
    private static void Main()
    {
        var hInstance = GetModuleHandle(null);
        if (hInstance == IntPtr.Zero)
        {
            return;
        }

        // Load persisted settings before creating the window so the first render uses the
        // user's preferred language/font/etc. instead of the defaults.
        LoadSettings();

        try
        {
            Run(hInstance);
        }
        finally
        {
            // Free cached GDI fonts on exit.
            if (_dateFontHandle != IntPtr.Zero)
            {
                DeleteObject(_dateFontHandle);
                _dateFontHandle = IntPtr.Zero;
            }
            if (_timeFontHandle != IntPtr.Zero)
            {
                DeleteObject(_timeFontHandle);
                _timeFontHandle = IntPtr.Zero;
            }
            // Free the loaded icon (LoadImage created it; the window class holds a reference too).
            if (_hIcon != IntPtr.Zero)
            {
                DestroyIcon(_hIcon);
                _hIcon = IntPtr.Zero;
            }
            // Free the per-pixel-alpha DIB section.
            FreeDib();
        }
    }

    private static void Run(IntPtr hInstance)
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        IntPtr hIcon = IntPtr.Zero;
        if (File.Exists(iconPath))
        {
            hIcon = LoadImage(IntPtr.Zero, iconPath, ImageIcon, 0, 0, LrLoadFromFile);
            _hIcon = hIcon;
        }

        var wndClass = new WndClassEx
        {
            cbSize = (uint)Marshal.SizeOf<WndClassEx>(),
            style = 0,
            lpfnWndProc = WndProcRef,
            cbClsExtra = 0,
            cbWndExtra = 0,
            hInstance = hInstance,
            hIcon = hIcon,
            hCursor = LoadCursor(IntPtr.Zero, (IntPtr)IdcArrow),
            hbrBackground = IntPtr.Zero,
            lpszMenuName = null,
            lpszClassName = WindowClassName,
            hIconSm = hIcon
        };

        if (RegisterClassEx(ref wndClass) == 0)
        {
            return;
        }

        var x = 0;
        var y = 0;
        var workArea = new Rect();
        if (SystemParametersInfo(SpiGetWorkArea, 0, ref workArea, 0))
        {
            x = workArea.left + Math.Max(0, ((workArea.right - workArea.left) - WindowWidth) / 2);
            y = workArea.top + 40;
        }

        IntPtr hwnd = CreateWindowEx(
            WsExTopmost | WsExToolWindow | WsExLayered,
            WindowClassName,
            WindowTitle,
            WsPopup | WsVisible,
            x,
            y,
            WindowWidth,
            WindowHeight,
            IntPtr.Zero,
            IntPtr.Zero,
            hInstance,
            IntPtr.Zero);

        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        // CRITICAL (Win11 crash fix): query the actual DPI of the monitor the window was created on.
        // On Win11 with PerMonitorV2, CreateWindowEx creates the window at the monitor's native DPI
        // (e.g. 144 for 150% scaling). WM_DPICHANGED may fire synchronously during CreateWindowEx,
        // but it's not guaranteed — and even when it does, our handler skips rendering while
        // !_windowReady (see below). So we explicitly query the DPI here, after CreateWindowEx
        // returns, to ensure the first RenderAndPresent uses the correct font heights.
        //
        // GetDpiForWindow is available on Windows 10 1607+ and Windows 11. If it returns 0
        // (very old system), we fall back to 96.
        uint queriedDpi = GetDpiForWindow(hwnd);
        if (queriedDpi > 0 && queriedDpi != (uint)_dpi)
        {
            _dpi = (int)queriedDpi;
        }

        // Mark the window as ready — from now on, WM_DPICHANGED and WM_TIMER can safely call
        // RenderAndPresent (which calls UpdateLayeredWindow).
        _windowReady = true;

        // Use per-pixel alpha (UpdateLayeredWindow) instead of color-key transparency.
        // Color-key (LwaColorKey) produces a visible dark fringe around anti-aliased glyphs because
        // the blended edge pixels don't match the key exactly and remain visible. Per-pixel alpha
        // gives each pixel its own alpha value, so anti-aliased edges blend correctly with whatever
        // is behind the window.
        //
        // We do NOT call SetLayeredWindowAttributes here — that would override UpdateLayeredWindow.
        SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, SwpNomove | SwpNosize | SwpShowWindow);

        UpdateClockText();
        RenderAndPresent(hwnd);

        Msg msg;
        while (GetMessage(out msg, IntPtr.Zero, 0, 0))
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
    }

    private static IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WmCreate:
                SetTimer(hwnd, TimerId, TimerIntervalMs, IntPtr.Zero);
                return IntPtr.Zero;

            case WmTimer:
                if (wParam == (IntPtr)TimerId)
                {
                    UpdateClockText();
                    // Periodically re-sample the wallpaper color (every WallpaperResampleIntervalTicks
                    // seconds) so the clock adapts when the user changes their wallpaper.
                    if (_useWallpaperColor)
                    {
                        _wallpaperResampleCounter++;
                        if (_wallpaperResampleCounter >= WallpaperResampleIntervalTicks)
                        {
                            _wallpaperResampleCounter = 0;
                            uint oldColor = _textColor;
                            RefreshWallpaperColor();
                            // Only re-render if the color actually changed (avoid unnecessary work).
                            if (_textColor != oldColor)
                            {
                                // Drop cached fonts so they're recreated with the new color
                                // (SetTextColor is called per-render, so fonts don't actually
                                // need rebuilding, but the DIB must be re-rendered).
                            }
                        }
                    }
                    // Re-render the per-pixel-alpha bitmap and present it via UpdateLayeredWindow.
                    // We do NOT use InvalidateRect + WM_PAINT because layered windows with
                    // UpdateLayeredWindow bypass the normal paint pipeline.
                    RenderAndPresent(hwnd);
                }
                return IntPtr.Zero;

            case WmRButtonUp:
            case WmNCRButtonUp:
                // WM_NCHITTEST always returns HTCAPTION, so right-clicks arrive as WM_NCRBUTTONUP rather than WM_RBUTTONUP. Handle both.
                ShowContextMenu(hwnd);
                return IntPtr.Zero;

            case WmCommand:
                var commandId = LowWord(wParam);
                if (commandId == CmdToggleTopMost)
                {
                    _alwaysOnTop = !_alwaysOnTop;
                    SetWindowPos(
                        hwnd,
                        _alwaysOnTop ? HwndTopmost : HwndNotTopmost,
                        0,
                        0,
                        0,
                        0,
                        SwpNomove | SwpNosize | SwpShowWindow);
                }
                else if (commandId == CmdExit)
                {
                    PostMessage(hwnd, WmClose, IntPtr.Zero, IntPtr.Zero);
                }
                else if (commandId == CmdLanguageEn)
                {
                    if (_language != "en")
                    {
                        _language = "en";
                        UpdateClockText();
                        RenderAndPresent(hwnd);
                        SaveSettings();
                    }
                }
                else if (commandId == CmdLanguageZh)
                {
                    if (_language != "zh")
                    {
                        _language = "zh";
                        UpdateClockText();
                        RenderAndPresent(hwnd);
                        SaveSettings();
                    }
                }
                else if (commandId == CmdFontSegoUi || commandId == CmdFontConsolas ||
                         commandId == CmdFontCascadiaCode || commandId == CmdFontMicrosoftYaHei)
                {
                    // Drop cached time font so it gets recreated with the new face name on next render.
                    var (name, pitch) = commandId switch
                    {
                        CmdFontSegoUi => ("Segoe UI", PitchAndFamilySwiss),
                        CmdFontConsolas => ("Consolas", PitchAndFamilyModern),
                        CmdFontCascadiaCode => ("Cascadia Code", PitchAndFamilyModern),
                        CmdFontMicrosoftYaHei => ("Microsoft YaHei", PitchAndFamilySwiss),
                        _ => (_timeFontName, _timeFontPitchAndFamily)
                    };
                    if (_timeFontName != name)
                    {
                        _timeFontName = name;
                        _timeFontPitchAndFamily = pitch;
                        if (_timeFontHandle != IntPtr.Zero)
                        {
                            DeleteObject(_timeFontHandle);
                            _timeFontHandle = IntPtr.Zero;
                        }
                        RenderAndPresent(hwnd);
                        SaveSettings();
                    }
                }
                else if (commandId == CmdFontCustom)
                {
                    // Open a Win32 input dialog to let the user type any system font name.
                    // We then validate it via EnumFontFamilies before applying.
                    string? input = PromptForFontName(hwnd);
                    if (!string.IsNullOrWhiteSpace(input))
                    {
                        string trimmed = input.Trim();
                        if (IsFontAvailable(trimmed))
                        {
                            // Use DEFAULT pitch/family so GDI picks the best match for the named font.
                            _timeFontName = trimmed;
                            _timeFontPitchAndFamily = PitchAndFamilySwiss;  // sensible default for sans-serif fonts
                            if (_timeFontHandle != IntPtr.Zero)
                            {
                                DeleteObject(_timeFontHandle);
                                _timeFontHandle = IntPtr.Zero;
                            }
                            RenderAndPresent(hwnd);
                            SaveSettings();
                        }
                        else
                        {
                            MessageBox(hwnd,
                                $"The font '{trimmed}' was not found on this system.\n\nPlease check the spelling or install the font first.",
                                "LightClock - Font Not Found",
                                0x10 /* MB_ICONERROR */);
                        }
                    }
                }
                else if (commandId == CmdToggleAutoStart)
                {
                    _autoStart = !_autoStart;
                    SetAutoStart(_autoStart);
                    SaveSettings();
                }
                else if (commandId == CmdToggleWallpaperColor)
                {
                    _useWallpaperColor = !_useWallpaperColor;
                    if (_useWallpaperColor)
                    {
                        // Immediately sample the wallpaper so the color change is visible.
                        RefreshWallpaperColor();
                        _wallpaperResampleCounter = 0;
                    }
                    else
                    {
                        // Revert to the default text color.
                        _textColor = DefaultTextColor;
                    }
                    RenderAndPresent(hwnd);
                    SaveSettings();
                }
                return IntPtr.Zero;

            case WmPaint:
                Paint(hwnd);
                return IntPtr.Zero;

            case WmEraseBkgnd:
                return (IntPtr)1;

            case WmDpiChanged:
                // wParam = new DPI (HIWORD = y dpi, LOWORD = x dpi). lParam = pointer to suggested RECT.
                int newDpi = (int)(((ulong)(long)wParam >> 16) & 0xFFFF);
                if (newDpi > 0 && newDpi != _dpi)
                {
                    _dpi = newDpi;
                    // Drop cached fonts so they get recreated at the new DPI on the next paint.
                    if (_dateFontHandle != IntPtr.Zero)
                    {
                        DeleteObject(_dateFontHandle);
                        _dateFontHandle = IntPtr.Zero;
                    }
                    if (_timeFontHandle != IntPtr.Zero)
                    {
                        DeleteObject(_timeFontHandle);
                        _timeFontHandle = IntPtr.Zero;
                    }
                    // Free the DIB so it gets recreated at the new size.
                    FreeDib();

                    // Per MSDN, the app should respond to WM_DPICHANGED by resizing its window
                    // to the suggested rect in lParam. We apply it via SetWindowPos.
                    if (lParam != IntPtr.Zero)
                    {
                        var suggestedRect = Marshal.PtrToStructure<Rect>(lParam);
                        SetWindowPos(hwnd, IntPtr.Zero,
                            suggestedRect.left, suggestedRect.top,
                            suggestedRect.right - suggestedRect.left,
                            suggestedRect.bottom - suggestedRect.top,
                            SwpNozorder | SwpNoactivate);
                    }

                    // Only render if the window is fully initialized. On Win11, WM_DPICHANGED can
                    // fire synchronously during CreateWindowEx — calling UpdateLayeredWindow at
                    // that point crashes DWM. The _windowReady flag is set after CreateWindowEx
                    // returns in Run().
                    if (_windowReady)
                    {
                        RenderAndPresent(hwnd);
                    }
                }
                return IntPtr.Zero;

            case WmNCHitTest:
                return (IntPtr)HtCaption;

            case WmWindowPosChanging:
                // Win+D (Show Desktop) hides all top-level windows by sending WM_WINDOWPOSCHANGING
                // with the SWP_HIDEWINDOW flag. We strip that flag here so the clock stays visible
                // when the user presses Win+D. This also blocks programmatic minimize-all commands
                // from hiding the clock while still allowing normal minimize via the taskbar (which
                // this window doesn't have, being a WS_EX_TOOLWINDOW).
                //
                // IMPORTANT: after editing the WINDOWPOS struct, we must fall through to
                // DefWindowProc so other position changes (move, resize, Z-order) are applied
                // normally. Previously we returned IntPtr.Zero here, which swallowed ALL
                // position changes including Z-order → the 'Always on Top' toggle (which uses
                // HWND_TOPMOST/HWND_NOTOPMOST via SetWindowPos) had no effect, so the clock
                // was permanently stuck on top.
                if (wParam != IntPtr.Zero)
                {
                    var wp = Marshal.PtrToStructure<WindowPos>(wParam);
                    if ((wp.flags & SwpHideWindow) != 0)
                    {
                        wp.flags &= ~(uint)SwpHideWindow;
                        Marshal.StructureToPtr(wp, wParam, fDeleteOld: false);
                    }
                }
                break;  // fall through to DefWindowProc

            case WmDestroy:
                KillTimer(hwnd, TimerId);
                PostQuitMessage(0);
                return IntPtr.Zero;
        }

        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private static void Paint(IntPtr hwnd)
    {
        // Layered windows using UpdateLayeredWindow don't go through WM_PAINT — we render
        // directly into a 32-bit DIB and call UpdateLayeredWindow in RenderAndPresent.
        // But Windows may still send WM_PAINT (e.g. when the window is uncovered); validate
        // the region so we don't get spurious repaints.
        var hdc = BeginPaint(hwnd, out var ps);
        if (hdc != IntPtr.Zero)
        {
            EndPaint(hwnd, ref ps);
        }
    }

    /// <summary>
    /// Renders the clock into a 32-bit ARGB DIB and presents it via UpdateLayeredWindow.
    /// This is the per-pixel-alpha path that eliminates the dark fringe around anti-aliased
    /// text glyphs that color-key transparency (LwaColorKey) produced.
    /// </summary>
    private static void RenderAndPresent(IntPtr hwnd)
    {
        GetClientRect(hwnd, out var rect);
        int width = rect.right - rect.left;
        int height = rect.bottom - rect.top;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        EnsureDib(width, height);
        if (_memDc == IntPtr.Zero || _dib == IntPtr.Zero || _dibBits == IntPtr.Zero)
        {
            return;
        }

        // Clear the REAL DIB pixel memory to fully transparent (all zero).
        // This must touch the DIB's own memory (via _dibBits pointer), NOT a byte[] copy.
        // If we only cleared a copy, the next DrawText would stack on top of the previous
        // frame's glyphs → digits would never disappear, causing the overlap bug.
        RtlZeroMemory(_dibBits, (nuint)(width * height * 4));

        // Lazily create fonts once and reuse them across paints to avoid leaking GDI handles.
        // Scale by current DPI so the clock stays readable when dragged between monitors with different DPI.
        int dateHeight = MulDiv(DpiDateFontHeight, _dpi, 96);
        int timeHeight = MulDiv(DpiTimeFontHeight, _dpi, 96);
        _dateFontHandle = _dateFontHandle == IntPtr.Zero
            ? CreateFont(dateHeight, 0, 0, 0, 400, 0, 0, 0, DefaultCharset, OutTtPrecis, ClipDefaultPrecis, CleartypeQuality, PitchAndFamilySwiss, "Segoe UI")
            : _dateFontHandle;
        // Time font uses Consolas (monospaced) so each digit takes the same width.
        // With Segoe UI (proportional), "1" is narrower than "8" — when the minute changes
        // from e.g. 11:58 to 11:59, the text width shifts and DT_CENTER re-centers it,
        // causing a visible horizontal "jump". A monospaced font eliminates this entirely.
        _timeFontHandle = _timeFontHandle == IntPtr.Zero
            ? CreateFont(timeHeight, 0, 0, 0, 600, 0, 0, 0, DefaultCharset, OutTtPrecis, ClipDefaultPrecis, CleartypeQuality, _timeFontPitchAndFamily, _timeFontName)
            : _timeFontHandle;

        // Set up the memory DC for text rendering.
        // We render with OPAQUE background mode and a sentinel 'background' color (magenta,
        // RGB(255,0,255)) so we can distinguish background pixels from text pixels in
        // post-processing. GDI's DrawText does NOT write the alpha channel of a 32-bit DIB,
        // so we can't rely on alpha to tell us where text was drawn.
        //
        // After DrawText, we walk the pixels:
        //   - Pixels exactly equal to magenta -> background -> set alpha = 0 (fully transparent).
        //   - Pixels that aren't magenta -> text (or anti-aliased blend of text and magenta)
        //     -> set alpha = 255 and un-blend the magenta out of the RGB to recover the true
        //     text color. This gives smooth anti-aliased edges with proper per-pixel alpha.
        uint bgColor = Rgb(255, 0, 255);  // magenta sentinel
        SetBkMode(_memDc, OpaqueBkMode);
        SetBkColor(_memDc, bgColor);
        SetTextColor(_memDc, _textColor);

        IntPtr oldFont = SelectObject(_memDc, _dateFontHandle);
        var dateRect = new Rect
        {
            left = rect.left,
            right = rect.right,
            top = rect.top + 18,
            bottom = rect.top + 120
        };
        DrawText(_memDc, _dateText, _dateText.Length, ref dateRect, DtCenter | DtVCenter | DtSingleLine);

        SelectObject(_memDc, _timeFontHandle);
        var timeRect = new Rect
        {
            left = rect.left,
            right = rect.right,
            top = rect.top + 92,
            bottom = rect.bottom
        };
        DrawText(_memDc, _timeText, _timeText.Length, ref timeRect, DtCenter | DtVCenter | DtSingleLine);
        SelectObject(_memDc, oldFont);

        // Copy the CURRENT DIB pixel data (post-DrawText) into our managed buffer so we can
        // post-process it. This is a READ from DIB memory, NOT a write to it. After processing,
        // we Marshal.Copy back into the DIB so UpdateLayeredWindow sees the correct pixels.
        int byteCount = width * height * 4;
        if (_dibBitsBacking == null || _dibBitsBacking.Length != byteCount)
        {
            _dibBitsBacking = new byte[byteCount];
        }
        Marshal.Copy(_dibBits, _dibBitsBacking, 0, byteCount);

        // Post-process: convert magenta-background + text-foreground into per-pixel ARGB.
        //
        // GDI anti-aliasing blends text color (T) with background color (B = magenta) at edge
        // pixels: result = T*alpha + B*(1-alpha), where alpha is the text coverage (0..1).
        // Solving for alpha:
        //   For each channel c: result_c = T_c * alpha + B_c * (1 - alpha)
        //                       alpha = (result_c - B_c) / (T_c - B_c)
        // We compute alpha from the channel where |T_c - B_c| is largest (most sensitive),
        // then un-blend to recover T_c, then write premultiplied ARGB.
        //
        // Magenta background: B = (R=255, G=0, B=255).
        // Text color: T = current _textColor components (may be the default or wallpaper-derived).
        // We pick the channel with the largest |T_c - B_c| for alpha computation.
        //   - Red:   |T_r - 255|
        //   - Green: |T_g - 0|   = T_g
        //   - Blue:  |T_b - 255|
        // Green is usually the best choice when T has a high green component; we compute all
        // three and pick the max dynamically so the algorithm works for any text color.
        byte tR = (byte)(_textColor & 0xFF);
        byte tG = (byte)((_textColor >> 8) & 0xFF);
        byte tB = (byte)((_textColor >> 16) & 0xFF);
        byte bR = 255, bG = 0, bB = 255;    // magenta background components
        int diffR = Math.Abs(tR - bR);
        int diffG = Math.Abs(tG - bG);  // = tG since bG=0
        int diffB = Math.Abs(tB - bB);
        // Choose the channel with the largest |T-B| difference for alpha computation.
        // If all diffs are 0 (text color == magenta, unlikely), fall back to green.
        int alphaChannel = diffG >= diffR && diffG >= diffB ? 1 : (diffR >= diffB ? 0 : 2);
        int tAlpha = alphaChannel == 0 ? tR : (alphaChannel == 1 ? tG : tB);
        int bAlpha = alphaChannel == 0 ? bR : (alphaChannel == 1 ? bG : bB);

        for (int i = 0; i < _dibBitsBacking.Length; i += 4)
        {
            byte b = _dibBitsBacking[i];        // DIB layout is BGRA
            byte g = _dibBitsBacking[i + 1];
            byte r = _dibBitsBacking[i + 2];
            // byte a = _dibBitsBacking[i + 3];  // untouched by GDI; we'll set it ourselves

            // Detect pure background (magenta) — use a small tolerance for safety.
            if (r >= 250 && g <= 5 && b >= 250)
            {
                // Background: fully transparent.
                _dibBitsBacking[i] = 0;
                _dibBitsBacking[i + 1] = 0;
                _dibBitsBacking[i + 2] = 0;
                _dibBitsBacking[i + 3] = 0;
                continue;
            }

            // Compute alpha (0..255) from the chosen channel (largest |T-B| diff).
            // alpha = (channel - bAlpha) * 255 / (tAlpha - bAlpha)
            int channelVal = alphaChannel == 0 ? r : (alphaChannel == 1 ? g : b);
            int denom = tAlpha - bAlpha;
            int alpha = denom == 0 ? 0 : ((channelVal - bAlpha) * 255) / denom;
            if (alpha > 255) alpha = 255;
            if (alpha < 0) alpha = 0;

            if (alpha == 0)
            {
                _dibBitsBacking[i] = 0;
                _dibBitsBacking[i + 1] = 0;
                _dibBitsBacking[i + 2] = 0;
                _dibBitsBacking[i + 3] = 0;
            }
            else
            {
                // Un-blend: recover true text color for this pixel.
                // result = T*alpha + B*(1-alpha) → T = (result - B*(1-alpha)) / alpha
                // For premultiplied output we want T*alpha, so:
                //   premultiplied = result - B*(1-alpha) = result - B + B*alpha
                // We compute premultiplied B, G, R directly.
                int pmB = b - (bB * (255 - alpha) + 128) / 255;
                int pmG = g - (bG * (255 - alpha) + 128) / 255;
                int pmR = r - (bR * (255 - alpha) + 128) / 255;
                if (pmB < 0) pmB = 0; if (pmB > 255) pmB = 255;
                if (pmG < 0) pmG = 0; if (pmG > 255) pmG = 255;
                if (pmR < 0) pmR = 0; if (pmR > 255) pmR = 255;
                _dibBitsBacking[i] = (byte)pmB;
                _dibBitsBacking[i + 1] = (byte)pmG;
                _dibBitsBacking[i + 2] = (byte)pmR;
                _dibBitsBacking[i + 3] = (byte)alpha;
            }
        }

        // Write the processed pixels BACK to the DIB so UpdateLayeredWindow sees them.
        Marshal.Copy(_dibBitsBacking, 0, _dibBits, byteCount);

        // Present via UpdateLayeredWindow with per-pixel alpha.
        var blend = new BlendFunction
        {
            BlendOp = AcSrcOver,
            BlendFlags = 0,
            SourceConstantAlpha = InitialAlpha,
            AlphaFormat = AcSrcAlpha
        };
        var zeroPt = new Point { x = 0, y = 0 };
        var size = new Size { cx = width, cy = height };

        // UpdateLayeredWindow requires a screen DC (hdcDst) and the window's current position
        // (pptDst). Passing NULL for either causes the function to silently fail on some
        // Windows versions, leaving the window completely invisible (process running but
        // no GUI shown). We fetch the actual window rect via GetWindowRect to be safe.
        GetWindowRect(hwnd, out var windowRect);
        var dstPt = new Point { x = windowRect.left, y = windowRect.top };

        IntPtr screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
        {
            return;
        }
        try
        {
            UpdateLayeredWindow(
                hwnd,
                screenDc,         // hdcDst - real screen DC (not NULL)
                ref dstPt,        // pptDst - window's current position (not NULL)
                ref size,         // psize - new window size
                _memDc,           // hdcSrc - source DC
                ref zeroPt,       // pptSrc - source origin
                0,                // crKey - color key (unused with ULW_ALPHA)
                ref blend,
                UlwAlpha);
            // Return value is non-zero on success; we don't currently surface failures
            // because there's no logging channel, but the explicit DC + position fix above
            // addresses the most common 'invisible window' bug.
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    /// <summary>
    /// Ensures we have a 32-bit ARGB DIB section of the given size for per-pixel-alpha rendering.
    /// Reuses the existing DIB if the size hasn't changed.
    /// </summary>
    private static void EnsureDib(int width, int height)
    {
        if (_dib != IntPtr.Zero && _dibWidth == width && _dibHeight == height)
        {
            return;
        }

        FreeDib();

        // Create a compatible memory DC.
        IntPtr screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
        {
            return;
        }
        try
        {
            _memDc = CreateCompatibleDC(screenDc);
            if (_memDc == IntPtr.Zero)
            {
                return;
            }

            var bmi = new BitmapInfo
            {
                bmiHeader = new BitmapInfoHeader
                {
                    biSize = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                    biWidth = width,
                    biHeight = -height,  // negative = top-down DIB (so row 0 is at the top)
                    biPlanes = 1,
                    biBitCount = (ushort)BitsPixel,
                    biCompression = BiRgb,
                    biSizeImage = (uint)(width * height * 4),
                    biXPelsPerMeter = 0,
                    biYPelsPerMeter = 0,
                    biClrUsed = 0,
                    biClrImportant = 0
                }
            };

            _dib = CreateDIBSection(screenDc, ref bmi, DibRgbColors, out IntPtr bits, IntPtr.Zero, 0);
            if (_dib == IntPtr.Zero)
            {
                DeleteDC(_memDc);
                _memDc = IntPtr.Zero;
                return;
            }

            _dibBits = bits;  // keep the raw pointer to DIB pixel memory

            _oldDib = SelectObject(_memDc, _dib);

            // Allocate a managed byte[] buffer for post-processing (read → premultiply → write).
            // We don't pre-fill it here; RenderAndPresent will Marshal.Copy from _dibBits into it
            // after DrawText, process it, then Marshal.Copy back.
            int byteCount = width * height * 4;
            _dibBitsBacking = new byte[byteCount];

            _dibWidth = width;
            _dibHeight = height;
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    /// <summary>
    /// Frees the cached DIB section and its memory DC.
    /// </summary>
    private static void FreeDib()
    {
        if (_memDc != IntPtr.Zero)
        {
            if (_oldDib != IntPtr.Zero)
            {
                SelectObject(_memDc, _oldDib);
                _oldDib = IntPtr.Zero;
            }
            DeleteDC(_memDc);
            _memDc = IntPtr.Zero;
        }
        if (_dib != IntPtr.Zero)
        {
            DeleteObject(_dib);
            _dib = IntPtr.Zero;
        }
        _dibBits = IntPtr.Zero;
        _dibWidth = 0;
        _dibHeight = 0;
        _dibBitsBacking = null;
    }

    private static void ShowContextMenu(IntPtr hwnd)
    {
        IntPtr menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return;
        }

        try
        {
            // Always on Top (toggle)
            AppendMenu(menu, MfString | (_alwaysOnTop ? MfChecked : MfUnchecked), CmdToggleTopMost, "Always on Top");

            // Language submenu
            IntPtr langMenu = CreatePopupMenu();
            AppendMenu(langMenu, MfString | (_language == "en" ? MfChecked : MfUnchecked), (IntPtr)CmdLanguageEn, "English");
            AppendMenu(langMenu, MfString | (_language == "zh" ? MfChecked : MfUnchecked), (IntPtr)CmdLanguageZh, "中文 (Chinese)");
            AppendMenu(menu, MfString | 0x0010 /* MF_POPUP */, langMenu, "Language");

            // Font submenu (affects time display only; date always uses Segoe UI for proportional look)
            IntPtr fontMenu = CreatePopupMenu();
            AppendMenu(fontMenu, MfString | (_timeFontName == "Segoe UI" ? MfChecked : MfUnchecked), (IntPtr)CmdFontSegoUi, "Segoe UI");
            AppendMenu(fontMenu, MfString | (_timeFontName == "Consolas" ? MfChecked : MfUnchecked), (IntPtr)CmdFontConsolas, "Consolas");
            AppendMenu(fontMenu, MfString | (_timeFontName == "Cascadia Code" ? MfChecked : MfUnchecked), (IntPtr)CmdFontCascadiaCode, "Cascadia Code");
            AppendMenu(fontMenu, MfString | (_timeFontName == "Microsoft YaHei" ? MfChecked : MfUnchecked), (IntPtr)CmdFontMicrosoftYaHei, "Microsoft YaHei");
            // Show a checkmark on "Custom..." only if the current font is NOT one of the four presets.
            bool isPreset = _timeFontName == "Segoe UI" || _timeFontName == "Consolas" ||
                            _timeFontName == "Cascadia Code" || _timeFontName == "Microsoft YaHei";
            AppendMenu(fontMenu, MfSeparator, IntPtr.Zero, null);
            AppendMenu(fontMenu, MfString | (!isPreset ? MfChecked : MfUnchecked), (IntPtr)CmdFontCustom, "Custom...");
            AppendMenu(menu, MfString | 0x0010 /* MF_POPUP */, fontMenu, "Font");

            // Start with Windows (toggle)
            AppendMenu(menu, MfString | (_autoStart ? MfChecked : MfUnchecked), (IntPtr)CmdToggleAutoStart, "Start with Windows");

            // Wallpaper color (toggle) - Material You style text color extraction
            AppendMenu(menu, MfString | (_useWallpaperColor ? MfChecked : MfUnchecked), (IntPtr)CmdToggleWallpaperColor, "Wallpaper Color");

            // Exit
            AppendMenu(menu, MfSeparator, IntPtr.Zero, null);
            AppendMenu(menu, MfString, (IntPtr)CmdExit, "Exit");

            GetCursorPos(out var point);
            SetForegroundWindow(hwnd);
            // TPM_RETURNCMD makes TrackPopupMenu return the selected item ID instead of posting WM_COMMAND.
            // Capture the return value and dispatch the command explicitly so menu items actually work.
            int chosen = TrackPopupMenuRaw(
                menu,
                TpmLeftAlign | TpmRightButton | TpmReturnCmd,
                point.x,
                point.y,
                0,
                hwnd,
                IntPtr.Zero);

            if (chosen != 0)
            {
                PostMessage(hwnd, WmCommand, (IntPtr)chosen, IntPtr.Zero);
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private static void UpdateClockText()
    {
        var now = DateTime.Now;
        if (_language == "zh")
        {
            // Chinese: build the date string manually to avoid .NET's zh-CN DateTimeFormat
            // inserting spaces between numeric components (e.g. "7 月 1日" instead of "7月1日").
            // We use the culture only to get the localized weekday name, then assemble the
            // final string ourselves — numeric parts have no surrounding spaces, but a single
            // space is intentionally kept before the weekday (e.g. "7月1日 星期三").
            //
            // GetCultureInfo returns a cached instance (unlike 'new CultureInfo' which allocates),
            // and this method runs every second on the timer tick.
            try
            {
                var ci = System.Globalization.CultureInfo.GetCultureInfo("zh-CN");
                // zh-CN's default DateTimeFormatInfo.DayNames gives "星期日","星期一",...,"星期六".
                string weekday = ci.DateTimeFormat.DayNames[(int)now.DayOfWeek];
                _dateText = $"{now.Month}月{now.Day}日 {weekday}";
                _timeText = now.ToString("HH:mm", ci);
            }
            catch
            {
                // CultureInfo not available (e.g. invariant-globalization mode) — fall back to English.
                var enCi = System.Globalization.CultureInfo.GetCultureInfo("en-US");
                _dateText = now.ToString("dddd, MMMM d", enCi);
                _timeText = now.ToString("HH:mm", enCi);
            }
        }
        else
        {
            // English: explicitly use en-US culture so the weekday and month names are
            // always English, regardless of the system's default UI language. Without this,
            // 'now.ToString("dddd, MMMM d")' uses the current thread culture (which on a
            // Chinese Windows is zh-CN), producing '星期三, 7月 1' instead of 'Wednesday, July 1'.
            //
            // GetCultureInfo returns a cached instance (unlike 'new CultureInfo' which allocates),
            // and this method runs every second on the timer tick.
            var enCi = System.Globalization.CultureInfo.GetCultureInfo("en-US");
            _dateText = now.ToString("dddd, MMMM d", enCi);
            _timeText = now.ToString("HH:mm", enCi);
        }
    }

    private static int LowWord(IntPtr value) => (int)((uint)(long)value & 0xFFFF);

    private static int MulDiv(int nNumber, int nNumerator, int nDenominator)
        => (int)(((long)nNumber * nNumerator) / nDenominator);

    private static uint Rgb(byte r, byte g, byte b) => (uint)(r | (g << 8) | (b << 16));

    // ---- Custom font input dialog + font availability check ----

    // ID constants for the font-prompt dialog controls.
    private const int IdcFontPromptLabel = 1000;
    private const int IdcFontPromptEdit = 1001;
    private const int Idok = 1;
    private const int Idcancel = 2;

    /// <summary>
    /// Opens a simple Win32 input dialog to let the user type a font name.
    /// Returns null if the user cancelled; otherwise the trimmed string they entered.
    /// Uses a lightweight approach: a modal dialog box with a single-line edit control,
    /// built via an in-memory DLGTEMPLATE (no resource file needed).
    /// </summary>
    private static string? PromptForFontName(IntPtr parentHwnd)
    {
        var state = new FontPromptState { FontName = _timeFontName };
        IntPtr hInst = GetModuleHandle(null);
        byte[] template = BuildFontPromptTemplate();
        IntPtr hGlobal = Marshal.AllocHGlobal(template.Length);
        try
        {
            Marshal.Copy(template, 0, hGlobal, template.Length);
            GCHandle stateHandle = GCHandle.Alloc(state);
            try
            {
                IntPtr dlgProc = Marshal.GetFunctionPointerForDelegate(FontPromptDialogProc);
                int result = DialogBoxIndirectParam(hInst, hGlobal, parentHwnd, dlgProc, GCHandle.ToIntPtr(stateHandle));
                return result <= 0 ? null : state.FontName;
            }
            finally
            {
                if (stateHandle.IsAllocated) stateHandle.Free();
            }
        }
        finally
        {
            Marshal.FreeHGlobal(hGlobal);
        }
    }

    private sealed class FontPromptState
    {
        public string? FontName;
    }

    /// <summary>
    /// Dialog procedure for the font-prompt dialog. Handles WM_INITDIALOG (pre-fill the edit
    /// field with the current font name and select all), and WM_COMMAND for IDOK (read the
    /// edit field text and store it) and IDCANCEL (close without saving).
    /// </summary>
    private delegate IntPtr DialogProc(IntPtr hDlg, uint msg, IntPtr wParam, IntPtr lParam);
    private static readonly DialogProc FontPromptDialogProc = FontPromptDialogProcImpl;

    // Singleton state for the active font-prompt dialog. Only one dialog can be open at a time
    // (modal), so a single static field is sufficient and avoids the complexity of storing the
    // GCHandle in the dialog's DWLP_USER window bytes.
    private static FontPromptState? _activeFontPromptState;

    private static IntPtr FontPromptDialogProcImpl(IntPtr hDlg, uint msg, IntPtr wParam, IntPtr lParam)
    {
        const int WmInitDialog = 0x0110;
        const int WmCommand = 0x0111;
        // High word of wParam in WM_COMMAND is the notification code; low word is the control ID.
        switch (msg)
        {
            case WmInitDialog:
            {
                // lParam holds the GCHandle to FontPromptState passed via DialogBoxIndirectParam's dwInitParam.
                GCHandle handle = GCHandle.FromIntPtr(lParam);
                _activeFontPromptState = (FontPromptState)handle.Target!;
                // Pre-fill the edit field with the current font name.
                if (!string.IsNullOrEmpty(_activeFontPromptState.FontName))
                {
                    SetDlgItemText(hDlg, IdcFontPromptEdit, _activeFontPromptState.FontName);
                }
                // Select all text in the edit field so the user can immediately type to replace.
                SendDlgItemMessage(hDlg, IdcFontPromptEdit, 0x00B1 /* EM_SETSEL */, (IntPtr)0, (IntPtr)(-1));
                // Set focus to the edit field.
                SetFocus(GetDlgItem(hDlg, IdcFontPromptEdit));
                return (IntPtr)0;  // return FALSE because we set focus manually
            }
            case WmCommand:
            {
                int controlId = (int)((uint)(long)wParam & 0xFFFF);
                int notification = (int)(((uint)(long)wParam >> 16) & 0xFFFF);
                if (controlId == Idok && notification == 0 /* BN_CLICKED */)
                {
                    // Read the edit field text.
                    int len = (int)SendDlgItemMessage(hDlg, IdcFontPromptEdit, 0x000E /* WM_GETTEXTLENGTH */, IntPtr.Zero, IntPtr.Zero);
                    if (len > 0 && _activeFontPromptState != null)
                    {
                        var sb = new System.Text.StringBuilder(len + 1);
                        GetDlgItemText(hDlg, IdcFontPromptEdit, sb, sb.Capacity);
                        _activeFontPromptState.FontName = sb.ToString();
                    }
                    EndDialog(hDlg, Idok);
                    return (IntPtr)1;
                }
                if (controlId == Idcancel && notification == 0 /* BN_CLICKED */)
                {
                    EndDialog(hDlg, Idcancel);
                    return (IntPtr)1;
                }
                return (IntPtr)0;
            }
        }
        return (IntPtr)0;
    }

    /// <summary>
    /// Builds a minimal in-memory DLGTEMPLATE for the font-prompt dialog.
    /// Layout: a 320x140 dialog with a label, an edit field, an OK button, and a Cancel button.
    /// All coordinates are in dialog-template units (roughly 1/4 of the average char size).
    /// </summary>
    private static byte[] BuildFontPromptTemplate()
    {
        // DLGTEMPLATE structure (WORD-aligned):
        //   WORD style; WORD exStyle; WORD cdit; short x, y, cx, cy;
        // Followed by: menu (0xFFFF + 0x0000 = no menu), class (0xFFFF + 0x0000 = default), title (WCHAR*).
        // Then for each control: DWORD style; DWORD exStyle; short x, y, cx, cy; WORD id;
        //   then class (0xFFFF + atom OR WCHAR*), title (WCHAR*), extra data count (WORD).
        // We use System.Drawing-less binary writer.
        using var ms = new System.IO.MemoryStream();
        using var bw = new System.IO.BinaryWriter(ms, System.Text.Encoding.Unicode);
        // Style: WS_POPUP | WS_VISIBLE | WS_CAPTION | DS_MODALFRAME | DS_CENTER
        const uint style = 0x80000000 | 0x10000000 | 0x00C00000 | 0x00000080 /* DS_MODALFRAME */ | 0x00000800 /* DS_CENTER */;
        const uint exStyle = 0x00000000;
        const ushort cdit = 4;  // 4 controls: label, edit, OK, Cancel
        // Dialog box rect (in dialog units): x=0, y=0, cx=300, cy=120
        bw.Write((uint)style);
        bw.Write((uint)exStyle);
        bw.Write((ushort)cdit);
        bw.Write((short)0);   // x
        bw.Write((short)0);   // y
        bw.Write((short)300); // cx
        bw.Write((short)120); // cy
        // Menu: 0x0000 (no menu)
        bw.Write((ushort)0x0000);
        // Window class: 0x0000 (default dialog class)
        bw.Write((ushort)0x0000);
        // Title: "Custom Font"
        foreach (char c in "Custom Font") bw.Write((short)c);
        bw.Write((short)0);  // null terminator
        // If DS_SETFONT were set, we'd add fontSize + fontName here. We don't, so skip.

        // --- Control 1: Label (static text) ---
        // Style: WS_CHILD | WS_VISIBLE | SS_LEFT
        bw.Write((uint)(0x40000000 | 0x10000000 | 0x00000000));
        bw.Write((uint)0);  // exStyle
        bw.Write((short)10);   // x
        bw.Write((short)10);   // y
        bw.Write((short)280);  // cx
        bw.Write((short)15);   // cy
        bw.Write((ushort)IdcFontPromptLabel);  // id
        // Class: 0xFFFF + 0x0082 (static)
        bw.Write((ushort)0xFFFF);
        bw.Write((ushort)0x0082);
        // Title: "Enter font name (e.g. Arial, Times New Roman):"
        foreach (char c in "Enter font name (e.g. Arial, Times New Roman):") bw.Write((short)c);
        bw.Write((short)0);
        bw.Write((ushort)0);  // extra data count

        // --- Control 2: Edit field ---
        // Style: WS_CHILD | WS_VISIBLE | WS_BORDER | WS_TABSTOP | ES_AUTOHSCROLL
        bw.Write((uint)(0x40000000 | 0x10000000 | 0x00800000 | 0x00010000 | 0x00000080));
        bw.Write((uint)0x00000200);  // exStyle = WS_EX_CLIENTEDGE for a 3D sunken border
        bw.Write((short)10);   // x
        bw.Write((short)30);   // y
        bw.Write((short)280);  // cx
        bw.Write((short)20);   // cy
        bw.Write((ushort)IdcFontPromptEdit);  // id
        // Class: 0xFFFF + 0x0081 (edit)
        bw.Write((ushort)0xFFFF);
        bw.Write((ushort)0x0081);
        // Title: empty
        bw.Write((short)0);
        bw.Write((ushort)0);  // extra data count

        // --- Control 3: OK button ---
        // Style: WS_CHILD | WS_VISIBLE | WS_TABSTOP | BS_DEFPUSHBUTTON
        bw.Write((uint)(0x40000000 | 0x10000000 | 0x00010000 | 0x00000001));
        bw.Write((uint)0);
        bw.Write((short)130); // x
        bw.Write((short)65);  // y
        bw.Write((short)70);  // cx
        bw.Write((short)25);  // cy
        bw.Write((ushort)Idok);
        // Class: 0xFFFF + 0x0080 (button)
        bw.Write((ushort)0xFFFF);
        bw.Write((ushort)0x0080);
        foreach (char c in "OK") bw.Write((short)c);
        bw.Write((short)0);
        bw.Write((ushort)0);

        // --- Control 4: Cancel button ---
        // Style: WS_CHILD | WS_VISIBLE | WS_TABSTOP | BS_PUSHBUTTON
        bw.Write((uint)(0x40000000 | 0x10000000 | 0x00010000 | 0x00000000));
        bw.Write((uint)0);
        bw.Write((short)210); // x
        bw.Write((short)65);  // y
        bw.Write((short)70);  // cx
        bw.Write((short)25);  // cy
        bw.Write((ushort)Idcancel);
        bw.Write((ushort)0xFFFF);
        bw.Write((ushort)0x0080);
        foreach (char c in "Cancel") bw.Write((short)c);
        bw.Write((short)0);
        bw.Write((ushort)0);

        // The entire template must be DWORD-aligned. Pad the final byte count up to a multiple of 4.
        long length = ms.Length;
        int pad = (int)(4 - (length % 4)) % 4;
        for (int i = 0; i < pad; i++) bw.Write((byte)0);
        return ms.ToArray();
    }

    /// <summary>
    /// Checks whether a font with the given face name is installed on the system.
    /// Uses EnumFontFamilies to enumerate all available fonts and compares names case-insensitively.
    /// </summary>
    private static bool IsFontAvailable(string fontName)
    {
        if (string.IsNullOrWhiteSpace(fontName)) return false;
        IntPtr screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero) return false;
        try
        {
            _fontSearchTarget = fontName;
            _fontSearchFound = false;
            IntPtr procPtr = Marshal.GetFunctionPointerForDelegate(FontEnumProc);
            // Passing IntPtr.Zero as lpszFamily enumerates ALL fonts on the system.
            EnumFontFamilies(screenDc, IntPtr.Zero, procPtr, IntPtr.Zero);
            return _fontSearchFound;
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screenDc);
            _fontSearchTarget = null;
        }
    }

    private static string? _fontSearchTarget;
    private static bool _fontSearchFound;

    private delegate int EnumFontFamProcDelegate(ref EnumLogFont lpelf, ref NewTextMetric lpntm, uint fontType, IntPtr lParam);
    private static readonly EnumFontFamProcDelegate FontEnumProc = FontEnumProcImpl;

    private static int FontEnumProcImpl(ref EnumLogFont lpelf, ref NewTextMetric lpntm, uint fontType, IntPtr lParam)
    {
        // Compare the font face name case-insensitively.
        if (_fontSearchTarget != null)
        {
            string faceName = lpelf.elfLogFont.lfFaceName;
            // Trim at the first null char (the field is fixed-size).
            int nullIdx = faceName.IndexOf('\0');
            if (nullIdx >= 0) faceName = faceName.Substring(0, nullIdx);
            if (string.Equals(faceName, _fontSearchTarget, StringComparison.OrdinalIgnoreCase))
            {
                _fontSearchFound = true;
                return 0;  // stop enumeration
            }
        }
        return 1;  // continue enumeration
    }

    // ---- Wallpaper color extraction (Material You style) ----

    /// <summary>
    /// Extracts the dominant color from the current desktop wallpaper and sets _textColor
    /// to a lightened version of it (so the text remains readable on dark backgrounds).
    /// </summary>
    private static void RefreshWallpaperColor()
    {
        try
        {
            string? wallpaperPath = GetWallpaperPath();
            if (string.IsNullOrEmpty(wallpaperPath) || !File.Exists(wallpaperPath))
            {
                // No wallpaper file (e.g. solid color background). Fall back to default.
                _textColor = DefaultTextColor;
                return;
            }

            // Always re-extract the color from the current wallpaper. This ensures that
            // when the user changes their desktop wallpaper, the clock color updates
            // accordingly (within the resample interval). We don't cache by path alone
            // because the same path may point to different content over time (e.g. the
            // user replaces the file, or Windows updates the TranscodedWallpaper cache).

            // Load the image, downsample to 64x64, compute the dominant color via a simple
            // histogram-based algorithm (similar to Android's Monet extraction).
            uint dominantColor = ExtractDominantColor(wallpaperPath);
            // Lighten the dominant color so text is readable on any background.
            // We convert to HSL, boost lightness to ~0.85, and convert back.
            _textColor = LightenForText(dominantColor);
        }
        catch
        {
            // Image loading failed — fall back to default.
            _textColor = DefaultTextColor;
        }
    }

    /// <summary>
    /// Returns the path to the current desktop wallpaper via SystemParametersInfoW(SPI_GETDESKWALLPAPER).
    /// </summary>
    private static string? GetWallpaperPath()
    {
        const uint SpiGetDeskWallpaper = 0x0073;
        var sb = new System.Text.StringBuilder(260);
        if (SystemParametersInfoGetString(SpiGetDeskWallpaper, (uint)sb.Capacity, sb, 0))
        {
            string path = sb.ToString();
            return string.IsNullOrEmpty(path) ? null : path;
        }
        return null;
    }

    /// <summary>
    /// Loads an image file, downsamples it to a small grid (e.g. 64x64), and computes the
    /// dominant color using a hue histogram. Returns the dominant color as an RGB uint.
    /// </summary>
    private static uint ExtractDominantColor(string imagePath)
    {
        // Use System.Drawing.Common to load the image. This is available in .NET 8 on Windows
        // as long as the project targets net*-windows (which we do).
        // We don't reference System.Drawing.Common explicitly, but it's part of the Windows
        // runtime. Fall back to a manual BMP parser if the assembly isn't available.
        try
        {
            using var fs = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            // Load via System.Drawing.Image (requires the System.Drawing.Common package on non-Windows,
            // but on Windows net8.0-windows it's available via the Windows Desktop runtime).
            // We use reflection to avoid a hard dependency; if the type isn't available we fall back.
            var imageType = Type.GetType("System.Drawing.Image, System.Drawing.Common");
            if (imageType != null)
            {
                var fromStream = imageType.GetMethod("FromStream", new[] { typeof(Stream) });
                if (fromStream != null)
                {
                    var image = fromStream.Invoke(null, new object[] { fs });
                    if (image != null)
                    {
                        try
                        {
                            return ExtractDominantColorFromImage(image, imageType);
                        }
                        finally
                        {
                            var dispose = image.GetType().GetMethod("Dispose", Type.EmptyTypes);
                            dispose?.Invoke(image, null);
                        }
                    }
                }
            }
        }
        catch
        {
            // Fall through to default.
        }
        return DefaultTextColor;
    }

    private static uint ExtractDominantColorFromImage(object image, Type imageType)
    {
        // Downsample to 64x64 via System.Drawing.Bitmap constructor.
        var bitmapType = Type.GetType("System.Drawing.Bitmap, System.Drawing.Common");
        if (bitmapType == null) return DefaultTextColor;

        // Create a 64x64 Bitmap and draw the image into it.
        var sizeType = Type.GetType("System.Drawing.Size, System.Drawing.Common");
        var ctor = bitmapType.GetConstructor(new[] { typeof(int), typeof(int) });
        if (ctor == null) return DefaultTextColor;
        var small = ctor.Invoke(new object[] { 64, 64 });
        if (small == null) return DefaultTextColor;

        try
        {
            // Get a Graphics object from the small bitmap.
            var graphicsType = Type.GetType("System.Drawing.Graphics, System.Drawing.Common");
            if (graphicsType == null) return DefaultTextColor;
            var fromImage = graphicsType.GetMethod("FromImage", new[] { imageType });
            if (fromImage == null) return DefaultTextColor;
            var g = fromImage.Invoke(null, new object[] { small });
            if (g == null) return DefaultTextColor;

            try
            {
                // Draw the original image scaled into the 64x64 bitmap.
                var drawImage = graphicsType.GetMethod("DrawImage", new[] { imageType, typeof(int), typeof(int), typeof(int), typeof(int) });
                drawImage?.Invoke(g, new object[] { image, 0, 0, 64, 64 });
            }
            finally
            {
                var dispose = graphicsType.GetMethod("Dispose", Type.EmptyTypes);
                dispose?.Invoke(g, null);
            }

            // Build a hue histogram from the 64x64 bitmap.
            // We use GetPixel(x, y) which returns a System.Drawing.Color.
            var getPixel = bitmapType.GetMethod("GetPixel", new[] { typeof(int), typeof(int) });
            if (getPixel == null) return DefaultTextColor;

            // Hue buckets: 36 buckets of 10 degrees each.
            var hueCounts = new int[36];
            var hueR = new long[36];
            var hueG = new long[36];
            var hueB = new long[36];
            int totalSamples = 0;

            var colorType = Type.GetType("System.Drawing.Color, System.Drawing.Common");
            if (colorType == null) return DefaultTextColor;
            var getHue = colorType.GetProperty("GetHue")?.GetMethod ?? colorType.GetMethod("GetHue", Type.EmptyTypes);
            var getR = colorType.GetProperty("R")?.GetMethod;
            var getG = colorType.GetProperty("G")?.GetMethod;
            var getB = colorType.GetProperty("B")?.GetMethod;
            var getBrightness = colorType.GetProperty("GetBrightness")?.GetMethod ?? colorType.GetMethod("GetBrightness", Type.EmptyTypes);
            var getSaturation = colorType.GetProperty("GetSaturation")?.GetMethod ?? colorType.GetMethod("GetSaturation", Type.EmptyTypes);
            if (getHue == null || getR == null || getG == null || getB == null) return DefaultTextColor;

            for (int y = 0; y < 64; y++)
            {
                for (int x = 0; x < 64; x++)
                {
                    var color = getPixel.Invoke(small, new object[] { x, y });
                    if (color == null) continue;
                    float hue = (float)getHue.Invoke(color, null)!;
                    float brightness = getBrightness != null ? (float)getBrightness.Invoke(color, null)! : 0.5f;
                    float saturation = getSaturation != null ? (float)getSaturation.Invoke(color, null)! : 0.5f;
                    // Skip very dark, very bright, or very desaturated pixels — they don't contribute
                    // meaningful hue information for a "dominant color" extraction.
                    if (brightness < 0.1f || brightness > 0.9f || saturation < 0.15f) continue;
                    int bucket = (int)(hue / 10.0f);
                    if (bucket >= 36) bucket = 35;
                    if (bucket < 0) bucket = 0;
                    hueCounts[bucket]++;
                    hueR[bucket] += (byte)getR.Invoke(color, null)!;
                    hueG[bucket] += (byte)getG.Invoke(color, null)!;
                    hueB[bucket] += (byte)getB.Invoke(color, null)!;
                    totalSamples++;
                }
            }

            if (totalSamples == 0) return DefaultTextColor;

            // Find the hue bucket with the most samples.
            int bestBucket = 0;
            int bestCount = 0;
            for (int i = 0; i < 36; i++)
            {
                if (hueCounts[i] > bestCount)
                {
                    bestCount = hueCounts[i];
                    bestBucket = i;
                }
            }

            if (hueCounts[bestBucket] == 0) return DefaultTextColor;

            // Average color within the winning bucket.
            byte avgR = (byte)(hueR[bestBucket] / hueCounts[bestBucket]);
            byte avgG = (byte)(hueG[bestBucket] / hueCounts[bestBucket]);
            byte avgB = (byte)(hueB[bestBucket] / hueCounts[bestBucket]);
            return Rgb(avgR, avgG, avgB);
        }
        finally
        {
            var dispose = bitmapType.GetMethod("Dispose", Type.EmptyTypes);
            dispose?.Invoke(small, null);
        }
    }

    /// <summary>
    /// Lightens an RGB color so it's suitable as text on a dark/transparent background.
    /// Converts to HSL, sets lightness to 0.85, keeps hue and saturation, converts back.
    /// </summary>
    private static uint LightenForText(uint color)
    {
        byte r = (byte)(color & 0xFF);
        byte g = (byte)((color >> 8) & 0xFF);
        byte b = (byte)((color >> 16) & 0xFF);
        RgbToHsl(r, g, b, out double h, out double s, out _);
        // Target lightness 0.85 — bright but not pure white. Clamp saturation to at least 0.4
        // so the color stays vivid (avoid washed-out gray).
        if (s < 0.4) s = 0.4;
        HslToRgb(h, s, 0.85, out byte nr, out byte ng, out byte nb);
        return Rgb(nr, ng, nb);
    }

    private static void RgbToHsl(byte r, byte g, byte b, out double h, out double s, out double l)
    {
        double rf = r / 255.0, gf = g / 255.0, bf = b / 255.0;
        double max = Math.Max(rf, Math.Max(gf, bf));
        double min = Math.Min(rf, Math.Min(gf, bf));
        double delta = max - min;
        l = (max + min) / 2.0;
        if (delta == 0)
        {
            h = 0;
            s = 0;
        }
        else
        {
            s = delta / (1.0 - Math.Abs(2.0 * l - 1.0));
            if (max == rf) h = ((gf - bf) / delta) % 6;
            else if (max == gf) h = (bf - rf) / delta + 2;
            else h = (rf - gf) / delta + 4;
            h *= 60;
            if (h < 0) h += 360;
        }
    }

    private static void HslToRgb(double h, double s, double l, out byte r, out byte g, out byte b)
    {
        double c = (1.0 - Math.Abs(2.0 * l - 1.0)) * s;
        double x = c * (1.0 - Math.Abs((h / 60.0) % 2 - 1.0));
        double m = l - c / 2.0;
        double rp, gp, bp;
        if (h < 60)       { rp = c; gp = x; bp = 0; }
        else if (h < 120) { rp = x; gp = c; bp = 0; }
        else if (h < 180) { rp = 0; gp = c; bp = x; }
        else if (h < 240) { rp = 0; gp = x; bp = c; }
        else if (h < 300) { rp = x; gp = 0; bp = c; }
        else              { rp = c; gp = 0; bp = x; }
        r = (byte)Math.Round((rp + m) * 255);
        g = (byte)Math.Round((gp + m) * 255);
        b = (byte)Math.Round((bp + m) * 255);
    }

    // ---- Settings persistence (settings.json in %APPDATA%/LightClock/) ----

    private static string GetSettingsPath()
    {
        if (_settingsPath != null) return _settingsPath;
        string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string dir = Path.Combine(baseDir, "LightClock");
        Directory.CreateDirectory(dir);
        _settingsPath = Path.Combine(dir, "settings.json");
        return _settingsPath;
    }

    private static void LoadSettings()
    {
        try
        {
            string path = GetSettingsPath();
            if (!File.Exists(path)) return;
            string json = File.ReadAllText(path);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("language", out var langEl) && langEl.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                string lang = langEl.GetString() ?? "en";
                if (lang == "en" || lang == "zh") _language = lang;
            }
            if (root.TryGetProperty("timeFont", out var fontEl) && fontEl.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                string font = fontEl.GetString() ?? "Consolas";
                // For preset fonts, set the matching pitch/family. For custom (user-entered) font
                // names, use the default Swiss pitch/family — the font name is preserved as-is.
                switch (font)
                {
                    case "Segoe UI": _timeFontName = font; _timeFontPitchAndFamily = PitchAndFamilySwiss; break;
                    case "Consolas": _timeFontName = font; _timeFontPitchAndFamily = PitchAndFamilyModern; break;
                    case "Cascadia Code": _timeFontName = font; _timeFontPitchAndFamily = PitchAndFamilyModern; break;
                    case "Microsoft YaHei": _timeFontName = font; _timeFontPitchAndFamily = PitchAndFamilySwiss; break;
                    default:
                        // Custom font name — accept it as-is. We don't validate here because the
                        // font might have been installed after the setting was saved; if it's
                        // missing, GDI will fall back to a default font at render time.
                        _timeFontName = font;
                        _timeFontPitchAndFamily = PitchAndFamilySwiss;
                        break;
                }
            }
            if (root.TryGetProperty("autoStart", out var autoEl) && autoEl.ValueKind == System.Text.Json.JsonValueKind.True)
            {
                _autoStart = true;
            }
            if (root.TryGetProperty("useWallpaperColor", out var wpEl) && wpEl.ValueKind == System.Text.Json.JsonValueKind.True)
            {
                _useWallpaperColor = true;
                // Sample the wallpaper now so the first render uses the extracted color.
                RefreshWallpaperColor();
            }
            // Sync the registry entry with the loaded preference. If the user previously enabled
            // auto-start from the menu but later removed the entry manually (or via Task Manager),
            // we re-create it so the menu checkmark stays consistent with reality.
            if (_autoStart) SetAutoStart(true);
        }
        catch
        {
            // Corrupt settings file — silently fall back to defaults.
        }
    }

    private static void SaveSettings()
    {
        try
        {
            string path = GetSettingsPath();
            var obj = new Dictionary<string, object>
            {
                ["language"] = _language,
                ["timeFont"] = _timeFontName,
                ["autoStart"] = _autoStart,
                ["useWallpaperColor"] = _useWallpaperColor
            };
            string json = System.Text.Json.JsonSerializer.Serialize(obj, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch
        {
            // Settings are best-effort — ignore write failures (e.g. read-only AppData).
        }
    }

    // ---- Auto-start via HKCU\Software\Microsoft\Windows\CurrentVersion\Run ----

    private const string AutoStartRegistryKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
    private const string AutoStartValueName = "LightClock";
    private static readonly IntPtr HkeyCurrentUser = new IntPtr(unchecked((long)0x80000001));

    private static void SetAutoStart(bool enable)
    {
        try
        {
            // KEY_SET_VALUE (0x0002) is all we need for both RegSetValueEx and RegDeleteValue.
            // Requesting KEY_ALL_ACCESS can fail on locked-down systems where the user has
            // restricted registry permissions, even for their own HKCU.
            const int keyValue = 0x0002;
            int result = RegOpenKeyEx(HkeyCurrentUser, AutoStartRegistryKey, 0, keyValue, out IntPtr hKey);
            if (result != 0)
            {
                // Could not open the Run key. SaveSettings() will still persist the preference
                // to settings.json, so on next launch LoadSettings() will retry via the
                // 'if (_autoStart) SetAutoStart(true)' call.
                return;
            }
            try
            {
                if (enable)
                {
                    // Quote the path in case it contains spaces. Use the .exe (not .dll) so Windows
                    // launches the native host instead of the dotnet runtime host.
                    // Environment.ProcessPath returns the full path to the currently-executing .exe.
                    string? exePath = Environment.ProcessPath;
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        string value = "\"" + exePath + "\"";
                        // RegSetValueExW expects UTF-16 bytes including the null terminator.
                        // The previous P/Invoke signature took 'string lpData' which caused
                        // the marshaler to pass a pointer without the correct cbData length,
                        // leading to either a silent write failure or a crash when Windows
                        // tried to read past the buffer.
                        byte[] data = System.Text.Encoding.Unicode.GetBytes(value + "\0");
                        int setResult = RegSetValueEx(hKey, AutoStartValueName, 0, 1 /* REG_SZ */, data, (uint)data.Length);
                        if (setResult != 0)
                        {
                            // Write failed — settings.json still records the intent; LoadSettings
                            // will retry on next launch.
                        }
                    }
                }
                else
                {
                    // RegDeleteValue returns 2 (ERROR_FILE_NOT_FOUND) if the value doesn't exist,
                    // which is fine for our "disable" semantics — treat as success.
                    RegDeleteValue(hKey, AutoStartValueName);
                }
            }
            finally
            {
                RegCloseKey(hKey);
            }
        }
        catch
        {
            // Auto-start is best-effort — ignore registry failures. The user's intent is still
            // persisted in settings.json, so we'll retry on next launch.
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WndClassEx
    {
        public uint cbSize;
        public uint style;
        public WndProcDelegate lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public Point pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Size
    {
        public int cx;
        public int cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PaintStruct
    {
        public IntPtr hdc;
        public int fErase;
        public Rect rcPaint;
        public int fRestore;
        public int fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] rgbReserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader bmiHeader;
        // No color table for 32-bit DIB.
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPos
    {
        public IntPtr hwnd;
        public IntPtr hwndInsertAfter;
        public int x;
        public int y;
        public int cx;
        public int cy;
        public uint flags;
    }

    private sealed class GdiObject : IDisposable
    {
        public IntPtr Handle { get; }

        public GdiObject(IntPtr handle)
        {
            Handle = handle;
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero)
            {
                DeleteObject(Handle);
            }
        }
    }

    private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx([In] ref WndClassEx lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetMessage(out Msg lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage([In] ref Msg lpmsg);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage([In] ref Msg lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(
        IntPtr hwnd,
        IntPtr hdcDst,
        ref Point pptDst,
        ref Size psize,
        IntPtr hdcSrc,
        ref Point pptSrc,
        uint crKey,
        ref BlendFunction pblend,
        uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, int uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetTimer(IntPtr hWnd, int nIDEvent, int uElapse, IntPtr lpTimerFunc);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool KillTimer(IntPtr hWnd, int uIDEvent);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int nExitCode);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr BeginPaint(IntPtr hWnd, out PaintStruct lpPaint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EndPaint(IntPtr hWnd, [In] ref PaintStruct lpPaint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetClientRect(IntPtr hWnd, out Rect lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateSolidBrush(uint colorRef);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int FillRect(IntPtr hDC, [In] ref Rect lprc, IntPtr hbr);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int SetBkMode(IntPtr hdc, int mode);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern uint SetBkColor(IntPtr hdc, uint color);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern uint SetTextColor(IntPtr hdc, uint color);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFont(
        int cHeight,
        int cWidth,
        int cEscapement,
        int cOrientation,
        int cWeight,
        uint bItalic,
        uint bUnderline,
        uint bStrikeOut,
        uint iCharSet,
        uint iOutPrecision,
        uint iClipPrecision,
        uint iQuality,
        uint iPitchAndFamily,
        string pszFaceName);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int DrawText(IntPtr hdc, string lpchText, int cchText, ref Rect lprc, uint format);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, IntPtr uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "TrackPopupMenu")]
    private static extern int TrackPopupMenuRaw(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorPos(out Point lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr LoadCursor(IntPtr hInstance, IntPtr lpCursorName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImage(IntPtr hInst, string name, uint type, int cx, int cy, uint fuLoad);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref Rect pvParam, uint fWinIni);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int RegOpenKeyEx(IntPtr hKey, string lpSubKey, uint ulOptions, int samDesired, out IntPtr phkResult);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "RegSetValueExW")]
    private static extern int RegSetValueEx(IntPtr hKey, string lpValueName, uint reserved, uint dwType, byte[] lpData, uint cbData);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int RegDeleteValue(IntPtr hKey, string lpValueName);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern int RegCloseKey(IntPtr hKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr hDC);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BitmapInfo pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint dwOffset);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern void RtlZeroMemory(IntPtr destination, nuint length);

    // ---- P/Invokes for the custom-font input dialog ----

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int DialogBoxIndirectParam(IntPtr hInstance, IntPtr hTemplate, IntPtr hWndParent, IntPtr lpDialogFunc, IntPtr dwInitParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EndDialog(IntPtr hDlg, int nResult);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDlgItemText(IntPtr hDlg, int nIDDlgItem, string lpString);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetDlgItemText(IntPtr hDlg, int nIDDlgItem, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendDlgItemMessage(IntPtr hDlg, int nIDDlgItem, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetDlgItem(IntPtr hDlg, int nIDDlgItem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    // ---- P/Invokes for font enumeration ----

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int EnumFontFamilies(IntPtr hdc, IntPtr lpszFamily, IntPtr lpEnumFontFamProc, IntPtr lParam);

    // ---- P/Invoke for wallpaper path ----

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfoGetString(uint uiAction, uint uiParam, System.Text.StringBuilder pvParam, uint fWinIni);

    // ---- Font-related structures ----

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct LogFont
    {
        public int lfHeight;
        public int lfWidth;
        public int lfEscapement;
        public int lfOrientation;
        public int lfWeight;
        public byte lfItalic;
        public byte lfUnderline;
        public byte lfStrikeOut;
        public byte lfCharSet;
        public byte lfOutPrecision;
        public byte lfClipPrecision;
        public byte lfQuality;
        public byte lfPitchAndFamily;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string lfFaceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct EnumLogFont
    {
        public LogFont elfLogFont;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string elfFullName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string elfStyle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NewTextMetric
    {
        public int tmHeight;
        public int tmAscent;
        public int tmDescent;
        public int tmInternalLeading;
        public int tmExternalLeading;
        public int tmAveCharWidth;
        public int tmMaxCharWidth;
        public int tmWeight;
        public int tmOverhang;
        public int tmDigitizedAspectX;
        public int tmDigitizedAspectY;
        public byte tmFirstChar;
        public byte tmLastChar;
        public byte tmDefaultChar;
        public byte tmBreakChar;
        public byte tmItalic;
        public byte tmUnderlined;
        public byte tmStruckOut;
        public byte tmPitchAndFamily;
        public byte tmCharSet;
        public int ntmFlags;
        public uint ntmSizeEM;
        public uint ntmCellHeight;
        public uint ntmAvgWidth;
    }
}
