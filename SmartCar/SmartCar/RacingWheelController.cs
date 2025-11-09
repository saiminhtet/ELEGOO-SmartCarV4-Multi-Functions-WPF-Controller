using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SharpDX.DirectInput;
using NLog;

namespace SmartCar
{
    public class RacingWheelController : IDisposable
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private DirectInput _directInput;
        private Joystick? _wheel;
        private CancellationTokenSource? _cancellationSource;
        private Task? _pollTask;
        private bool _isRunning;

        // Wheel state
        private int _lastSteering = 0;
        private int _lastThrottle = 0;
        private int _lastBrake = 0;
        private bool[] _lastButtons = new bool[32];

        // Deadzone configuration
        private const int STEERING_DEADZONE = 2000; // Smaller deadzone for steering precision
        private const int PEDAL_DEADZONE = 1000;    // Raw axis deadzone for pedals (out of 65535)
        private const int MAX_AXIS_VALUE = 65535;
        private const int PEDAL_DEADZONE_PERCENT = 2; // Percentage deadzone (2% of pedal travel)

        // Events
        public event EventHandler<RacingWheelInputEventArgs>? InputChanged;
        public event EventHandler<RacingWheelButtonEventArgs>? ButtonPressed;
        public event EventHandler<RacingWheelButtonEventArgs>? ButtonReleased;

        public bool IsConnected => _wheel != null;
        public string DeviceName => _wheel?.Information.ProductName ?? "No Device";

        public RacingWheelController()
        {
            _directInput = new DirectInput();
            _lastButtons = new bool[32];
        }

        public bool Initialize()
        {
            try
            {
                // Find Logitech G27 or similar racing wheel
                var wheelGuid = Guid.Empty;

                // First, try to find Logitech devices specifically
                foreach (var deviceInstance in _directInput.GetDevices(DeviceClass.All, DeviceEnumerationFlags.AllDevices))
                {
                    string productName = deviceInstance.ProductName.ToLower();

                    // Look for Logitech racing wheels
                    if (productName.Contains("g27") ||
                        productName.Contains("g29") ||
                        productName.Contains("g920") ||
                        productName.Contains("driving force") ||
                        productName.Contains("logitech") && productName.Contains("wheel"))
                    {
                        wheelGuid = deviceInstance.InstanceGuid;
                        Logger.Info($"Found Logitech racing wheel: {deviceInstance.ProductName}");
                        break;
                    }
                }

                // If no Logitech wheel found, try any wheel/joystick with "wheel" in name
                if (wheelGuid == Guid.Empty)
                {
                    foreach (var deviceInstance in _directInput.GetDevices(DeviceType.Driving, DeviceEnumerationFlags.AllDevices))
                    {
                        wheelGuid = deviceInstance.InstanceGuid;
                        Logger.Info($"Found driving device: {deviceInstance.ProductName}");
                        break;
                    }
                }

                if (wheelGuid == Guid.Empty)
                {
                    Logger.Warn("No racing wheel found");
                    return false;
                }

                // Instantiate the wheel
                _wheel = new Joystick(_directInput, wheelGuid);
                Logger.Info($"Initialized: {_wheel.Information.ProductName}");

                // Set cooperative level
                _wheel.Properties.BufferSize = 128;

                // Acquire the wheel
                _wheel.Acquire();

                return true;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Initialization error");
                return false;
            }
        }

        public void Start()
        {
            if (_wheel == null)
            {
                Logger.Error("Racing wheel not initialized");
                return;
            }

            if (_isRunning)
            {
                Logger.Debug("Already running");
                return;
            }

            _isRunning = true;
            _cancellationSource = new CancellationTokenSource();
            _pollTask = Task.Run(() => PollLoopAsync(_cancellationSource.Token));
            Logger.Info("Started polling");
        }

        public void Stop()
        {
            if (!_isRunning) return;

            _isRunning = false;
            _cancellationSource?.Cancel();
            _pollTask?.Wait(1000);
            Logger.Info("Stopped polling");
        }

        private async Task PollLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _isRunning)
            {
                try
                {
                    if (_wheel == null) break;

                    // Poll the wheel
                    _wheel.Poll();
                    var state = _wheel.GetCurrentState();

                    // Process steering (X axis) - G27 uses full rotation
                    int steering = state.X;

                    // Process throttle (Y axis) - gas pedal (right)
                    int throttle = state.Y;

                    // Process brake - try RotationZ first (common for G27), fallback to Z
                    // On many Logitech wheels, RotationZ is the brake pedal (middle)
                    int brake = state.RotationZ;

                    // If RotationZ is at rest position (near center), use Z axis instead
                    if (Math.Abs(brake - (MAX_AXIS_VALUE / 2)) < 1000)
                    {
                        brake = state.Z;
                    }

                    // Apply deadzones
                    int centerSteering = MAX_AXIS_VALUE / 2;
                    int deltaSteering = steering - centerSteering;

                    if (Math.Abs(deltaSteering) < STEERING_DEADZONE)
                        deltaSteering = 0;

                    // Pedals are typically 0-65535, where 0 = fully pressed
                    // Normalize to 0-100 where 100 = fully pressed
                    int normalizedThrottle = (int)(((MAX_AXIS_VALUE - throttle) / (float)MAX_AXIS_VALUE) * 100);
                    int normalizedBrake = (int)(((MAX_AXIS_VALUE - brake) / (float)MAX_AXIS_VALUE) * 100);

                    // Apply pedal deadzone (use percentage threshold for clarity)
                    if (normalizedThrottle < PEDAL_DEADZONE_PERCENT) normalizedThrottle = 0;
                    if (normalizedBrake < PEDAL_DEADZONE_PERCENT) normalizedBrake = 0;

                    // Check if inputs changed
                    if (deltaSteering != _lastSteering ||
                        normalizedThrottle != _lastThrottle ||
                        normalizedBrake != _lastBrake)
                    {
                        _lastSteering = deltaSteering;
                        _lastThrottle = normalizedThrottle;
                        _lastBrake = normalizedBrake;

                        // Convert steering to -100 to +100 range
                        int normalizedSteering = (int)((deltaSteering / (float)(centerSteering)) * 100);
                        normalizedSteering = Math.Clamp(normalizedSteering, -100, 100);

                        InputChanged?.Invoke(this, new RacingWheelInputEventArgs(
                            normalizedSteering,
                            normalizedThrottle,
                            normalizedBrake));
                    }

                    // Process buttons
                    var buttons = state.Buttons;
                    for (int i = 0; i < Math.Min(buttons.Length, _lastButtons.Length); i++)
                    {
                        bool pressed = buttons[i];
                        bool wasPressed = _lastButtons[i];

                        if (pressed && !wasPressed)
                        {
                            ButtonPressed?.Invoke(this, new RacingWheelButtonEventArgs(i));
                        }
                        else if (!pressed && wasPressed)
                        {
                            ButtonReleased?.Invoke(this, new RacingWheelButtonEventArgs(i));
                        }

                        _lastButtons[i] = pressed;
                    }

                    // Poll at 60Hz for responsive controls
                    await Task.Delay(16, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Poll error");
                    await Task.Delay(1000, token);
                }
            }
        }

        public void Dispose()
        {
            Stop();
            _wheel?.Unacquire();
            _wheel?.Dispose();
            _directInput?.Dispose();
        }
    }

    public class RacingWheelInputEventArgs : EventArgs
    {
        public int Steering { get; }    // -100 (full left) to +100 (full right)
        public int Throttle { get; }    // 0 (not pressed) to 100 (fully pressed)
        public int Brake { get; }       // 0 (not pressed) to 100 (fully pressed)

        public RacingWheelInputEventArgs(int steering, int throttle, int brake)
        {
            Steering = steering;
            Throttle = throttle;
            Brake = brake;
        }
    }

    public class RacingWheelButtonEventArgs : EventArgs
    {
        public int ButtonIndex { get; }

        public RacingWheelButtonEventArgs(int buttonIndex)
        {
            ButtonIndex = buttonIndex;
        }
    }
}
