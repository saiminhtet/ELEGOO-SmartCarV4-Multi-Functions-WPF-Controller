# ELEGOO Smart Car V4 - Architecture Documentation

## Overview

This project provides a robust TCP socket-based controller for the ELEGOO Smart Car V4 using C# and .NET 6.0.

## Architecture Components

### 1. ConnectionManager.cs

The core component that handles all TCP communication with the smart car.

### 2. VideoStreamViewer.cs

Handles live video streaming from the ESP32-WROVER camera module using OpenCV.

**Key Features:**
- **MJPEG Stream Parsing**: Decodes MJPEG stream from ESP32 camera (http://192.168.4.1/stream)
- **OpenCV Integration**: Uses OpenCvSharp4 for frame decoding and display
- **Async Streaming**: Non-blocking video capture and processing
- **Event-Driven**: Raises FrameReceived event for each decoded frame
- **Resource Management**: Proper cleanup and disposal of frames

**Architecture:**
```
┌─────────────────────────────────────────┐
│       VideoStreamViewer                  │
├─────────────────────────────────────────┤
│  - HttpClient (stream connection)       │
│  - MJPEG Parser (frame extraction)      │
│  - Frame Buffer (0xFF 0xD8 → 0xFF 0xD9) │
└─────────────────────────────────────────┘
         │
         ├─── StreamVideoAsync()
         │    - Connects to /stream endpoint
         │    - Parses MJPEG boundaries
         │    - Extracts JPEG frames
         │
         ├─── ProcessFrame()
         │    - Decodes JPEG to Mat
         │    - Raises FrameReceived event
         │
         └─── VideoWindow
              - Displays frames in OpenCV window
              - Updates at video frame rate
```

### 1. ConnectionManager.cs (continued)

The core component that handles all TCP communication with the smart car.

**Key Features:**
- **Async/Await Pattern**: Non-blocking I/O for smooth operation
- **Connection Management**: Automatic connection, reconnection, and cleanup
- **Message Framing**: Properly handles TCP stream data with message boundaries
- **Heartbeat Monitoring**: Detects connection loss via heartbeat timeout
- **Command Queue**: Thread-safe command queuing with concurrent processing
- **Event-Driven**: Exposes events for message reception and connection status

**Architecture:**
```
┌─────────────────────────────────────────┐
│       ConnectionManager                  │
├─────────────────────────────────────────┤
│  - TcpClient (proper client connection) │
│  - NetworkStream (async I/O)            │
│  - ConcurrentQueue (command queue)      │
│  - CancellationTokens (graceful stop)   │
└─────────────────────────────────────────┘
         │
         ├─── ReceiveLoopAsync()
         │    - Reads data asynchronously
         │    - Parses {Heartbeat} messages
         │    - Extracts JSON commands
         │    - Raises MessageReceived event
         │
         ├─── SendLoopAsync()
         │    - Dequeues commands
         │    - Sends via TCP stream
         │    - Thread-safe operation
         │
         └─── HeartbeatMonitorAsync()
              - Monitors last heartbeat time
              - Triggers reconnection on timeout
```

### 2. Program.cs

The main application entry point with a clean async structure.

**Features:**
- Clean async Main() method
- Event-driven message handling
- Non-blocking keyboard input
- Graceful shutdown

### 3. Command.cs

Generates JSON-formatted commands for the smart car.

**Available Commands:**
- `CarControlTime(direction, speed, time)` - Timed movement
- `CarControl(direction, speed)` - Continuous movement
- `CameraRotation(direction)` - Camera control
- `MotorControl(motor, speed, direction)` - Individual motor control
- And more sensor/control commands

## Key Improvements Over Previous Implementation

### Problem 1: Server/Client Confusion
**Before:** Used `socket.Accept()` (server-side listening)
```csharp
Socket handler = clientSocket.Accept(); // WRONG!
```

**After:** Uses `TcpClient.ConnectAsync()` (proper client connection)
```csharp
await _client.ConnectAsync(_ipAddress, _port);
```

### Problem 2: Blocking Calls
**Before:** Synchronous blocking `socket.Receive()`
```csharp
int bytesRead = socket.Receive(buffer); // Blocks entire thread
```

**After:** Async/await pattern
```csharp
int bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length, token);
```

### Problem 3: Poor Message Framing
**Before:** Expected exact string match
```csharp
if (data == "{Heartbeat}{Heartbeat}{Heartbeat}{Heartbeat}")
```

**After:** Proper parsing of TCP stream
```csharp
// Handles any number of concatenated messages
while (data.Contains("{Heartbeat}")) { ... }
// Extracts individual JSON objects
```

### Problem 4: No Error Recovery
**Before:** Crashed on errors
```csharp
Environment.Exit(0); // Kills entire application
```

**After:** Graceful reconnection
```csharp
private async Task HandleDisconnectionAsync() {
    await Task.Delay(2000);
    await ConnectAsync(); // Auto-reconnect
}
```

### Problem 5: Threading Issues
**Before:** Commented-out thread code, blocking loops
```csharp
//Thread receiveThread = new Thread(ReceiveThread);
while (true) { socket.Receive(...); } // Blocking
```

**After:** Proper async tasks
```csharp
_receiveTask = ReceiveLoopAsync(_cancellationSource.Token);
_sendTask = SendLoopAsync(_cancellationSource.Token);
_heartbeatTask = HeartbeatMonitorAsync(_cancellationSource.Token);
```

## Communication Protocol

### Connection Flow
```
[Client]                           [Smart Car]
   |                                    |
   |--- Connect to 192.168.4.1:100 --->|
   |<--------- {Heartbeat} ------------|
   |--- Echo: {Heartbeat} ------------->| (CRITICAL: exact echo required)
   |<--------- {Heartbeat} ------------|
   |--- Echo: {Heartbeat} ------------->|
   |                                    |
   |--- Send Command JSON ------------->|
   |<----- {N_ok} Acknowledgment -------|
   |                                    |
   |<--------- {Heartbeat} ------------|
   |--- Echo: {Heartbeat} ------------->|
   |                (continuous)        |
```

**CRITICAL:** The car REQUIRES an exact echo of `{Heartbeat}` back for every heartbeat
received. If the client doesn't echo the heartbeat, the car will close the connection
after ~4 heartbeats. The ConnectionManager automatically echoes `{Heartbeat}` back to
the car in response to each heartbeat received.

This was discovered by analyzing the official ELEGOO Python library:
```python
elif data == "{Heartbeat}":
    client_socket.send(data.encode())  # Echo it back!
```

### Message Format
Commands are JSON objects:
```json
{
  "H": "sequence_number",
  "N": command_type,
  "D1": parameter1,
  "D2": parameter2,
  "T": duration_ms
}
```

Example - Forward for 1 second:
```json
{"H":"1","N":2,"D1":3,"D2":100,"T":1000}
```

## Usage

### Building
```bash
cd D:\Research&Development\ELEGOOSmartCarV4\SmartCar\SmartCar\SmartCar
dotnet build
```

### Running
```bash
dotnet run
```

### Controls
- **W** - Move forward (1 second)
- **S** - Move backward (1 second)
- **A** - Turn left (0.5 seconds)
- **D** - Turn right (0.5 seconds)
- **[** - Rotate camera left
- **]** - Rotate camera right
- **V** - Toggle live video stream
- **Q** - Quit application

### Video Streaming
The application supports live video streaming from the ESP32-WROVER camera:
- Stream URL: `http://192.168.4.1/stream` (MJPEG format)
- Press **V** to start/stop the video feed
- Video displays in a separate OpenCV window
- Runs asynchronously without blocking car controls

## Configuration

To change the car's IP or port, edit `Program.cs`:
```csharp
private static string Robot_IP = "192.168.4.1";
private static int Robot_Port = 100;
```

## Troubleshooting

### Connection Issues
1. Ensure the car's WiFi module is active
2. Connect your computer to the car's WiFi network
3. Verify IP address (usually 192.168.4.1)
4. Check firewall settings

### Car Not Responding
- Watch for "Heartbeat received" messages
- If heartbeats stop, connection is lost
- ConnectionManager will auto-reconnect

### Commands Not Working
- Verify the command format in Command.cs
- Check console for "Sent: {command}" messages
- Look for error messages in console output

## Technical Notes

### Thread Safety
- `ConcurrentQueue<string>` for command queuing
- `SemaphoreSlim` for send coordination
- `lock` statements for connection state

### Memory Management
- Proper `Dispose()` pattern implementation
- `CancellationToken` for graceful task shutdown
- Stream and socket cleanup on disconnect

### Performance
- 50ms delay in main loop (reduces CPU usage)
- 4KB receive buffer (handles large messages)
- Async I/O (non-blocking operations)

## Future Enhancements

Possible improvements:
1. Add command response validation
2. Implement sequence number tracking
3. Add sensor data parsing
4. Create GUI interface
5. Add logging framework
6. Implement command macros
7. Add telemetry recording

## File Structure
```
SmartCar/
├── Program.cs              # Main application entry with video integration
├── ConnectionManager.cs    # TCP connection handler
├── VideoStreamViewer.cs    # Video streaming with OpenCV (NEW)
├── Command.cs              # Command generation
├── TCP.cs                  # Legacy code (not used)
└── SmartCar.csproj        # Project configuration
```

## Dependencies

The project uses the following NuGet packages:
- **Newtonsoft.Json** (13.0.3) - JSON serialization for commands
- **OpenCvSharp4** (4.9.0.20240103) - OpenCV wrapper for C#
- **OpenCvSharp4.runtime.win** (4.9.0.20240103) - Native OpenCV binaries for Windows

## References
- [ELEGOO Smart Car V4 Documentation](https://www.elegoo.com)
- [.NET TcpClient Documentation](https://docs.microsoft.com/dotnet/api/system.net.sockets.tcpclient)
- [C# Async/Await Pattern](https://docs.microsoft.com/dotnet/csharp/programming-guide/concepts/async/)
