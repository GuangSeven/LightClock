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
    private const int CmdToggleAutoStart = 1020;

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
    private const uint PitchAndFamilySwiss = 0x22;  // VARIABLE_PITCH (0x02) | FF_SWISS (0x20) — matches Segoe UI's category
    private const uint PitchAndFamilyModern = 0x31;  // FIXED_PITCH (0x01) | FF_MODERN (0x30) — used for monospaced time font

    private static readonly IntPtr HwndTopmost = new(-1);
    private static readonly IntPtr HwndNotTopmost = new(-2);

    private static readonly uint TextColor = Rgb(195, 236, 244);

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

    // Cached 32-bit ARGB DIB section + its DC + old bitmap, used for per-pixel-alpha rendering.
    // Recreated only when window size or DPI changes.
    private static IntPtr _memDc = IntPtr.Zero;
    private static IntPtr _dib = IntPtr.Zero;
    private static IntPtr _oldDib = IntPtr.Zero;
    private static int _dibWidth;
    private static int _dibHeight;
    private static byte[]? _dibBitsBacking;

    private const int DpiDateFontHeight = 56;
    private const int DpiTimeFontHeight = 168;

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
                else if (commandId == CmdToggleAutoStart)
                {
                    _autoStart = !_autoStart;
                    SetAutoStart(_autoStart);
                    SaveSettings();
                }
                return IntPtr.Zero;

            case WmPaint:
                Paint(hwnd);
                return IntPtr.Zero;

            case WmEraseBkgnd:
                return (IntPtr)1;

            case WmDpiChanged:
                // wParam = new DPI (HIWORD = y dpi, LOWORD = x dpi). lParam = suggested rect.
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
                    RenderAndPresent(hwnd);
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
                if (wParam != IntPtr.Zero)
                {
                    var wp = Marshal.PtrToStructure<WindowPos>(wParam);
                    if ((wp.flags & SwpHideWindow) != 0)
                    {
                        wp.flags &= ~(uint)SwpHideWindow;
                        Marshal.StructureToPtr(wp, wParam, fDeleteOld: false);
                    }
                }
                return IntPtr.Zero;

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
        if (_memDc == IntPtr.Zero || _dib == IntPtr.Zero)
        {
            return;
        }

        // Clear the DIB to fully transparent (all zero).
        // 32-bit ARGB DIB layout is BGRA, 4 bytes per pixel, premultiplied alpha.
        // For fully transparent black (0,0,0,0), the bytes are all 0.
        if (_dibBitsBacking != null)
        {
            Array.Clear(_dibBitsBacking, 0, _dibBitsBacking.Length);
        }

        // Lazily create fonts once and reuse them across paints to avoid leaking GDI handles.
        // Scale by current DPI so the clock stays readable when dragged between monitors with different DPI.
        int dateHeight = MulDiv(DpiDateFontHeight, _dpi, 96);
        int timeHeight = MulDiv(DpiTimeFontHeight, _dpi, 96);
        _dateFontHandle = _dateFontHandle == IntPtr.Zero
            ? CreateFont(dateHeight, 0, 0, 0, 400, 0, 0, 0, DefaultCharset, OutTtPrecis, ClipDefaultPrecis, ProofQuality, PitchAndFamilySwiss, "Segoe UI")
            : _dateFontHandle;
        // Time font uses Consolas (monospaced) so each digit takes the same width.
        // With Segoe UI (proportional), "1" is narrower than "8" — when the minute changes
        // from e.g. 11:58 to 11:59, the text width shifts and DT_CENTER re-centers it,
        // causing a visible horizontal "jump". A monospaced font eliminates this entirely.
        _timeFontHandle = _timeFontHandle == IntPtr.Zero
            ? CreateFont(timeHeight, 0, 0, 0, 600, 0, 0, 0, DefaultCharset, OutTtPrecis, ClipDefaultPrecis, ProofQuality, _timeFontPitchAndFamily, _timeFontName)
            : _timeFontHandle;

        // Set up the memory DC for text rendering.
        // Note: GDI text rendering on a 32-bit DIB produces straight-alpha (non-premultiplied)
        // pixels by default. For UpdateLayeredWindow with AC_SRC_ALPHA, the bitmap must be
        // premultiplied. We use SetTextColor + SetBkMode(TRANSPARENT) and then post-process
        // the DIB bits to convert from straight to premultiplied alpha.
        SetBkMode(_memDc, TransparentBkMode);
        SetTextColor(_memDc, TextColor);

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

        // Convert straight-alpha to premultiplied-alpha in-place.
        // GDI leaves the DIB with: B, G, R, A where A is the alpha from text anti-aliasing
        // (0 where background, 255 where glyph interior, intermediate at edges).
        // We need premultiplied: B*A/255, G*A/255, R*A/255, A.
        if (_dibBitsBacking != null)
        {
            for (int i = 0; i < _dibBitsBacking.Length; i += 4)
            {
                byte b = _dibBitsBacking[i];
                byte g = _dibBitsBacking[i + 1];
                byte r = _dibBitsBacking[i + 2];
                byte a = _dibBitsBacking[i + 3];
                if (a == 0)
                {
                    _dibBitsBacking[i] = 0;
                    _dibBitsBacking[i + 1] = 0;
                    _dibBitsBacking[i + 2] = 0;
                }
                else if (a < 255)
                {
                    _dibBitsBacking[i] = (byte)((b * a) / 255);
                    _dibBitsBacking[i + 1] = (byte)((g * a) / 255);
                    _dibBitsBacking[i + 2] = (byte)((r * a) / 255);
                }
                // a stays the same
            }
        }

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
        UpdateLayeredWindow(
            hwnd,
            IntPtr.Zero,        // hdcDst - screen DC (NULL = default)
            IntPtr.Zero,        // pptDst - NULL = keep current position
            ref size,           // psize - new window size
            _memDc,             // hdcSrc - source DC
            ref zeroPt,         // pptSrc - source origin
            0,                  // crKey - color key (unused with ULW_ALPHA)
            ref blend,
            UlwAlpha);
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

            _oldDib = SelectObject(_memDc, _dib);

            // Create a managed byte[] view over the DIB bits so we can clear and post-process them.
            int byteCount = width * height * 4;
            _dibBitsBacking = new byte[byteCount];
            Marshal.Copy(bits, _dibBitsBacking, 0, byteCount);

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
            AppendMenu(langMenu, MfString | (_language == "en" ? MfChecked : MfUnchecked), CmdLanguageEn, "English");
            AppendMenu(langMenu, MfString | (_language == "zh" ? MfChecked : MfUnchecked), CmdLanguageZh, "中文 (Chinese)");
            AppendMenu(menu, MfString | 0x0010 /* MF_POPUP */, (uint)langMenu.ToInt64(), "Language");

            // Font submenu (affects time display only; date always uses Segoe UI for proportional look)
            IntPtr fontMenu = CreatePopupMenu();
            AppendMenu(fontMenu, MfString | (_timeFontName == "Segoe UI" ? MfChecked : MfUnchecked), CmdFontSegoUi, "Segoe UI");
            AppendMenu(fontMenu, MfString | (_timeFontName == "Consolas" ? MfChecked : MfUnchecked), CmdFontConsolas, "Consolas");
            AppendMenu(fontMenu, MfString | (_timeFontName == "Cascadia Code" ? MfChecked : MfUnchecked), CmdFontCascadiaCode, "Cascadia Code");
            AppendMenu(fontMenu, MfString | (_timeFontName == "Microsoft YaHei" ? MfChecked : MfUnchecked), CmdFontMicrosoftYaHei, "Microsoft YaHei");
            AppendMenu(menu, MfString | 0x0010 /* MF_POPUP */, (uint)fontMenu.ToInt64(), "Font");

            // Start with Windows (toggle)
            AppendMenu(menu, MfString | (_autoStart ? MfChecked : MfUnchecked), CmdToggleAutoStart, "Start with Windows");

            // Exit
            AppendMenu(menu, MfSeparator, 0, null);
            AppendMenu(menu, MfString, CmdExit, "Exit");

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
            // Chinese: use the system's zh-CN culture if available, otherwise fall back to invariant.
            try
            {
                var ci = new System.Globalization.CultureInfo("zh-CN");
                _dateText = now.ToString("M月d日 dddd", ci);
                _timeText = now.ToString("HH:mm", ci);
            }
            catch
            {
                // CultureInfo not available — fall back to English.
                _dateText = now.ToString("dddd, MMMM d");
                _timeText = now.ToString("HH:mm");
            }
        }
        else
        {
            _dateText = now.ToString("dddd, MMMM d");
            _timeText = now.ToString("HH:mm");
        }
    }

    private static int LowWord(IntPtr value) => (int)((uint)(long)value & 0xFFFF);

    private static int MulDiv(int nNumber, int nNumerator, int nDenominator)
        => (int)(((long)nNumber * nNumerator) / nDenominator);

    private static uint Rgb(byte r, byte g, byte b) => (uint)(r | (g << 8) | (b << 16));

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
                switch (font)
                {
                    case "Segoe UI": _timeFontName = font; _timeFontPitchAndFamily = PitchAndFamilySwiss; break;
                    case "Consolas": _timeFontName = font; _timeFontPitchAndFamily = PitchAndFamilyModern; break;
                    case "Cascadia Code": _timeFontName = font; _timeFontPitchAndFamily = PitchAndFamilyModern; break;
                    case "Microsoft YaHei": _timeFontName = font; _timeFontPitchAndFamily = PitchAndFamilySwiss; break;
                }
            }
            if (root.TryGetProperty("autoStart", out var autoEl) && autoEl.ValueKind == System.Text.Json.JsonValueKind.True)
            {
                _autoStart = true;
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
                ["autoStart"] = _autoStart
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
            int result = RegOpenKeyEx(HkeyCurrentUser, AutoStartRegistryKey, 0, 0x000F003F /* KEY_ALL_ACCESS */, out IntPtr hKey);
            if (result != 0) return;
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
                        RegSetValueEx(hKey, AutoStartValueName, 0, 1 /* REG_SZ */, value);
                    }
                }
                else
                {
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
            // Auto-start is best-effort — ignore registry failures.
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
        IntPtr pptDst,
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

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateSolidBrush(uint colorRef);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int FillRect(IntPtr hDC, [In] ref Rect lprc, IntPtr hbr);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int SetBkMode(IntPtr hdc, int mode);

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
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, uint uIDNewItem, string? lpNewItem);

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

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int RegOpenKeyEx(IntPtr hKey, string lpSubKey, uint ulOptions, int samDesired, out IntPtr phkResult);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "RegSetValueExW")]
    private static extern int RegSetValueEx(IntPtr hKey, string lpValueName, uint reserved, uint dwType, string lpData);

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
}
