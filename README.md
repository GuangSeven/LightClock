# WidgetClock

A lightweight, draggable desktop clock widget for Windows with a macOS-style design, built with **WinUI 3** (Windows App SDK).

![WidgetClock preview](docs/preview.png)

---

## Features

| Feature | Detail |
|---------|--------|
| 🕐 Live clock | Updates every second |
| 🖱️ Draggable | Left-click and drag anywhere on the widget |
| 🔝 Always on Top | Floats above all other windows (toggleable) |
| 🔎 HiDPI scaling | Per-monitor-v2 DPI awareness for sharp rendering on high-resolution displays |
| 🔤 Font switching | Right-click menu lets you switch between Segoe UI Variable, Consolas, and Georgia |
| 🌑 macOS dark-glass style | Frosted acrylic backdrop + dark charcoal overlay + rounded corners |
| ⚙️ Settings via right-click | Toggle seconds, 24-hour format, always-on-top; or exit |
| 📐 Compact footprint | ~330 × 148 px widget, sits in the bottom-right corner by default |

---

## Requirements

| Component | Minimum version |
|-----------|----------------|
| Windows | 10 version 1903 (build 18362) — Windows 11 gets rounded window corners |
| .NET | 8 SDK |
| Windows App SDK runtime | 1.5 (installed automatically with the app or via [installer](https://aka.ms/windowsappsdk/1.5/latest/windowsappsdk-x64.exe)) |
| Visual Studio | 2022 17.8+ with the **Windows application development** workload |

---

## Build & Run

### Visual Studio

1. Open `WidgetClock.sln`
2. Select **Debug | x64** (or Release)
3. Press **F5**

### Command line

```powershell
# Restore dependencies
dotnet restore WidgetClock/WidgetClock.csproj

# Run (x64 debug)
dotnet run --project WidgetClock/WidgetClock.csproj -r win-x64
```

### Self-contained publish (single folder, no runtime install needed)

```powershell
dotnet publish WidgetClock/WidgetClock.csproj `
    -c Release -r win-x64 --self-contained true `
    -o publish/
```

---

## Usage

| Action | Result |
|--------|--------|
| Left-click + drag | Move the widget anywhere on the desktop |
| Right-click | Open settings / exit menu |
| **Always on Top** (menu) | Toggle whether the widget floats above all windows |
| **Show Seconds** (menu) | Show/hide the seconds counter |
| **24-Hour Format** (menu) | Switch between 12-hour (AM/PM) and 24-hour display |
| **Font** (menu) | Switch clock font style (Segoe UI Variable / Consolas / Georgia) |
| **Exit** (menu) | Close the application |

---

## Project Structure

```
WidgetClock/
├── WidgetClock.sln            Visual Studio solution
└── WidgetClock/
    ├── WidgetClock.csproj     Unpackaged WinUI 3 project (.NET 8)
    ├── app.manifest           DPI awareness + OS compatibility
    ├── App.xaml / .cs         Application entry point
    ├── MainWindow.xaml        Clock UI (macOS-style dark-glass layout)
    ├── MainWindow.xaml.cs     Clock logic, dragging, settings
    └── Assets/
        └── AppIcon.ico        Application icon
```

---

## Architecture Notes

- **Unpackaged** (`WindowsPackageType=None`) — no MSIX required; runs directly from the output folder.
- **DesktopAcrylicBackdrop** — the system-provided acrylic blur effect provides the "frosted glass" background; a semi-transparent dark `Border` (`#CC1C1C1E`) overlays it for the macOS dark-widget tint.
- **Dragging** — implemented via `ReleaseCapture()` + `SendMessage(WM_NCLBUTTONDOWN, HTCAPTION)` so the OS handles the move loop natively; right-click is not affected, so the XAML `ContextFlyout` works normally.
- **Rounded corners** — on Windows 11, `DwmSetWindowAttribute(DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUND)` enables DWM-level round corners; on Windows 10 the `Border.CornerRadius="18"` in XAML provides the rounded visual inside a square window.
- **Always on top** — `OverlappedPresenter.IsAlwaysOnTop = true` (default); user can toggle via the right-click menu.
