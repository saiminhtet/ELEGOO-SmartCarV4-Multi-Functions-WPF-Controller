using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace SmartCar
{
    public class ConnectionManager : IDisposable
    {
        private readonly string _ipAddress;
        private readonly int _port;
        private TcpClient _client;
        private NetworkStream _stream;
        private readonly ConcurrentQueue<string> _sendQueue;
        private readonly SemaphoreSlim _sendSemaphore;
        private CancellationTokenSource _cancellationSource;
        private Task _receiveTask;
        private Task _sendTask;
        private Task _heartbeatTask;
        private Task _sensorTask;
        private bool _isConnected;
        private readonly object _lockObject = new object();
        private int _lastLineTrackingSensorRequested = -1; // Track which sensor we last requested (0=left, 1=middle, 2=right)
        private int _currentMode = 0; // Current car mode: 0=Manual/Normal, 1=Line Detection, 2=Obstacle Detection, 3=Follow
        private DateTime _lastHeartbeatReceived;
        private DateTime _lastKeepAliveSent;
        private DateTime _lastCommandSent;

        public event EventHandler<string> MessageReceived;
        public event EventHandler<bool> ConnectionStatusChanged;
        public event EventHandler<SensorData> SensorDataUpdated;

        public bool IsConnected => _isConnected;
        public SensorData CurrentSensorData { get; private set; }

        public ConnectionManager(string ipAddress, int port)
        {
            _ipAddress = ipAddress;
            _port = port;
            _sendQueue = new ConcurrentQueue<string>();
            _sendSemaphore = new SemaphoreSlim(0);
            _lastHeartbeatReceived = DateTime.Now;
            _lastKeepAliveSent = DateTime.MinValue;
            _lastCommandSent = DateTime.Now;
            CurrentSensorData = new SensorData();
        }

        public async Task<bool> ConnectAsync()
        {
            try
            {
                Console.WriteLine($"Connecting to {_ipAddress}:{_port}...");

                _client = new TcpClient();

                // Increase timeouts for WiFi stability
                _client.ReceiveTimeout = 15000; // 15 seconds
                _client.SendTimeout = 15000;

                // Enable TCP keepalive to detect dead connections
                _client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

                // Increase buffer sizes for better throughput
                _client.ReceiveBufferSize = 8192;
                _client.SendBufferSize = 8192;

                // Disable Nagle's algorithm for lower latency
                _client.NoDelay = true;

                // Connect with timeout
                var connectTask = _client.ConnectAsync(_ipAddress, _port);
                if (await Task.WhenAny(connectTask, Task.Delay(10000)) == connectTask)
                {
                    _stream = _client.GetStream();
                    _isConnected = true;
                    _cancellationSource = new CancellationTokenSource();
                    _lastHeartbeatReceived = DateTime.Now;

                    // Start background tasks
                    _receiveTask = ReceiveLoopAsync(_cancellationSource.Token);
                    _sendTask = SendLoopAsync(_cancellationSource.Token);
                    _heartbeatTask = HeartbeatMonitorAsync(_cancellationSource.Token);
                    _sensorTask = SensorPollingAsync(_cancellationSource.Token);

                    Console.WriteLine("Connected successfully!");
                    ConnectionStatusChanged?.Invoke(this, true);
                    return true;
                }
                else
                {
                    Console.WriteLine("Connection timeout.");
                    Cleanup();
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Connection error: {ex.Message}");
                Cleanup();
                return false;
            }
        }

        public void SendCommand(string command)
        {
            if (!_isConnected)
            {
                Console.WriteLine("ERROR: Not connected. Command not sent.");
                return;
            }

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Queuing command: {command}");
            _lastCommandSent = DateTime.Now;
            _sendQueue.Enqueue(command);
            _sendSemaphore.Release();
        }

        public void SwitchMode(int mode)
        {
            if (mode < 0 || mode > 3)
            {
                Console.WriteLine($"ERROR: Invalid mode {mode}. Must be 0-3.");
                return;
            }

            _currentMode = mode;

            if (mode == 0)
            {
                // Mode 0 = Manual/Normal mode - clear any autonomous behavior
                Console.WriteLine($"[Mode] Switching to Mode 0 (Manual/Normal) - Manual control only");
                // Send joystick clear to disable autonomous modes
                string clearCommand = SmartCarCommands.JoystickClear();
                SendCommand(clearCommand);
            }
            else
            {
                Console.WriteLine($"[Mode] Switching to Mode {mode}");
                string modeCommand = SmartCarCommands.SwitchMode(mode);
                SendCommand(modeCommand);
            }
        }

        private async Task SendLoopAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested && _isConnected)
                {
                    await _sendSemaphore.WaitAsync(token);

                    if (_sendQueue.TryDequeue(out string command))
                    {
                        try
                        {
                            byte[] data = Encoding.ASCII.GetBytes(command);
                            await _stream.WriteAsync(data, 0, data.Length, token);
                            await _stream.FlushAsync(token);

                            // Only show actual commands, not heartbeat echoes (too verbose)
                            if (command != "{Heartbeat}")
                            {
                                Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] → Command sent: {command}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"ERROR: Send failed: {ex.Message}");
                            await HandleDisconnectionAsync();
                            break;
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken token)
        {
            byte[] buffer = new byte[4096];
            StringBuilder messageBuffer = new StringBuilder();

            try
            {
                while (!token.IsCancellationRequested && _isConnected)
                {
                    int bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length, token);

                    if (bytesRead == 0)
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ERROR: Connection closed by remote host (0 bytes read)");
                        await HandleDisconnectionAsync();
                        break;
                    }

                    string data = Encoding.ASCII.GetString(buffer, 0, bytesRead);

                    // Only log non-heartbeat messages to reduce spam
                    if (!data.Contains("{Heartbeat}"))
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ← Received: {data}");
                    }

                    messageBuffer.Append(data);

                    // Process complete messages
                    ProcessMessages(messageBuffer);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.WriteLine($"Receive error: {ex.Message}");
                await HandleDisconnectionAsync();
            }
        }

        private void ProcessMessages(StringBuilder buffer)
        {
            string data = buffer.ToString();
            int processedLength = 0;

            // Look for {Heartbeat} messages
            while (data.Contains("{Heartbeat}"))
            {
                int heartbeatIndex = data.IndexOf("{Heartbeat}");
                if (heartbeatIndex >= 0)
                {
                    _lastHeartbeatReceived = DateTime.Now;
                    // Heartbeat logging is too verbose, only log issues

                    // Send keep-alive response to prevent disconnection
                    SendKeepAliveResponse();

                    // Remove processed heartbeat
                    processedLength = heartbeatIndex + "{Heartbeat}".Length;
                    data = data.Substring(processedLength);
                    buffer.Remove(0, processedLength);
                    processedLength = 0;
                }
            }

            // Look for error messages
            while (data.Contains("error:"))
            {
                int errorIndex = data.IndexOf("error:");
                int errorEnd = data.IndexOf('\n', errorIndex);
                if (errorEnd < 0) errorEnd = data.IndexOf('{', errorIndex);
                if (errorEnd < 0) errorEnd = data.Length;

                string errorMsg = data.Substring(errorIndex, errorEnd - errorIndex).Trim();
                Console.WriteLine($"Car error: {errorMsg}");

                // Remove processed error
                data = data.Substring(errorEnd);
                buffer.Remove(0, errorEnd);
            }

            // Look for acknowledgment messages: {ok} or {N_ok}
            int ackStart = 0;
            while (ackStart < data.Length)
            {
                int openBrace = data.IndexOf('{', ackStart);
                if (openBrace < 0) break;

                int closeBrace = data.IndexOf('}', openBrace);
                if (closeBrace < 0) break;

                string possibleAck = data.Substring(openBrace, closeBrace - openBrace + 1);

                // Check if it's an acknowledgment: {ok} or {digit_ok}
                if (possibleAck == "{ok}" || System.Text.RegularExpressions.Regex.IsMatch(possibleAck, @"^\{\d+_ok\}$"))
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ✓ Acknowledgment: {possibleAck}");

                    // Remove processed acknowledgment
                    processedLength = closeBrace + 1;
                    data = data.Substring(processedLength);
                    buffer.Remove(0, processedLength);
                    ackStart = 0;
                    processedLength = 0;
                }
                // Check for sensor response: {H_value} like {457_44} or {458_930}
                else if (System.Text.RegularExpressions.Regex.IsMatch(possibleAck, @"^\{\d+_\d+\}$"))
                {
                    var parts = possibleAck.Trim('{', '}').Split('_');
                    if (parts.Length == 2 && int.TryParse(parts[1], out int value))
                    {
                        Console.WriteLine($"[Sensors] Raw response: {possibleAck} -> value={value}, lastLineRequest={_lastLineTrackingSensorRequested}");

                        // Line tracking sensors return RAW analog values (0-1023)
                        // Low value (~0-300) = line detected (dark/black)
                        // High value (~700-1023) = no line (bright/white)
                        const int LINE_DETECTION_THRESHOLD = 500;

                        // Determine if this is ultrasonic or line tracking based on context
                        if (_lastLineTrackingSensorRequested >= 0) // We recently requested a line sensor
                        {
                            // This is a line tracking response
                            bool lineDetected = (value < LINE_DETECTION_THRESHOLD);

                            if (_lastLineTrackingSensorRequested == 0) // Left sensor
                            {
                                CurrentSensorData.LeftLineDetected = lineDetected;
                                Console.WriteLine($"[Sensors] ✓ Line LEFT: {value} -> {(lineDetected ? "DETECTED" : "NOT DETECTED")}");
                            }
                            else if (_lastLineTrackingSensorRequested == 1) // Middle sensor
                            {
                                CurrentSensorData.MiddleLineDetected = lineDetected;
                                Console.WriteLine($"[Sensors] ✓ Line MIDDLE: {value} -> {(lineDetected ? "DETECTED" : "NOT DETECTED")}");
                            }
                            else if (_lastLineTrackingSensorRequested == 2) // Right sensor
                            {
                                CurrentSensorData.RightLineDetected = lineDetected;
                                Console.WriteLine($"[Sensors] ✓ Line RIGHT: {value} -> {(lineDetected ? "DETECTED" : "NOT DETECTED")}");
                            }

                            CurrentSensorData.LineTrackingTimestamp = DateTime.Now;
                            _lastLineTrackingSensorRequested = -1; // Reset after processing
                            SensorDataUpdated?.Invoke(this, CurrentSensorData);
                        }
                        else // Ultrasonic sensor response
                        {
                            CurrentSensorData.UltrasonicDistance = value;
                            CurrentSensorData.UltrasonicTimestamp = DateTime.Now;
                            Console.WriteLine($"[Sensors] ✓ Ultrasonic: {value} cm");
                            SensorDataUpdated?.Invoke(this, CurrentSensorData);
                        }
                    }

                    // Remove processed sensor data
                    processedLength = closeBrace + 1;
                    data = data.Substring(processedLength);
                    buffer.Remove(0, processedLength);
                    ackStart = 0;
                    processedLength = 0;
                }
                else
                {
                    ackStart = closeBrace + 1;
                }
            }

            // Look for JSON messages (start with { and end with })
            int startIndex = 0;
            while (startIndex < data.Length)
            {
                int jsonStart = data.IndexOf('{', startIndex);
                if (jsonStart < 0) break;

                int jsonEnd = data.IndexOf('}', jsonStart);
                if (jsonEnd < 0) break; // Incomplete message, wait for more data

                string jsonMessage = data.Substring(jsonStart, jsonEnd - jsonStart + 1);

                // Skip if it's a heartbeat (already processed)
                if (jsonMessage != "{Heartbeat}")
                {
                    try
                    {
                        // Validate and parse JSON
                        var json = JObject.Parse(jsonMessage);

                        // Check if it's sensor data
                        if (json["N"] != null)
                        {
                            int commandType = json["N"].Value<int>();
                            Console.WriteLine($"[DEBUG] Received command type N={commandType}, full JSON: {jsonMessage}");

                            // Ultrasonic sensor response (N=21)
                            if (commandType == 21)
                            {
                                if (json["D"] != null)
                                {
                                    CurrentSensorData.UltrasonicDistance = json["D"].Value<int>();
                                    CurrentSensorData.UltrasonicTimestamp = DateTime.Now;
                                    Console.WriteLine($"[Sensors] ✓ Ultrasonic: {CurrentSensorData.UltrasonicDistance} cm");
                                    SensorDataUpdated?.Invoke(this, CurrentSensorData);
                                }
                                else
                                {
                                    Console.WriteLine($"[Sensors] ERROR: Ultrasonic response missing 'D' field: {jsonMessage}");
                                }
                            }
                            // Line tracking sensor response (N=22)
                            else if (commandType == 22)
                            {
                                Console.WriteLine($"[DEBUG] Line tracking response - D1={json["D1"]}, D2={json["D2"]}, D3={json["D3"]}");

                                if (json["D1"] != null)
                                {
                                    int leftSensor = json["D1"]?.Value<int>() ?? 0;
                                    int middleSensor = json["D2"]?.Value<int>() ?? 0;
                                    int rightSensor = json["D3"]?.Value<int>() ?? 0;

                                    // 0 = line detected (black), 1 = no line (white)
                                    CurrentSensorData.LeftLineDetected = (leftSensor == 0);
                                    CurrentSensorData.MiddleLineDetected = (middleSensor == 0);
                                    CurrentSensorData.RightLineDetected = (rightSensor == 0);
                                    CurrentSensorData.LineTrackingTimestamp = DateTime.Now;

                                    Console.WriteLine($"[Sensors] ✓ Line: L={leftSensor} M={middleSensor} R={rightSensor}");
                                    SensorDataUpdated?.Invoke(this, CurrentSensorData);
                                }
                                else
                                {
                                    Console.WriteLine($"[Sensors] ERROR: Line tracking response missing 'D1' field: {jsonMessage}");
                                }
                            }
                            else
                            {
                                // Other command response
                                Console.WriteLine($"[DEBUG] Other command: {jsonMessage}");
                                MessageReceived?.Invoke(this, jsonMessage);
                            }
                        }
                        else
                        {
                            Console.WriteLine($"[DEBUG] Message without N field: {jsonMessage}");
                            MessageReceived?.Invoke(this, jsonMessage);
                        }
                    }
                    catch
                    {
                        // Invalid JSON, skip
                    }
                }

                processedLength = jsonEnd + 1;
                startIndex = jsonEnd + 1;
            }

            // Remove processed data from buffer
            if (processedLength > 0)
            {
                buffer.Remove(0, processedLength);
            }
        }

        private void SendKeepAliveResponse()
        {
            // CRITICAL: Car requires ECHO of {Heartbeat} back
            // Based on official Python library: client_socket.send(data.encode())

            // Throttle to once per heartbeat to avoid flooding
            var timeSinceLastKeepAlive = DateTime.Now - _lastKeepAliveSent;
            if (timeSinceLastKeepAlive.TotalMilliseconds < 500)
            {
                return; // Already sent for this heartbeat
            }

            _lastKeepAliveSent = DateTime.Now;

            // Echo heartbeat back to car (exact string match required)
            string heartbeatEcho = "{Heartbeat}";
            _sendQueue.Enqueue(heartbeatEcho);
            _sendSemaphore.Release();
        }

        private async Task HeartbeatMonitorAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested && _isConnected)
                {
                    await Task.Delay(10000, token); // Check every 10 seconds

                    var timeSinceLastHeartbeat = DateTime.Now - _lastHeartbeatReceived;
                    if (timeSinceLastHeartbeat.TotalSeconds > 30) // Increased from 15 to 30 seconds
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] WARNING: Heartbeat timeout - no heartbeat for {timeSinceLastHeartbeat.TotalSeconds:F1}s");
                        await HandleDisconnectionAsync();
                        break;
                    }
                    else if (timeSinceLastHeartbeat.TotalSeconds > 10)
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] WARNING: Slow heartbeat - last received {timeSinceLastHeartbeat.TotalSeconds:F1}s ago");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation
            }
        }

        private async Task SensorPollingAsync(CancellationToken token)
        {
            try
            {
                // Wait a bit for initial connection to stabilize
                await Task.Delay(5000, token);

                Console.WriteLine("[Sensors] Sensor polling service started");
                Console.WriteLine("[Sensors] Starting in Mode 0 (Manual/Normal) - Press 1/2/3 to enable sensor modes");

                int pollCount = 0;
                while (!token.IsCancellationRequested && _isConnected)
                {
                    await Task.Delay(5000, token); // Wait 5 seconds between maintenance cycles

                    if (_currentMode == 0)
                    {
                        // Manual mode - no maintenance needed
                        continue;
                    }

                    pollCount++;
                    Console.WriteLine($"[Mode] Maintenance #{pollCount} - Mode {_currentMode} active");

                    // In autonomous modes (1/2/3), only re-send mode command to keep it active
                    // DO NOT manually poll sensors - this interferes with autonomous operation!
                    Console.WriteLine($"[Mode] 🔄 Re-asserting Mode {_currentMode} to maintain autonomous operation");
                    string modeMaintainCmd = SmartCarCommands.SwitchMode(_currentMode);
                    _sendQueue.Enqueue(modeMaintainCmd);
                    _sendSemaphore.Release();
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("[Sensors] Sensor polling stopped");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Sensors] Error in sensor polling: {ex.Message}");
            }
        }

        private async Task HandleDisconnectionAsync()
        {
            lock (_lockObject)
            {
                if (!_isConnected)
                {
                    Console.WriteLine("Already disconnected, ignoring");
                    return;
                }
                _isConnected = false;
            }

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] *** DISCONNECTED from server ***");
            ConnectionStatusChanged?.Invoke(this, false);
            Cleanup();

            // Attempt reconnection
            await Task.Delay(2000);
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Attempting to reconnect...");
            await ConnectAsync();
        }

        private void Cleanup()
        {
            try
            {
                _cancellationSource?.Cancel();
                _stream?.Close();
                _client?.Close();
            }
            catch { }
        }

        public void Dispose()
        {
            _isConnected = false;
            _cancellationSource?.Cancel();

            try
            {
                _receiveTask?.Wait(1000);
                _sendTask?.Wait(1000);
                _heartbeatTask?.Wait(1000);
                _sensorTask?.Wait(1000);
            }
            catch { }

            _stream?.Dispose();
            _client?.Dispose();
            _cancellationSource?.Dispose();
            _sendSemaphore?.Dispose();
        }
    }
}
