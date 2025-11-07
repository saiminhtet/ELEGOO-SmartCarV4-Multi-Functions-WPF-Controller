# ELEGOO Smart Car V4 - WPF Controller

A modern Windows desktop application for controlling the ELEGOO Smart Car V4 with live video streaming.

## Features

### 🎮 Full Car Control
- **WASD keyboard controls** for driving
- **Camera rotation** with bracket keys
- **Real-time commands** via TCP socket
- **Stable connection** with heartbeat echo

### 📹 Live Video Streaming
- **High-performance WPF rendering** with WriteableBitmap
- **Real-time FPS counter** and performance metrics
- **Smart frame dropping** to prevent lag
- **Instant window loading** (no OpenCV delay)
- **Resizable video window** with status indicators

### 💻 Modern UI
- **Dark theme** professional interface
- **Connection status** with color indicators
- **Video status** display
- **Keyboard shortcuts** guide
- **Status bar** with real-time feedback

## Requirements

- Windows 10/11
- .NET 6.0 or higher
- ELEGOO Smart Car V4 with ESP32-WROVER camera

## Installation

```bash
cd SmartCar\SmartCar
dotnet restore
dotnet build
```

## Running

```bash
dotnet run
```

Or double-click the compiled `.exe` in `bin\Debug\net6.0-windows\`

## Usage

### Main Window
1. Application auto-connects to car at `192.168.4.1:100`
2. Green indicator shows connected status
3. Use keyboard to control the car
4. Press **V** to toggle video stream

### Keyboard Controls

| Key | Action |
|-----|--------|
| **W** | Move Forward (500ms) |
| **S** | Move Backward (500ms) |
| **A** | Turn Left (300ms) |
| **D** | Turn Right (300ms) |
| **[** | Rotate Camera Left |
| **]** | Rotate Camera Right |
| **0** | Switch to Mode 0 (Manual/Normal) |
| **1** | Switch to Mode 1 (Line Detection) |
| **2** | Switch to Mode 2 (Obstacle Detection) |
| **3** | Switch to Mode 3 (Follow Mode) |
| **J** | Toggle Joystick Control Mode |
| **V** | Toggle Live Video Stream |
| **Esc** | Exit Application |

### Joystick Control (Thrustmaster TCA Sidestick)

The application supports flight stick/joystick control for a more immersive driving experience!

**Setup:**
1. Connect your Thrustmaster TCA Sidestick (or any DirectInput joystick/gamepad)
2. Launch the application - joystick will be detected automatically
3. Press **J** to enable joystick control mode

**Joystick Controls:**
- **X-Axis (Left/Right)**: Turn left/right
- **Y-Axis (Forward/Back)**: Drive forward/backward
- **Deadzone**: 7.6% center deadzone prevents drift
- **Variable Speed**: Speed scales with stick deflection (0-100%)
- **Analog Control**: Smooth acceleration and precise control

**Button Mapping:**
| Button | Action |
|--------|--------|
| **Button 1** (Trigger) | Reserved for speed boost |
| **Button 2** | Toggle video stream |
| **Button 3** | Switch to Mode 1 (Line Detection) |
| **Button 4** | Switch to Mode 2 (Obstacle Detection) |
| **Button 5** | Switch to Mode 3 (Follow Mode) |

**Features:**
- ✅ **Analog control** - Smooth, variable speed (not just on/off)
- ✅ **One-handed operation** - Control everything from the stick
- ✅ **Auto-stop** - Robot stops when stick returns to center
- ✅ **Works alongside keyboard** - Can switch between joystick and keyboard anytime

Press **J** again to disable joystick and return to keyboard control.

### Sensor Modes

The car has 4 operating modes that control behavior:

- **Mode 0 (Manual/Normal)**: Default mode - robot only responds to WASD driving commands, no autonomous behavior (DEFAULT)
- **Mode 1 (Line Detection)**: Activates IR line tracking sensors (3 sensors: left, middle, right) for autonomous line following
- **Mode 2 (Obstacle Detection)**: Activates ultrasonic distance sensor for obstacle avoidance
- **Mode 3 (Follow Mode)**: Activates sensors for object following behavior

**Note**: The car starts in Mode 0 (Manual/Normal) where it only responds to your keyboard commands. Press keys 1-3 to enable autonomous sensor modes. Press 0 to return to manual mode.

### Video Stream
- Press **V** to open video window
- FPS counter shows real-time performance
- Resolution displayed in status bar
- **Sensor overlay** shows real-time ultrasonic distance and line tracking data
- Status indicator shows stream health:
  - 🟢 **Green** = Streaming (>5 FPS)
  - 🟠 **Orange** = Slow (1-5 FPS)
  - 🔴 **Red** = Stalled (<1 FPS)

## Architecture

```
┌─────────────────────────────────────────┐
│           MainWindow (WPF)              │
│  • Keyboard input handling              │
│  • Connection status display            │
│  • Video toggle control                 │
└─────────────────────────────────────────┘
         │                    │
         ↓                    ↓
┌──────────────────┐  ┌──────────────────┐
│ ConnectionManager│  │VideoStreamViewer │
│  • TCP @port 100 │  │  • HTTP @port 81 │
│  • Heartbeat echo│  │  • MJPEG parsing │
│  • Command queue │  │  • Frame limiting│
└──────────────────┘  └──────────────────┘
         │                    │
         ↓                    ↓
┌──────────────────┐  ┌──────────────────┐
│  Smart Car ESP32 │  │VideoViewerWindow │
│  192.168.4.1:100 │  │  • WriteableBitmap│
│  • Receives cmds │  │  • FPS counter   │
│  • Sends {ok}    │  │  • Fast rendering│
└──────────────────┘  └──────────────────┘
```

## Project Structure

```
SmartCar/
├── MainWindow.xaml              # Main UI window
├── MainWindow.xaml.cs           # Main window logic
├── VideoViewerWindow.xaml       # Video player UI
├── VideoViewerWindow.xaml.cs    # Video rendering
├── ConnectionManager.cs         # TCP communication
├── VideoStreamViewer.cs         # MJPEG stream handler
├── Command.cs                   # Car command generator
└── SmartCar.csproj             # Project configuration
```

## Technology Stack

- **.NET 6.0 Windows** - WPF framework
- **OpenCvSharp4** - Image processing
- **Newtonsoft.Json** - JSON serialization
- **WriteableBitmap** - High-performance rendering
- **Async/Await** - Non-blocking I/O

## Performance

| Feature | Performance |
|---------|-------------|
| **Connection** | <1 second |
| **Video Load** | Instant |
| **Frame Rate** | 20-30 FPS |
| **Frame Rendering** | ~5-10ms |
| **Command Latency** | <100ms |

## Troubleshooting

### Can't Connect to Car
- Ensure car is powered on
- Connect to car's WiFi network (usually "elegooxxxxx")
- Check IP address is `192.168.4.1`
- Verify ports 100 (control) and 81 (video) are accessible

### Video Not Showing
- Press **V** to toggle video
- Check if video window opened in background
- Verify camera is enabled on car
- Check port 81 is accessible

### Laggy Video
- Frame dropping is automatic (shows "-X" next to FPS)
- Close other applications using network
- Move closer to car's WiFi

### Commands Not Working
- Check connection status (green indicator)
- Watch for heartbeat responses in background
- Ensure window has focus (click on it)

## Credits

- **ELEGOO** for the Smart Car V4 hardware
- **OpenCV** for image processing
- Built with ❤️ using C# and WPF

## Future Development

### Cross-Platform Support

**Current Limitation**: This application is **Windows-only** due to WPF (Windows Presentation Foundation) framework.

#### What Works Cross-Platform:
- ✅ TCP socket communication (`ConnectionManager.cs`)
- ✅ Video streaming (`VideoStreamViewer.cs`)
- ✅ OpenCV image processing
- ✅ Robot command logic (`Command.cs`)

#### What Requires Windows:
- ❌ WPF UI (`MainWindow.xaml`, `VideoViewerWindow.xaml`)
- ❌ WPF controls (`System.Windows.*`)
- ❌ Dispatcher, WriteableBitmap, etc.

#### Options for macOS/Linux Support:

**Option 1: Avalonia UI (Recommended)**
- Cross-platform WPF alternative
- Very similar XAML syntax
- Runs on Windows, macOS, Linux
- Minimal code changes needed
- Website: https://avaloniaui.net/

**Option 2: .NET MAUI**
- Microsoft's official cross-platform framework
- Runs on Windows, macOS, iOS, Android
- Different UI paradigm than WPF
- Good for mobile support

**Option 3: Console Application**
- Remove all UI code
- Control via command-line interface
- Works on any platform with .NET
- Lightweight and simple

**Option 4: Web-Based UI**
- ASP.NET Core backend
- HTML/JavaScript frontend
- Access via browser on any OS
- Remote control capability

**Migration Effort**:
- **Avalonia**: Low (2-3 days) - Similar XAML, mostly copy-paste
- **.NET MAUI**: Medium (1-2 weeks) - Different patterns, redesign needed
- **Console**: Low (1 day) - Remove UI, keep core logic
- **Web UI**: Medium (1-2 weeks) - New frontend from scratch

**Recommendation**: Use **Avalonia UI** for easiest migration path while maintaining the desktop experience.

## License

This is example/educational code for the ELEGOO Smart Car V4.
