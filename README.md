# LightClock

A lightweight desktop clock overlay for Windows, implemented with **minimal dependencies** using **Win32 API + GDI** (no WinUI 3 / Windows App SDK).

![LightClock preview](docs/preview.png)

---

## Features

- Transparent text-only overlay style (no panel background)
- Date above large centered time
- Always on top by default
- Drag anywhere to move (`WM_NCHITTEST -> HTCAPTION`)
- Right-click menu:
  - Always on Top (toggle)
  - Exit
- Updates once per second
- Per-Monitor V2 DPI awareness — clock stays sharp and properly sized when dragged between monitors with different DPI (e.g. 1080p ↔ 4K)

---

## Requirements

### For end users (just want to run it)

- Windows 10/11 (x64 / x86 / ARM64)
- .NET 8 Desktop Runtime — install from <https://dotnet.microsoft.com/download/dotnet/8.0>
  - Pick ".NET Desktop Runtime 8.0.x" matching your Windows architecture
  - This is the only runtime you need; the framework-dependent build is ~216 KB

### For developers (building from source)

- Windows 10/11
- .NET 8 SDK — `winget install Microsoft.DotNet.SDK.8`

---

## Download

### Option A — Pre-built binary from CI (recommended)

1. Go to <https://github.com/GuangSeven/LightClock/actions>
2. Click the most recent successful **Windows Build** run on `main`
3. Scroll down to **Artifacts** and download the archive matching your architecture:
   - `LightClock-win-x64` — Intel/AMD 64-bit (most users)
   - `LightClock-win-x86` — 32-bit Windows
   - `LightClock-win-arm64` — Surface Pro X / Snapdragon laptops
4. Unzip and double-click `LightClock.exe`

### Option B — GitHub Release

When a `v*` tag is pushed, a GitHub Release is published automatically with all three architectures zipped together. See the [Releases page](https://github.com/GuangSeven/LightClock/releases).

### Option C — Build from source

```powershell
git clone https://github.com/GuangSeven/LightClock.git
cd LightClock

# Self-contained build (no .NET runtime required on target machine, ~70 MB)
dotnet publish LightClock/LightClock.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:Platform=x64 `
    -o publish/

# Framework-dependent build (requires .NET 8 Desktop Runtime installed, ~216 KB)
dotnet publish LightClock/LightClock.csproj `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -p:Platform=x64 `
    -o publish/
```

Then double-click `publish/LightClock.exe`.

### Quick local run during development

```powershell
dotnet run --project LightClock/LightClock.csproj -c Debug -p:Platform=x64
```

---

## Architecture

LightClock is intentionally minimal: a single C# file (`Program.cs`) implementing a Win32 message loop with P/Invoke. There is no XAML, no Windows App SDK, no WinRT projection — just `user32.dll` and `gdi32.dll` calls.

### Why not WinUI 3 / WPF / WinForms?

The original prototype was built with WinUI 3, but it pulled in `Microsoft.Windows.SDK.NET.dll` (~25 MB) and `WinRT.Runtime.dll` as runtimepack dependencies. These are not part of the standard .NET Desktop Runtime install, which caused misleading ".NET runtime missing" errors for end users.

By switching to raw Win32 + GDI, the framework-dependent build dropped from ~25 MB to **216 KB**, and users only need the standard .NET 8 Desktop Runtime.

### DPI handling

The app manifest declares `PerMonitorV2` DPI awareness. The window procedure handles `WM_DPICHANGED` by dropping the cached GDI fonts and recreating them at the new DPI on the next paint (using `MulDiv(height, dpi, 96)`). This keeps text crisp at any scale.

---

## GitHub Actions (automatic Windows build)

Workflow: `.github/workflows/windows-build.yml`

- Runs on `windows-latest`
- Build matrix: `win-x64`, `win-x86`, `win-arm64` (parallel)
- Each architecture publishes a self-contained build and uploads as its own artifact:
  - `LightClock-win-x64`
  - `LightClock-win-x86`
  - `LightClock-win-arm64`
- On `v*` tag push: zips all three architectures and publishes a GitHub Release

---

## Project Structure

```text
LightClock/
├── LightClock.sln
├── .github/workflows/windows-build.yml
├── docs/
│   ├── preview.png
│   └── smoke-test/                 # Wine smoke-test screenshots (CI verification)
└── LightClock/
    ├── LightClock.csproj
    ├── Program.cs                   # entire app — Win32 + GDI via P/Invoke
    ├── app.manifest                 # PerMonitorV2 DPI, Windows 10/11 compat
    └── Assets/AppIcon.ico
```

---

## License

See the repository for license details.
