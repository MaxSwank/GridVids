<p align="center">
  <img src="docs/logo.png" alt="GridVids Logo" width="200"/>
</p>

# 🎬 GridVids

[![Avalonia UI](https://img.shields.io/badge/UI-Avalonia-purple.svg)](https://avaloniaui.net/)
[![.NET](https://img.shields.io/badge/.NET-8.0-blue.svg)](https://dotnet.microsoft.com/)
[![mpv](https://img.shields.io/badge/Engine-mpv-red.svg)](https://mpv.io/)

**GridVids** is a high-performance, immersive video grid application built with C# and Avalonia UI. Leveraging the powerful `mpv` media player, GridVids allows users to experience multiple video streams simultaneously in high definitions, with dynamic layout transitions and advanced playback modes.

---

## ✨ Features

- **🚀 Instant Load**: Start playback immediately upon launch, ensuring a seamless experience.
- **🔳 Configurable Grids**: Seamlessly switch between different grid configurations, from a simple 2x2 to a dense 4x8 layout.
- **🔄 Auto-Swap**: Automatically cycle between two grid configurations at a set interval, perfect for monitoring multiple sources or creating a dynamic video wall.
- **🎨 Dynamic Collage**: An interactive, overlapping video display featuring:
    - **Fade-ins/Fade-outs**: Smooth transitions for new video slots.
    - **Occlusion Culling**: Intelligently kills hidden video processes to optimize system resources.
    - **Quad-Balanced Spawning**: Ensures even distribution of videos across all quadrants.
- **🎯 Intelligent Library**: Fast, cached video discovery in targeted folders with random selection logic.

---

## 🛠️ Tech Stack

- **Framework**: [.NET 8](https://dotnet.microsoft.com/download)
- **UI**: [Avalonia UI](https://avaloniaui.net/) (Cross-platform UI framework)
- **MVVM**: [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet)
- **Engine**: [mpv](https://mpv.io/) (via custom orchestrator for multiple instances)
- **Automation**: Lua scripts for `mpv` customization (like `slomo.lua`).

---

## 🏗️ Architecture

GridVids is built on a robust, asynchronous architecture designed to handle dozens of concurrent video processes efficiently.

- **`ScriptOrchestrator`**: Manages `mpv` binary paths, Lua script locations, and launches individual `mpv` instances with tailored arguments (wid embedding, hwdec, custom scripts).
- **`PlaybackService`**: Coordinates the launch and cleanup of video slots, ensuring that the UI remains responsive even during heavy transitions.
- **`MainViewModel`**: The core logic engine that manages grid states, collage timers, and user settings using the MVVM pattern.
- **`VideoLibraryService`**: Handles high-performance directory scanning and caching of media files.

---

## ⚙️ Prerequisites

- **.NET 8.0 SDK**
- **mpv player**: Make sure `mpv` is either in your PATH or placed in the `Binaries` folder within the project. The application will search for:
    - `mpv` on system PATH
    - `MPV_PATH` environment variable
    - `Binaries/win-x64/mpv.exe` (on Windows)

---

## 🚀 Getting Started

1. **Clone the repository**:
   ```pwsh
   git clone https://github.com/MaxSwank/GridVids.git
   cd GridVids
   ```

2. **Build & Run**:
   ```pwsh
   cd GridVids
   dotnet run
   ```

3. **Select Media**: Once launched, select a folder containing `.mp4` files to start the experience.

---

## � Downloads

Check out the [Releases](https://github.com/MaxSwank/GridVids/releases) page for the latest Windows installer.

### 🛠️ Building the Installer
If you are building from source and want to create the Windows installer:
1. Install [Inno Setup 6+](https://jrsoftware.org/isdl.php).
2. Run `dotnet publish -c Release -r win-x64 --self-contained true` in the root folder.
3. Open `installer.iss` in Inno Setup and click **Compile**.

---

## �📜 License

Distributed under the MIT License. See `LICENSE` for more information.
