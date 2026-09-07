# GridVids - System Architecture, Project Documentation & Master Plan

## 1. Project Overview & Objectives

**GridVids** is a high-performance, multi-stream video wall desktop application built for Windows using .NET 8 and Avalonia UI. It leverages native `mpv` processes embedded directly into Avalonia window handles (`HWND`) via Win32 interop to deliver fluid, hardware-accelerated playback across dense video matrices without rendering bottlenecks.

### Primary Purpose of `plan.md`
This document serves as the project's primary context anchor and architectural source of truth. It offloads deep technical context regarding components, lifecycles, display modes, IPC mechanisms, and configuration schemas, and must be updated with every feature addition or structural refactoring.

---

## 2. Technology Stack & External Dependencies

- **Runtime & Language**: .NET 8 (C# 12), Windows x64 (`WinExe`).
- **UI Framework**: Avalonia UI (`11.3.11`) using compiled bindings (`x:DataType`), custom styles, and `NativeControlHost`.
- **MVVM Framework**: CommunityToolkit.Mvvm (`8.2.1`) (`ObservableObject`, `ObservableProperty`, `RelayCommand`).
- **Video Engine**:
  - `mpv.exe` (Win64 build located in `Binaries/win-x64/mpv.exe`).
  - `ffprobe.exe` (Win64 build located in `Binaries/win-x64/ffprobe.exe`).
- **IPC Protocol**: Win32 Named Pipes (`\\.\pipe\mpv_...`) communicating JSON-formatted MPV IPC commands.
- **Testing**: xUnit, FluentAssertions, Moq (`GridVids.Tests`).

---

## 3. Directory & File Structure

```text
GridVids/
├── Binaries/
│   └── win-x64/
│       ├── mpv.exe                  # Standalone MPV player binary
│       └── ffprobe.exe              # Media probe utility for duration extraction
├── GridPlayer/
│   ├── App.axaml                    # Application resources, Fluent theme, dark mode brushes
│   ├── App.axaml.cs                 # Desktop initialization and IoC/DI setup
│   ├── Controls/
│   │   └── NativeEmbeddingControl.cs # Avalonia NativeControlHost subclass exposing Win32 HWND
│   ├── Interop/
│   │   └── Win32Interop.cs          # Low-level Win32 P/Invoke APIs and structs (GetCursorPos, ShowWindow, RECT, etc.)
│   ├── Models/
│   │   ├── AppSettings.cs           # Serializable user settings stored in LocalAppData
│   │   └── IGridSlot.cs             # Abstract interface for visual video slots
│   ├── Services/
│   │   ├── PlaybackService.cs       # Orchestrates video selections, pools, playback state, and mode timers
│   │   ├── ScriptOrchestrator.cs    # Spawns mpv processes with HWND embedding, IPC pipe management
│   │   ├── SettingsService.cs       # JSON persistence (%LocalAppData%\GridVids\settings.json)
│   │   └── VideoLibraryService.cs   # Directory scanner, random video provider, ffprobe metadata parser
│   ├── ViewModels/
│   │   ├── MainViewModel.cs         # Core UI state, constructor, settings persistence, commands, transition router
│   │   ├── MainViewModel.DisplayModes.cs # Grid sizing, Auto-Swap alternate timers, and batch recycling
│   │   ├── MainViewModel.ScrollingWall.cs # 60 FPS scrolling wall animation, row spawning, docking alignment
│   │   ├── MainViewModel.Boomerang.cs # Boomerang phase progression, reverse/forward speeds, seamless swap
│   │   ├── MainViewModel.Stackable.cs # 4-quadrant overlay state machine, geometry offsets, buffering
│   │   ├── MainViewModel.CycleModes.cs # Cycle modes sequencer, mode flow duration, mode delegation
│   │   ├── MainViewModel.RandomSwap.cs # Random slot swap timer, background MPV preloader
│   │   └── VideoSlotViewModel.cs    # Represents individual visible video slots (Grid & Stackable)
│   ├── Views/
│   │   ├── MainWindow.axaml         # Main layout: Titlebar, Player Canvas, Control Drawer, Debug HUD
│   │   └── MainWindow.axaml.cs      # Window lifecycle, auto-hide timer, mouse hit-testing via Win32Interop
│   ├── Program.cs                   # Application entry point
│   └── GridVids.csproj              # Project configuration and binary deployment targets
└── GridVids.Tests/
    ├── BoomerangModeTests.cs        # Unit tests for Boomerang state transitions and cycles
    ├── ScrollingWallTests.cs        # Unit tests for canvas math, wrap-around, and speed logic
    └── GridVids.Tests.csproj
```

---

## 4. Architecture & Component Diagram

```mermaid
graph TD
    subgraph UI Layer [Avalonia XAML / MVVM]
        MW[MainWindow.axaml] --> MVM[MainViewModel]
        MVM --> VSVM[VideoSlotViewModel]
        NEC[NativeEmbeddingControl] -->|Creates Win32 HWND| Win32[Windows OS HWND]
    end

    subgraph Core Services
        MVM --> PS[PlaybackService]
        MVM --> VLS[VideoLibraryService]
        MVM --> SS[SettingsService]
        PS --> SO[ScriptOrchestrator]
    end

    subgraph Native MPV Subprocesses
        SO -->|--wid=HWND| MPV1[mpv.exe Process 1]
        SO -->|--wid=HWND| MPV2[mpv.exe Process 2]
        SO -->|Named Pipe IPC| Pipe[\\.\pipe\mpv_slot_x]
        Pipe --> MPV1
    end

    subgraph Persistence & Filesystem
        VLS --> DiskVideos[(Video Directory / Subdirectories)]
        SS --> LocalSettings[(%LocalAppData%/GridVids/settings.json)]
    end
```

---

## 5. Core Subsystems

### 5.1 Native Video Embedding (`NativeEmbeddingControl`)
Avalonia renders using Skia/DirectX, but rendering multiple 4K/1080p video streams in managed code is CPU/GPU prohibitive. GridVids implements [`NativeEmbeddingControl`](file:///c:/Projects/GridVids/GridPlayer/Controls/NativeEmbeddingControl.cs) deriving from `Avalonia.Controls.NativeControlHost`:
- Overrides `CreateNativeControlCore(IPlatformHandle parent)` to instantiate a Win32 child window (`CreateWindowEx`) with class `STATIC`.
- Generates an `HWND` handle.
- Exposes `HandleCreated` and `HandleDestroyed` events.
- [`ScriptOrchestrator`](file:///c:/Projects/GridVids/GridPlayer/Services/ScriptOrchestrator.cs) launches `mpv.exe` targeting this `HWND` via `--wid=<HWND>`.

### 5.2 Process Orchestration & IPC (`ScriptOrchestrator`)
- **Process Arguments**:
  - `--wid=<HWND>`: Embeds video into the Avalonia child window.
  - `--hwdec=auto-safe`: Enables hardware acceleration.
  - `--loop-file=inf`: Continuous loop for individual video items.
  - `--keep-open=yes`: Prevents window destruction on EOF.
  - `--input-ipc-server=\\.\pipe\<unique_pipe_id>`: Enables real-time control (seek, speed, pause, mute, loadfile).
  - `--idle=yes`: Pre-launches background players without freezing UI.
- **Double Buffering / Preload**:
  - To prevent black screen flashes during swaps, replacement MPV instances are initialized in the background ~1.0 second prior to transition.
  - Once buffered, the old process is terminated (`Process.Kill`) and the new process is surfaced.

### 5.3 Video Library Management (`VideoLibraryService`)
- Recursively scans target directory for supported formats (`.mp4`, `.mkv`, `.avi`, `.mov`, `.webm`, `.wmv`, `.flv`, `.m4v`).
- Maintains an in-memory cache of video paths.
- Provides distinct random selections for multi-slot setups.
- Uses `ffprobe.exe` to inspect video duration when precise end-of-file tracking is required.

---

## 6. Active Display Modes

GridVids supports **5 distinct display modes**. *(Note: "Collage" mode has been deprecated and fully removed).*

| Display Mode | Description | Key Timing & Behavior |
| :--- | :--- | :--- |
| **Grid** | Matrix of video slots arranged in configurable Rows (1-5) and Columns (1-8). | Slots calculate 16:9 aspect ratios dynamically based on window width/height. Multiple Delay defaults to 2s. |
| **Auto-Swap** | Seamlessly alternates between two predefined grid dimensions (e.g., Grid 1: 2x2 ↔ Grid 2: 3x3). | Swaps on a fixed timer (`Multiple Delay`, default 2s) without UI tearing. Preloads next grid configuration. |
| **Stackable** | Plays a base grid (2x2 or 2x4) and sequentially stacks 4 overlay quadrant slots on top. | Layers overlay videos, then clears and refreshes base grid slots using buffered transitions. Multiple Delay defaults to 2s. |
| **Scrolling Wall** | Vertical canvas scrolling multiple rows of videos upward or downward at configurable speeds. | Speed range: 20-300 px/s (default 80 px/s). Wraps rows continuously and recycles off-screen MPV instances. |
| **Boomerang** | Forward playback, pauses, reverses playback at negative speed (`-1.0`), then transitions to the next video. | Multiple Delay defaults to 10s. Preloads replacement video ~1.5s before forward-reverse phase cycle concludes. |

---

## 7. Controls, Modifiers & State Flow

### 7.1 Single Vid Mode (`IsSingleVidEnabled`)
- **Strict Activation**: Only activates when explicitly checked by the user.
- **Behavior**:
  - When **Enabled**: Chooses 1 random video and synchronizes playback across all active slots.
  - When **Disabled**: Queries the `VideoLibraryService` for distinct video files so each slot displays a unique clip.

### 7.2 Random Swap (`IsRandomSwapEnabled`)
- Replaces the former Randomize dropdown.
- Configured by `SelectedRandomize` delay (3s, 5s, 10s, 15s, 30s, 60s).
- Every interval:
  1. Selects a random active slot index.
  2. Selects a random video not currently visible.
  3. Preloads the video in the background to ensure it has rendered initial frames (no blank/black screen).
  4. Swaps into the slot and disposes of the old player.

### 7.3 Cycle Modes (`IsCycleModesEnabled`)
- Automatically transitions across display modes:
  `Boomerang` ➔ `Grid` ➔ `Scrolling Wall` ➔ `Stackable` ➔ `Auto-Swap` ➔ (Repeat).
- Duration is controlled per mode via the `Multiple Delay` setting.
- **Scrolling Wall Cycle Docking Mechanism**:
  - When the Cycle timer expires during `Scrolling Wall`, the mode switch is marked pending (`_pendingCycleModeSwitch = true`).
  - The scroll continues running until it has traversed at least 2 full rows (`_scrolledDistanceInCycle >= 2 * cellH`).
  - It then initiates smooth docking alignment (`_isAligningScrollForCycleSwitch = true`): the wall continues moving until the nearest on-screen row aligns *exactly* with integer row boundaries (`CollageY = 0, cellH, 2*cellH, ...`).
  - Off-screen slots are purged and the frozen, docked wall remains visible (`IsScrollEnabled = true`).
  - In the background, `ExecutePlayback` preloads the incoming grid mode using the on-screen docked videos captured via `GetCurrentActiveVideoBatch()`.
  - Once the incoming slots are ready, `IsGridVisible = true` and `IsScrollEnabled = false` occur simultaneously, achieving a 100% seamless transition with zero blank or black frames.
- **Stackable Cycle Transition Mechanism**:
  - When the Cycle timer expires during `Stackable`, the switch is marked pending (`_pendingCycleModeSwitch = true`).
  - Rather than abruptly clearing the quad overlay or reloading the base grid, the 4th quadrant completes its cycle step and triggers `SwitchToRandomCycleMode()` directly.
  - `GetCurrentActiveVideoBatch()` prioritizes the active, visible `StackSlots` quadrant videos first (followed by base `VideoSlots`).
  - During the transition (`ApplyModeTransition`), the `StackSlots` overlay remains visible on screen while the incoming mode (`Grid`, `Auto-Swap`, `Boomerang`, or `Scrolling Wall`) starts decoding the identical batch of videos in the background.
  - After a 600ms grace period allowing native MPV processes to buffer and render their initial frames, `StackSlots` are cleanly cleared without any blank space or flashing.

### 7.4 In-Game Debug HUD (`IsDebugEnabled`)
- Positioned in the exact center of the screen on top of all video HWND windows (`Placement="Center"`, `HorizontalOffset="0"`, `VerticalOffset="0"`).
- Displays real-time operational telemetry:
  - Current Display Mode
  - Active Video Library count
  - Slot count & active MPV PIDs
  - Timers (Swap timer, Cycle timer, Boomerang phase, Scroll velocity)

---

## 8. Persistence & Settings (`AppSettings`)

Settings are serialized as JSON in `%LocalAppData%\GridVids\settings.json` via [`SettingsService`](file:///c:/Projects/GridVids/GridPlayer/Services/SettingsService.cs).

### Configuration Schema:
- `VideoDirectory` (`string`): Path to video folder.
- `IncludeSubdirectories` (`bool`): Recursion toggle for library scanning.
- `SelectedMode` (`string`): Default: `"Grid"`.
- `Rows` (`int`): Grid row count (1-5, default 2).
- `Columns` (`int`): Grid column count (1-8, default 2).
- `SelectedGrid1` / `SelectedGrid2` (`string`): Preset sizes for Auto-Swap (e.g. `"2x2"`, `"3x3"`).
- `MultipleDelay` (`int`): Delay in seconds for multi-video transitions (defaults: 2s for standard, 10s for Boomerang).
- `IsCycleModesEnabled` (`bool`): Cycle modes toggle.
- `IsSingleVidEnabled` (`bool`): Single video replication toggle.
- `IsRandomSwapEnabled` (`bool`): Periodic slot swap toggle.
- `SelectedRandomize` (`int`): Interval in seconds for random slot swapping.
- `ScrollSpeed` (`int`): Pixels per second for Scrolling Wall (20-300, default 80).
- `ScrollDirection` (`string`): `"Up"` or `"Down"`.
- `IsDebugEnabled` (`bool`): Centered debug HUD overlay toggle.

---

## 9. Test Suite & Verification

The test project `GridVids.Tests` validates core logic and math independently of native Win32 window handles:
- **`BoomerangModeTests`**: Validates phase transitions (Forward ➔ Pause ➔ Reverse ➔ Preload ➔ Complete) and timer intervals.
- **`ScrollingWallTests`**: Validates wrap-around canvas math, row heights, velocity calculations, and off-screen slot recycling.

To execute tests:
```powershell
dotnet test GridVids.Tests
```

To build the main application:
```powershell
dotnet build GridPlayer
```

---

## 10. Development Protocol & Maintenance Rule

1. **Always Update `plan.md`**: Whenever a new feature, display mode, setting, or architecture change is made, this file must be updated in the same turn.
2. **Never Break HWND Layering**: Native controls (`NativeControlHost`) render in separate Win32 window hierarchies. Popups and overlays intended to sit above videos must use top-level placement or specialized layering.
3. **Always Verify Single Vid & Random Swap Isolation**: Ensure `IsSingleVidEnabled` never bleeds into normal slot loading logic unless explicitly toggled on. Ensure `Random Swap` always pre-buffers before displaying.
