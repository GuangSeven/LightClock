# LightClock

A lightweight, draggable desktop clock overlay for Windows with a transparent text-only style, built with **WinUI 3** (Windows App SDK).

![LightClock preview](docs/preview.png)

---

## Features

| Feature | Detail |
|---------|--------|
| 🕐 Live clock | Updates every second |
| 🖱️ Draggable | Left-click and drag anywhere on the widget |
| 🔝 Always on Top | Floats above all other windows (toggleable) |
| 🔎 HiDPI scaling | Per-monitor-v2 DPI awareness for sharp rendering on high-resolution displays |
| 🔤 Font switching | Right-click menu lets you switch between Segoe UI Variable, Consolas, and Georgia |
| 🌌 Transparent overlay style | No panel background, only floating date/time text |
| ⚙️ Settings via right-click | Toggle seconds, 24-hour format, always-on-top; or exit |
| 📐 Wide layout | ~760 × 300 px, top-center by default |

---

## Requirements

| Component | Minimum version |
|-----------|----------------|
| Windows | 10 version 1903 (build 18362) — Windows 11 gets rounded window corners |
| .NET | 8 SDK |
| Visual Studio | Optional (only needed if you prefer IDE debugging) |

---

## Build & Run

### Visual Studio

1. Open `LightClock.sln`
2. Select **Debug | x64** (or Release)
3. Press **F5**

### Command line

```powershell
# Restore dependencies
dotnet restore LightClock/LightClock.csproj

# Run (x64 debug)
dotnet run --project LightClock/LightClock.csproj -r win-x64
```

### Self-contained publish (single folder, no Visual Studio / runtime dependency)

```powershell
dotnet publish LightClock/LightClock.csproj `
    -c Release -r win-x64 --self-contained true `
    -p:WindowsAppSDKSelfContained=true `
    -o publish/
```

Then run:

```powershell
.\publish\LightClock.exe
```

### GitHub Actions (Windows 自动构建)

仓库包含工作流：`.github/workflows/windows-build.yml`  
可在 **Actions → Windows Build** 里手动触发，或在 push / PR 时自动触发。  
构建成功后可下载产物：`LightClock-win-x64`。

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
LightClock/
├── LightClock.sln            Visual Studio solution
└── LightClock/
    ├── LightClock.csproj     Unpackaged WinUI 3 project (.NET 8)
    ├── app.manifest           DPI awareness + OS compatibility
    ├── App.xaml / .cs         Application entry point
    ├── MainWindow.xaml        Clock UI (transparent text-only layout)
    ├── MainWindow.xaml.cs     Clock logic, dragging, settings
    └── Assets/
        └── AppIcon.ico        Application icon
```

---

## Architecture Notes

- **Unpackaged** (`WindowsPackageType=None`) — no MSIX required; runs directly from the output folder.
- **Transparent overlay** — the window content is fully transparent; only date/time text is rendered.
- **Dragging** — implemented via `ReleaseCapture()` + `SendMessage(WM_NCLBUTTONDOWN, HTCAPTION)` so the OS handles the move loop natively; right-click is not affected, so the XAML `ContextFlyout` works normally.
- **Placement** — the widget starts near the top-center of the primary display.
- **Always on top** — `OverlappedPresenter.IsAlwaysOnTop = true` (default); user can toggle via the right-click menu.
