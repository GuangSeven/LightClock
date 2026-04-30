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
    private const uint TpmReturnCmd = 0x0100;

    private const int SwpNomove = 0x0002;
    private const int SwpNosize = 0x0001;
    private const int SwpShowWindow = 0x0040;

    private const uint LwaColorKey = 0x00000001;
    private const uint MfString = 0x00000000;
    private const uint MfSeparator = 0x00000800;
    private const uint MfChecked = 0x00000008;
    private const uint MfUnchecked = 0x00000000;

    private const uint DtCenter = 0x00000001;
    private const uint DtVCenter = 0x00000004;
    private const uint DtSingleLine = 0x00000020;

    private const int CmdToggleTopMost = 1001;
    private const int CmdExit = 1002;

    private const int WmCreate = 0x0001;
    private const int WmDestroy = 0x0002;
    private const int WmPaint = 0x000F;
    private const int WmClose = 0x0010;
    private const int WmCommand = 0x0111;
    private const int WmTimer = 0x0113;
    private const int WmEraseBkgnd = 0x0014;
    private const int WmRButtonUp = 0x0205;
    private const int WmNCHitTest = 0x0084;

    private const int HtCaption = 2;

    private static readonly IntPtr HwndTopmost = new(-1);
    private static readonly IntPtr HwndNotTopmost = new(-2);

    private static readonly uint TransparentColor = Rgb(0, 0, 0);
    private static readonly uint TextColor = Rgb(195, 236, 244);

    private static readonly WndProcDelegate WndProcRef = WndProc;
    private static bool _alwaysOnTop = true;

    private static string _dateText = string.Empty;
    private static string _timeText = string.Empty;

    [STAThread]
    private static void Main()
    {
        var hInstance = GetModuleHandle(null);
        if (hInstance == IntPtr.Zero)
        {
            return;
        }

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        IntPtr hIcon = IntPtr.Zero;
        if (File.Exists(iconPath))
        {
            hIcon = LoadImage(IntPtr.Zero, iconPath, ImageIcon, 0, 0, LrLoadFromFile);
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

        SetLayeredWindowAttributes(hwnd, TransparentColor, 255, LwaColorKey);
        SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, SwpNomove | SwpNosize | SwpShowWindow);

        UpdateClockText();
        InvalidateRect(hwnd, IntPtr.Zero, true);

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
                    InvalidateRect(hwnd, IntPtr.Zero, true);
                }
                return IntPtr.Zero;

            case WmRButtonUp:
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
                return IntPtr.Zero;

            case WmPaint:
                Paint(hwnd);
                return IntPtr.Zero;

            case WmEraseBkgnd:
                return (IntPtr)1;

            case WmNCHitTest:
                return (IntPtr)HtCaption;

            case WmDestroy:
                KillTimer(hwnd, TimerId);
                PostQuitMessage(0);
                return IntPtr.Zero;
        }

        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private static void Paint(IntPtr hwnd)
    {
        var hdc = BeginPaint(hwnd, out var ps);
        if (hdc == IntPtr.Zero)
        {
            return;
        }
        try
        {
            GetClientRect(hwnd, out var rect);

            using var bgBrush = new GdiObject(CreateSolidBrush(TransparentColor));
            FillRect(ps.hdc, ref rect, bgBrush.Handle);

            SetBkMode(ps.hdc, 1);
            SetTextColor(ps.hdc, TextColor);

            using var dateFont = new GdiObject(CreateFont(
                56, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 4, 0, "Segoe UI"));
            using var timeFont = new GdiObject(CreateFont(
                168, 0, 0, 0, 600, 0, 0, 0, 1, 0, 0, 4, 0, "Segoe UI"));

            IntPtr oldFont = SelectObject(ps.hdc, dateFont.Handle);
            var dateRect = new Rect
            {
                left = rect.left,
                right = rect.right,
                top = rect.top + 18,
                bottom = rect.top + 120
            };
            DrawText(ps.hdc, _dateText, _dateText.Length, ref dateRect, DtCenter | DtVCenter | DtSingleLine);

            SelectObject(ps.hdc, timeFont.Handle);
            var timeRect = new Rect
            {
                left = rect.left,
                right = rect.right,
                top = rect.top + 92,
                bottom = rect.bottom
            };
            DrawText(ps.hdc, _timeText, _timeText.Length, ref timeRect, DtCenter | DtVCenter | DtSingleLine);
            SelectObject(ps.hdc, oldFont);
        }
        finally
        {
            EndPaint(hwnd, ref ps);
        }
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
            AppendMenu(menu, MfString | (_alwaysOnTop ? MfChecked : MfUnchecked), CmdToggleTopMost, "Always on Top");
            AppendMenu(menu, MfSeparator, 0, null);
            AppendMenu(menu, MfString, CmdExit, "Exit");

            GetCursorPos(out var point);
            SetForegroundWindow(hwnd);
            TrackPopupMenu(menu, TpmReturnCmd, point.x, point.y, 0, hwnd, IntPtr.Zero);
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private static void UpdateClockText()
    {
        var now = DateTime.Now;
        _dateText = now.ToString("dddd, MMMM d");
        _timeText = now.ToString("HH:mm");
    }

    private static int LowWord(IntPtr value) => (short)((long)value & 0xFFFF);

    private static uint Rgb(byte r, byte g, byte b) => (uint)(r | (g << 8) | (b << 16));

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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

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
}
