# LightClock

A lightweight transparent desktop clock overlay for Windows 10/11, built with Win32 API + GDI. Single C# file, no UI framework.

![LightClock preview](https://edgeoneimg.cdn1.vip/i/6a561b4fe4125_1784027983.webp)

---

## Features

- Transparent text-only overlay (no background panel)
- Date above large centered time
- Always on top (toggle)
- Drag anywhere to move
- Right-click menu:
  - Always on Top (toggle)
  - Language: English / 中文
  - Font: Segoe UI / Consolas / Cascadia Code / Microsoft YaHei / Custom...
  - Start with Windows (toggle)
  - Accent Color — use Windows system accent color as text color (toggle)
  - Shadow: Off / 25% / 50% / 75% / 100%
  - Exit
- Per-Monitor V2 DPI aware — stays sharp on HiDPI displays
- Survives Win+D (Show Desktop)
- Settings persist across restarts (`%APPDATA%/LightClock/settings.json`)

Settings keys related to shadow:

- `shadowOpacity` (0-100): shadow alpha in percent
- `shadowOffsetPx` (integer > 0): base offset in px at 96 DPI (runtime scales by DPI)
- Legacy `shadowLevel` (0-3) is still accepted when loading old configs and mapped to:
  - level 1/2/3 → opacity 25/50/100 and base offset 2/4/6px

---

## Requirements

- Windows 10/11 (x64 / x86 / ARM64)
- .NET 8 Desktop Runtime — download from <https://dotnet.microsoft.com/download/dotnet/8.0>

---

## Download

### Option A — GitHub Release (recommended)

Go to the [Releases page](https://github.com/GuangSeven/LightClock/releases) and download the zip matching your architecture:

- `LightClock-win-x64.zip` — Intel/AMD 64-bit (most users)
- `LightClock-win-x86.zip` — 32-bit Windows
- `LightClock-win-arm64.zip` — ARM64 Windows (Surface Pro X, Snapdragon)

Unzip and double-click `LightClock.exe`.

### Option B — Build from source

```powershell
git clone https://github.com/GuangSeven/LightClock.git
cd LightClock
dotnet publish LightClock/LightClock.csproj -c Release -r win-x64 --self-contained false -p:Platform=x64 -o publish/
```

---

## Project Structure

```
LightClock/
├── LightClock.sln
├── .github/workflows/windows-build.yml
├── docs/preview.png
└── LightClock/
    ├── LightClock.csproj
    ├── Program.cs          # entire app — Win32 + GDI via P/Invoke
    ├── app.manifest        # PerMonitorV2 DPI, Windows 10/11 compat
    └── Assets/AppIcon.ico
```
