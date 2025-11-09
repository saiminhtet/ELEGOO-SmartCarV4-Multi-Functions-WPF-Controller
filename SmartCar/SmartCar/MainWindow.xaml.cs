using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using OpenCvSharp;
using NLog;

namespace SmartCarExample
{
    public partial class MainWindow : System.Windows.Window
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly SmartCar.AppConfiguration config;
        private SmartCar.ConnectionManager? connectionManager;
        private SmartCar.VideoStreamViewer? videoViewer;
        private SmartCar.VideoViewerWindow? videoWindow;
        private bool videoEnabled = false;
        private System.Collections.Generic.HashSet<Key> pressedKeys = new System.Collections.Generic.HashSet<Key>();
        private System.Windows.Threading.DispatcherTimer? statusUpdateTimer;
        private int lastDirection = 3; // Track last movement direction for stopping
        private SmartCar.JoystickController? joystickController;
        private bool joystickEnabled = false;
        private int lastJoystickX = 0;
        private int lastJoystickY = 0;
        private DateTime lastJoystickCommandTime = DateTime.MinValue;
        private SmartCar.RacingWheelController? racingWheelController;
        private bool racingWheelEnabled = false;
        private int lastWheelSteering = 0;
        private int lastWheelThrottle = 0;
        private DateTime lastWheelCommandTime = DateTime.MinValue;

        public MainWindow()
        {
            InitializeComponent();

            // Load configuration
            config = SmartCar.AppConfiguration.Load();

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Logger.Info("=== MainWindow Loading ===");

            // Focus window for keyboard input
            this.Focus();
            this.Activate();
            Logger.Debug($"Window focused: {this.IsFocused}, Activated: {this.IsActive}");

            // Initialize connection manager
            Logger.Info($"Initializing connection to {config.Robot.IpAddress}:{config.Robot.Port}");
            connectionManager = new SmartCar.ConnectionManager(config.Robot.IpAddress, config.Robot.Port);
            connectionManager.MessageReceived += OnMessageReceived;
            connectionManager.ConnectionStatusChanged += OnConnectionStatusChanged;

            // Initialize video viewer
            Logger.Info("Initializing video viewer");
            videoViewer = new SmartCar.VideoStreamViewer(config.Robot.IpAddress);
            videoViewer.FrameReceived += OnFrameReceived;
            videoViewer.FrameDropped += OnFrameDropped;

            // Initialize racing wheel controller (try first for G27)
            Logger.Info("Initializing racing wheel controller");
            racingWheelController = new SmartCar.RacingWheelController();
            if (racingWheelController.Initialize())
            {
                racingWheelController.InputChanged += OnRacingWheelInput;
                racingWheelController.ButtonPressed += OnRacingWheelButtonPressed;
                racingWheelController.ButtonReleased += OnRacingWheelButtonReleased;
                Logger.Info($"✓ Racing wheel ready: {racingWheelController.DeviceName}");
                Logger.Info("Press 'R' to enable racing wheel control");
                UpdateRacingWheelDisplay();
            }
            else
            {
                Logger.Info("✗ No racing wheel detected");
                racingWheelController = null;
            }

            // Initialize joystick controller (always try, even if racing wheel found)
            Logger.Info("Initializing joystick controller");
            joystickController = new SmartCar.JoystickController();
            if (joystickController.Initialize())
            {
                joystickController.MovementChanged += OnJoystickMovement;
                joystickController.ButtonPressed += OnJoystickButtonPressed;
                joystickController.ButtonReleased += OnJoystickButtonReleased;
                Logger.Info($"✓ Joystick ready: {joystickController.DeviceName}");
                Logger.Info("Press 'J' to enable joystick control");

                // Only update display if racing wheel wasn't found
                if (racingWheelController == null)
                {
                    UpdateJoystickDisplay();
                }
            }
            else
            {
                Logger.Info("✗ No joystick detected");
                joystickController = null;

                // Update display to show no controller if neither found
                if (racingWheelController == null)
                {
                    UpdateJoystickDisplay();
                }
            }

            // Connect to smart car
            UpdateStatus("Connecting to Smart Car...");
            Logger.Info("Attempting to connect...");
            bool connected = await connectionManager.ConnectAsync();

            if (!connected)
            {
                Logger.Error("Failed to connect");
                UpdateStatus("Failed to connect to Smart Car");
                MessageBox.Show($"Failed to connect to Smart Car at {config.Robot.IpAddress}:{config.Robot.Port}\n\nMake sure:\n1. Car is powered on\n2. You're connected to car's WiFi\n3. IP address is correct",
                    "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            else
            {
                Logger.Info("SUCCESS: Connected to car");
                UpdateStatus("Connected - Press WASD to drive!");
                UpdateModeDisplay(0); // Car starts in Mode 0 (Manual)
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            // Prevent repeated commands while key is held down
            if (pressedKeys.Contains(e.Key))
            {
                e.Handled = true;
                return;
            }

            pressedKeys.Add(e.Key);

            // DEBUG: Show which key was pressed
            Console.WriteLine($"Key pressed: {e.Key}");

            if (connectionManager == null)
            {
                Console.WriteLine("ERROR: connectionManager is null");
                UpdateStatus("ERROR: Connection manager not initialized");
                return;
            }

            if (!connectionManager.IsConnected)
            {
                Console.WriteLine("ERROR: Not connected to car");
                UpdateStatus("Not connected - waiting for connection...");
                return;
            }

            string? command = null;
            string action = "";

            switch (e.Key)
            {
                case Key.W:
                    lastDirection = 3;
                    command = SmartCar.SmartCarCommands.CarControl(3, config.Controls.Keyboard.ForwardSpeed);
                    action = "Moving Forward";
                    videoWindow?.UpdateCarStatus("Driving Forward");
                    break;
                case Key.S:
                    lastDirection = 4;
                    command = SmartCar.SmartCarCommands.CarControl(4, config.Controls.Keyboard.BackwardSpeed);
                    action = "Moving Backward";
                    videoWindow?.UpdateCarStatus("Reversing");
                    break;
                case Key.A:
                    lastDirection = 1;
                    command = SmartCar.SmartCarCommands.CarControl(1, config.Controls.Keyboard.TurnSpeed);
                    action = "Turning Left";
                    videoWindow?.UpdateCarStatus("Turning Left");
                    break;
                case Key.D:
                    lastDirection = 2;
                    command = SmartCar.SmartCarCommands.CarControl(2, config.Controls.Keyboard.TurnSpeed);
                    action = "Turning Right";
                    videoWindow?.UpdateCarStatus("Turning Right");
                    break;
                case Key.OemOpenBrackets: // [
                    command = SmartCar.SmartCarCommands.CameraRotation(3);
                    action = "Camera Left";
                    break;
                case Key.OemCloseBrackets: // ]
                    command = SmartCar.SmartCarCommands.CameraRotation(4);
                    action = "Camera Right";
                    break;
                case Key.J:
                    ToggleJoystickMode();
                    return;
                case Key.R:
                    ToggleRacingWheelMode();
                    return;
                case Key.V:
                    ToggleVideoStream();
                    return;
                case Key.D0: // Key 0 - Manual/Normal Mode
                    connectionManager.SwitchMode(0);
                    UpdateStatus("Mode 0: Manual/Normal - Drive with WASD");
                    UpdateModeDisplay(0);
                    videoWindow?.UpdateMode(0);
                    videoWindow?.UpdateCarStatus("Manual Control");
                    return;
                case Key.D1: // Key 1 - Line Detection Mode
                    connectionManager.SwitchMode(1);
                    UpdateStatus("Mode 1: Line Detection");
                    UpdateModeDisplay(1);
                    videoWindow?.UpdateMode(1);
                    videoWindow?.UpdateCarStatus("Autonomous - Following Line");
                    return;
                case Key.D2: // Key 2 - Obstacle Detection Mode
                    connectionManager.SwitchMode(2);
                    UpdateStatus("Mode 2: Obstacle Detection");
                    UpdateModeDisplay(2);
                    videoWindow?.UpdateMode(2);
                    videoWindow?.UpdateCarStatus("Autonomous - Avoiding Obstacles");
                    return;
                case Key.D3: // Key 3 - Follow Mode
                    connectionManager.SwitchMode(3);
                    UpdateStatus("Mode 3: Follow Mode");
                    UpdateModeDisplay(3);
                    videoWindow?.UpdateMode(3);
                    videoWindow?.UpdateCarStatus("Autonomous - Following Object");
                    return;
                case Key.Escape:
                    Close();
                    return;
            }

            if (command != null)
            {
                Console.WriteLine($"Sending command: {action}");
                Console.WriteLine($"Command JSON: {command}");
                connectionManager.SendCommand(command);
                UpdateStatus(action);
            }
            else
            {
                Console.WriteLine($"No command mapped for key: {e.Key}");
            }

            // Keep focus on window
            this.Focus();
            e.Handled = true;
        }

        private void Window_KeyUp(object sender, KeyEventArgs e)
        {
            pressedKeys.Remove(e.Key);

            // Stop robot when movement keys are released
            if (e.Key == Key.W || e.Key == Key.S || e.Key == Key.A || e.Key == Key.D)
            {
                if (connectionManager != null && connectionManager.IsConnected)
                {
                    // Send multiple stop commands to ensure motors fully power off
                    // 1. Set speed to 0
                    string stopCommand1 = SmartCar.SmartCarCommands.CarControl(lastDirection, 0);
                    Console.WriteLine($"Key released - sending stop sequence: step 1 - speed=0");
                    connectionManager.SendCommand(stopCommand1);

                    // 2. Send CarStop command (D1=5 means stop)
                    string stopCommand2 = SmartCar.SmartCarCommands.CarStop();
                    Console.WriteLine($"Key released - sending stop sequence: step 2 - CarStop()");
                    connectionManager.SendCommand(stopCommand2);

                    // 3. Clear joystick state to fully release motors
                    string clearCommand = SmartCar.SmartCarCommands.JoystickClear();
                    Console.WriteLine($"Key released - sending stop sequence: step 3 - JoystickClear()");
                    connectionManager.SendCommand(clearCommand);
                }
                videoWindow?.UpdateCarStatus("Ready");
            }

            e.Handled = true;
        }

        private async void ToggleVideoStream()
        {
            if (videoViewer == null) return;

            videoEnabled = !videoEnabled;

            if (videoEnabled)
            {
                UpdateStatus("Starting video stream...");

                // Create and show video window
                videoWindow = new SmartCar.VideoViewerWindow();
                videoWindow.Show();

                // Initialize video window status
                videoWindow.UpdateMode(0); // Start in manual mode
                videoWindow.UpdateCarStatus("Ready");
                videoWindow.UpdateWiFiSignal(100); // Assume excellent signal initially

                // Start streaming
                await videoViewer.StartAsync();

                // Start WiFi signal monitoring timer
                statusUpdateTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(config.Video.StatusUpdateIntervalSeconds)
                };
                statusUpdateTimer.Tick += (s, ev) =>
                {
                    // Simulate WiFi signal strength based on connection status
                    if (connectionManager?.IsConnected == true)
                    {
                        videoWindow?.UpdateWiFiSignal(85); // Good signal
                    }
                    else
                    {
                        videoWindow?.UpdateWiFiSignal(0); // No signal
                    }
                };
                statusUpdateTimer.Start();

                VideoStatus.Text = "On";
                VideoStatus.Foreground = new SolidColorBrush(Color.FromRgb(78, 201, 176));
            }
            else
            {
                UpdateStatus("Stopping video stream...");

                // Stop status update timer
                if (statusUpdateTimer != null)
                {
                    statusUpdateTimer.Stop();
                    statusUpdateTimer = null;
                }

                // Stop streaming
                videoViewer.Stop();

                // Close video window
                if (videoWindow != null)
                {
                    videoWindow.Close();
                    videoWindow = null;
                }

                VideoStatus.Text = "Off";
                VideoStatus.Foreground = new SolidColorBrush(Color.FromRgb(170, 170, 170));
            }
        }

        private void OnMessageReceived(object? sender, string message)
        {
            // Handle car responses
            Dispatcher.InvokeAsync(() =>
            {
                // Could display in status bar if needed
            });
        }

        private void OnConnectionStatusChanged(object? sender, bool isConnected)
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (isConnected)
                {
                    ConnectionIndicator.Fill = new SolidColorBrush(Color.FromRgb(78, 201, 176));
                    ConnectionStatus.Text = "Connected";
                    ConnectionStatus.Foreground = new SolidColorBrush(Color.FromRgb(78, 201, 176));
                    UpdateStatus("Connected - Ready to drive!");
                }
                else
                {
                    ConnectionIndicator.Fill = new SolidColorBrush(Color.FromRgb(244, 71, 71));
                    ConnectionStatus.Text = "Disconnected";
                    ConnectionStatus.Foreground = new SolidColorBrush(Color.FromRgb(244, 71, 71));
                    UpdateStatus("Disconnected from Smart Car");
                }
            });
        }

        private void OnFrameReceived(object? sender, Mat frame)
        {
            try
            {
                if (!videoEnabled)
                {
                    Console.WriteLine("Video disabled, disposing frame");
                    frame?.Dispose();
                    return;
                }

                if (frame == null)
                {
                    Console.WriteLine("WARNING: Received null frame");
                    return;
                }

                if (frame.IsDisposed)
                {
                    Console.WriteLine("WARNING: Received disposed frame");
                    return;
                }

                if (frame.Empty())
                {
                    Console.WriteLine("WARNING: Received empty frame");
                    frame.Dispose();
                    return;
                }

                if (videoWindow == null)
                {
                    Console.WriteLine("WARNING: Video window is null, disposing frame");
                    frame.Dispose();
                    return;
                }

                // UpdateFrame will handle the frame and dispose it
                videoWindow.UpdateFrame(frame);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR in OnFrameReceived: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                frame?.Dispose();
            }
        }

        private void OnFrameDropped(object? sender, EventArgs e)
        {
            videoWindow?.NotifyFrameDropped();
        }

        private void UpdateStatus(string message)
        {
            Dispatcher.InvokeAsync(() =>
            {
                StatusMessage.Text = message;
            });
        }

        private void UpdateModeDisplay(int mode)
        {
            Dispatcher.InvokeAsync(() =>
            {
                string modeText = mode switch
                {
                    0 => "0 - Manual",
                    1 => "1 - Line Detection",
                    2 => "2 - Obstacle Avoidance",
                    3 => "3 - Follow Mode",
                    _ => "Unknown"
                };
                CurrentModeText.Text = modeText;

                // Update color based on mode
                var color = mode > 0
                    ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 193, 7)) // Orange for autonomous
                    : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(78, 201, 176)); // Green for manual
                CurrentModeText.Foreground = color;
            });
        }

        private void UpdateJoystickDisplay()
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (joystickController == null)
                {
                    JoystickStatusText.Text = "Not Connected";
                    JoystickStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(170, 170, 170)); // Gray
                    JoystickDeviceText.Text = "";
                }
                else if (joystickEnabled)
                {
                    JoystickStatusText.Text = "Enabled";
                    JoystickStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(78, 201, 176)); // Green
                    JoystickDeviceText.Text = $"({joystickController.DeviceName})";
                }
                else
                {
                    JoystickStatusText.Text = "Disabled";
                    JoystickStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(170, 170, 170)); // Gray
                    JoystickDeviceText.Text = $"({joystickController.DeviceName})";
                }
            });
        }

        private void ToggleJoystickMode()
        {
            if (joystickController == null)
            {
                UpdateStatus("No joystick detected");
                MessageBox.Show("No joystick or gamepad detected!\n\nPlease connect your Thrustmaster TCA Sidestick and restart the application.",
                    "Joystick Not Found", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            joystickEnabled = !joystickEnabled;

            if (joystickEnabled)
            {
                joystickController.Start();
                UpdateStatus($"Joystick Enabled: {joystickController.DeviceName}");
                UpdateJoystickDisplay();
                Logger.Info($"Enabled - {joystickController.DeviceName}");
            }
            else
            {
                joystickController.Stop();
                // Send stop command when disabling
                if (connectionManager != null && connectionManager.IsConnected)
                {
                    connectionManager.SendCommand(SmartCar.SmartCarCommands.CarControl(3, 0));
                    connectionManager.SendCommand(SmartCar.SmartCarCommands.CarStop());
                    connectionManager.SendCommand(SmartCar.SmartCarCommands.JoystickClear());
                }
                UpdateStatus("Joystick Disabled - Keyboard Control");
                UpdateJoystickDisplay();
                Logger.Info("Disabled");
            }
        }

        private void OnJoystickMovement(object? sender, SmartCar.JoystickMovementEventArgs e)
        {
            if (!joystickEnabled || connectionManager == null || !connectionManager.IsConnected)
                return;

            // Calculate speed and direction from X and Y axes
            int x = e.X;  // -100 (left) to +100 (right)
            int y = e.Y;  // -100 (back) to +100 (forward)

            // Only send command if position changed significantly (throttling to prevent buzzing)
            int deltaX = Math.Abs(x - lastJoystickX);
            int deltaY = Math.Abs(y - lastJoystickY);
            var timeSinceLastCommand = (DateTime.Now - lastJoystickCommandTime).TotalMilliseconds;

            // Skip if position hasn't changed much and we recently sent a command
            if (deltaX < config.Controls.Joystick.PositionChangeDelta &&
                deltaY < config.Controls.Joystick.PositionChangeDelta &&
                timeSinceLastCommand < config.Controls.Joystick.CommandThrottleMs)
            {
                return; // No significant change, don't spam commands
            }

            lastJoystickX = x;
            lastJoystickY = y;
            lastJoystickCommandTime = DateTime.Now;

            // Dead zone check
            if (Math.Abs(x) < 5 && Math.Abs(y) < 5)
            {
                // Centered - send complete stop sequence
                connectionManager.SendCommand(SmartCar.SmartCarCommands.CarControl(3, 0));
                connectionManager.SendCommand(SmartCar.SmartCarCommands.CarStop());
                connectionManager.SendCommand(SmartCar.SmartCarCommands.JoystickClear());
                videoWindow?.UpdateCarStatus("Ready");
                return;
            }

            // Determine primary direction and speed
            int speed = (int)Math.Min(Math.Sqrt(x * x + y * y), 100);
            int mappedSpeed = (int)((speed / 100.0) * config.Controls.Joystick.MaxSpeed); // Map 0-100 to configured max

            // Determine direction based on dominant axis
            int direction;
            string status;

            if (Math.Abs(y) > Math.Abs(x))
            {
                // Forward/backward dominant
                if (y > 0)
                {
                    direction = 3; // Forward
                    status = $"Forward ({speed}%)";
                }
                else
                {
                    direction = 4; // Backward
                    status = $"Backward ({speed}%)";
                }
            }
            else
            {
                // Left/right dominant
                if (x > 0)
                {
                    direction = 2; // Right
                    status = $"Right ({speed}%)";
                }
                else
                {
                    direction = 1; // Left
                    status = $"Left ({speed}%)";
                }
            }

            // Send command
            string command = SmartCar.SmartCarCommands.CarControl(direction, mappedSpeed);
            connectionManager.SendCommand(command);
            videoWindow?.UpdateCarStatus(status);
        }

        private void OnJoystickButtonPressed(object? sender, SmartCar.JoystickButtonEventArgs e)
        {
            Logger.Debug($"Button {e.ButtonIndex} pressed");

            // Button mapping for Thrustmaster TCA Sidestick
            switch (e.ButtonIndex)
            {
                case 0: // Trigger - Speed boost or action
                    // Could implement speed boost here
                    break;
                case 1: // Button 2 - Toggle video
                    Dispatcher.InvokeAsync(() => ToggleVideoStream());
                    break;
                case 2: // Button 3 - Mode 1
                    Dispatcher.InvokeAsync(() =>
                    {
                        connectionManager?.SwitchMode(1);
                        UpdateStatus("Mode 1: Line Detection");
                        videoWindow?.UpdateMode(1);
                    });
                    break;
                case 3: // Button 4 - Mode 2
                    Dispatcher.InvokeAsync(() =>
                    {
                        connectionManager?.SwitchMode(2);
                        UpdateStatus("Mode 2: Obstacle Detection");
                        videoWindow?.UpdateMode(2);
                    });
                    break;
                case 4: // Button 5 - Mode 3
                    Dispatcher.InvokeAsync(() =>
                    {
                        connectionManager?.SwitchMode(3);
                        UpdateStatus("Mode 3: Follow Mode");
                        videoWindow?.UpdateMode(3);
                    });
                    break;
            }
        }

        private void OnJoystickButtonReleased(object? sender, SmartCar.JoystickButtonEventArgs e)
        {
            // Handle button release if needed
        }

        private void UpdateRacingWheelDisplay()
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (racingWheelController == null)
                {
                    JoystickStatusText.Text = "Not Connected";
                    JoystickStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(170, 170, 170)); // Gray
                    JoystickDeviceText.Text = "";
                }
                else if (racingWheelEnabled)
                {
                    JoystickStatusText.Text = "Racing Wheel";
                    JoystickStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(78, 201, 176)); // Green
                    JoystickDeviceText.Text = $"({racingWheelController.DeviceName})";
                }
                else
                {
                    JoystickStatusText.Text = "Wheel Standby";
                    JoystickStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(170, 170, 170)); // Gray
                    JoystickDeviceText.Text = $"({racingWheelController.DeviceName})";
                }
            });
        }

        private void ToggleRacingWheelMode()
        {
            if (racingWheelController == null)
            {
                UpdateStatus("No racing wheel detected");
                MessageBox.Show("No racing wheel detected!\n\nPlease connect your Logitech G27 racing wheel and restart the application.",
                    "Racing Wheel Not Found", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Disable joystick if it was enabled
            if (joystickEnabled && joystickController != null)
            {
                joystickController.Stop();
                joystickEnabled = false;
                UpdateJoystickDisplay();
            }

            racingWheelEnabled = !racingWheelEnabled;

            if (racingWheelEnabled)
            {
                racingWheelController.Start();
                UpdateStatus($"Racing Wheel Enabled: {racingWheelController.DeviceName}");
                UpdateRacingWheelDisplay();
                Logger.Info($"Enabled - {racingWheelController.DeviceName}");
            }
            else
            {
                racingWheelController.Stop();
                // Send stop command when disabling
                if (connectionManager != null && connectionManager.IsConnected)
                {
                    connectionManager.SendCommand(SmartCar.SmartCarCommands.CarControl(3, 0));
                    connectionManager.SendCommand(SmartCar.SmartCarCommands.CarStop());
                    connectionManager.SendCommand(SmartCar.SmartCarCommands.JoystickClear());
                }
                UpdateStatus("Racing Wheel Disabled - Keyboard Control");
                UpdateRacingWheelDisplay();
                Logger.Info("Disabled");
            }
        }

        private void OnRacingWheelInput(object? sender, SmartCar.RacingWheelInputEventArgs e)
        {
            if (!racingWheelEnabled || connectionManager == null || !connectionManager.IsConnected)
                return;

            int steering = -e.Steering;  // INVERTED: -100 (left) to +100 (right)
            int throttle = e.Throttle;   // 0 to 100
            int brake = e.Brake;         // 0 to 100

            // Debug: Log all inputs to diagnose steering
            if (throttle > 5 || brake > 5 || Math.Abs(steering) > 10)
            {
                Logger.Debug($"Wheel Input - Steering:{steering} Throttle:{throttle} Brake:{brake}");
            }

            // Throttling to prevent command spam
            int deltaSteering = Math.Abs(steering - lastWheelSteering);
            int deltaThrottle = Math.Abs(throttle - lastWheelThrottle);
            var timeSinceLastCommand = (DateTime.Now - lastWheelCommandTime).TotalMilliseconds;

            if (deltaSteering < config.Controls.RacingWheel.InputChangeDelta &&
                deltaThrottle < config.Controls.RacingWheel.InputChangeDelta &&
                timeSinceLastCommand < config.Controls.RacingWheel.CommandThrottleMs)
            {
                return; // No significant change
            }

            lastWheelSteering = steering;
            lastWheelThrottle = throttle;
            lastWheelCommandTime = DateTime.Now;

            // Calculate motor speeds with differential steering
            // Throttle pedal (right) = FORWARD only
            // Brake pedal (middle) = REVERSE only
            int baseSpeed = 0;
            bool isReverse = false;
            const int MIN_MOTOR_SPEED = 40; // Minimum speed to prevent buzzing

            if (throttle > 5) // Throttle/Gas pedal (right) = Forward
            {
                baseSpeed = (int)((throttle / 100.0) * config.Controls.RacingWheel.ThrottleMaxSpeed);
                isReverse = false;
            }
            else if (brake > config.Controls.RacingWheel.BrakeThreshold) // Brake pedal (middle) = Reverse
            {
                baseSpeed = (int)((brake / 100.0) * config.Controls.RacingWheel.BrakeMaxSpeed);
                isReverse = true;
            }

            // Apply differential steering for forward motion
            // steering: -100 = full left, 0 = straight, +100 = full right
            int leftSpeed = baseSpeed;
            int rightSpeed = baseSpeed;

            if (!isReverse && Math.Abs(steering) > 5 && baseSpeed > 0) // Apply steering when moving forward
            {
                float steeringFactor = steering / 100.0f; // -1.0 to +1.0

                if (steeringFactor > 0) // Turning right
                {
                    // Reduce right motor speed
                    int reducedSpeed = (int)(baseSpeed * (1.0f - Math.Abs(steeringFactor) * config.Controls.RacingWheel.SteeringFactor));

                    // Prevent motor buzzing - if speed too low, stop that motor
                    if (reducedSpeed < MIN_MOTOR_SPEED)
                    {
                        rightSpeed = 0; // Stop right motor for sharp turn
                        Logger.Debug($"Sharp RIGHT - L:{leftSpeed} R:0 (stopped to prevent buzz)");
                    }
                    else
                    {
                        rightSpeed = reducedSpeed;
                        Logger.Debug($"RIGHT - L:{leftSpeed} R:{rightSpeed}");
                    }
                }
                else // Turning left
                {
                    // Reduce left motor speed
                    int reducedSpeed = (int)(baseSpeed * (1.0f - Math.Abs(steeringFactor) * config.Controls.RacingWheel.SteeringFactor));

                    // Prevent motor buzzing - if speed too low, stop that motor
                    if (reducedSpeed < MIN_MOTOR_SPEED)
                    {
                        leftSpeed = 0; // Stop left motor for sharp turn
                        Logger.Debug($"Sharp LEFT - L:0 (stopped to prevent buzz) R:{rightSpeed}");
                    }
                    else
                    {
                        leftSpeed = reducedSpeed;
                        Logger.Debug($"LEFT - L:{leftSpeed} R:{rightSpeed}");
                    }
                }
            }

            // Clamp speeds
            leftSpeed = Math.Clamp(leftSpeed, 0, 255);
            rightSpeed = Math.Clamp(rightSpeed, 0, 255);

            // Send motor control commands
            if (baseSpeed < 5) // Stopped
            {
                connectionManager.SendCommand(SmartCar.SmartCarCommands.CarControl(3, 0));
                connectionManager.SendCommand(SmartCar.SmartCarCommands.CarStop());
                connectionManager.SendCommand(SmartCar.SmartCarCommands.JoystickClear());
                videoWindow?.UpdateCarStatus("Ready");
            }
            else if (isReverse)
            {
                // REVERSE: Use CarControl with direction=4 (backward)
                // Note: Differential steering not supported in reverse for simplicity
                string command = SmartCar.SmartCarCommands.CarControl(4, baseSpeed);
                connectionManager.SendCommand(command);

                string status = $"Reverse ({brake}%)";
                videoWindow?.UpdateCarStatus(status);
            }
            else
            {
                // FORWARD: Use MotorControlSpeed for differential steering
                string command = SmartCar.SmartCarCommands.MotorControlSpeed(leftSpeed, rightSpeed);
                connectionManager.SendCommand(command);

                string steeringInfo = Math.Abs(steering) > 10 ?
                    (steering > 0 ? " Right" : " Left") : "";
                string status = $"Forward{steeringInfo} ({throttle}%)";
                videoWindow?.UpdateCarStatus(status);
            }
        }

        private void OnRacingWheelButtonPressed(object? sender, SmartCar.RacingWheelButtonEventArgs e)
        {
            Logger.Debug($"Button {e.ButtonIndex} pressed");

            // Button mapping for Logitech G27
            switch (e.ButtonIndex)
            {
                case 0: // Button 1 on wheel - Toggle video
                    Dispatcher.InvokeAsync(() => ToggleVideoStream());
                    break;
                case 1: // Button 2 - Mode 0 (Manual)
                    Dispatcher.InvokeAsync(() =>
                    {
                        connectionManager?.SwitchMode(0);
                        UpdateStatus("Mode 0: Manual");
                        UpdateModeDisplay(0);
                        videoWindow?.UpdateMode(0);
                    });
                    break;
                case 2: // Button 3 - Mode 1 (Line Detection)
                    Dispatcher.InvokeAsync(() =>
                    {
                        connectionManager?.SwitchMode(1);
                        UpdateStatus("Mode 1: Line Detection");
                        UpdateModeDisplay(1);
                        videoWindow?.UpdateMode(1);
                    });
                    break;
                case 3: // Button 4 - Mode 2 (Obstacle Avoidance)
                    Dispatcher.InvokeAsync(() =>
                    {
                        connectionManager?.SwitchMode(2);
                        UpdateStatus("Mode 2: Obstacle Detection");
                        UpdateModeDisplay(2);
                        videoWindow?.UpdateMode(2);
                    });
                    break;
                case 4: // Button 5 - Mode 3 (Follow)
                    Dispatcher.InvokeAsync(() =>
                    {
                        connectionManager?.SwitchMode(3);
                        UpdateStatus("Mode 3: Follow Mode");
                        UpdateModeDisplay(3);
                        videoWindow?.UpdateMode(3);
                    });
                    break;
            }
        }

        private void OnRacingWheelButtonReleased(object? sender, SmartCar.RacingWheelButtonEventArgs e)
        {
            // Handle button release if needed
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // Cleanup
            joystickController?.Stop();
            joystickController?.Dispose();
            racingWheelController?.Stop();
            racingWheelController?.Dispose();
            videoViewer?.Stop();
            videoViewer?.Dispose();
            videoWindow?.Close();
            connectionManager?.Dispose();
        }
    }
}
