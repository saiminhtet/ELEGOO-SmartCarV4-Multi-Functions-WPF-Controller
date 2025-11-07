using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SharpDX.DirectInput;

namespace SmartCar
{
    public class JoystickController : IDisposable
    {
        private DirectInput _directInput;
        private Joystick? _joystick;
        private CancellationTokenSource? _cancellationSource;
        private Task? _pollTask;
        private bool _isRunning;

        // Joystick state
        private int _lastX = 0;
        private int _lastY = 0;
        private bool[] _lastButtons = new bool[32];

        // Deadzone configuration
        private const int DEADZONE = 5000; // Out of 65535 range (about 7.6%)
        private const int MAX_AXIS_VALUE = 65535;

        // Events
        public event EventHandler<JoystickMovementEventArgs>? MovementChanged;
        public event EventHandler<JoystickButtonEventArgs>? ButtonPressed;
        public event EventHandler<JoystickButtonEventArgs>? ButtonReleased;

        public bool IsConnected => _joystick != null;
        public string DeviceName => _joystick?.Information.ProductName ?? "No Device";

        public JoystickController()
        {
            _directInput = new DirectInput();
            _lastButtons = new bool[32];
        }

        public bool Initialize()
        {
            try
            {
                // Find all joystick devices
                var joystickGuid = Guid.Empty;

                foreach (var deviceInstance in _directInput.GetDevices(DeviceType.Joystick, DeviceEnumerationFlags.AllDevices))
                {
                    joystickGuid = deviceInstance.InstanceGuid;
                    Console.WriteLine($"[Joystick] Found: {deviceInstance.ProductName}");
                    break; // Use first joystick found
                }

                if (joystickGuid == Guid.Empty)
                {
                    // Try gamepad if no joystick found
                    foreach (var deviceInstance in _directInput.GetDevices(DeviceType.Gamepad, DeviceEnumerationFlags.AllDevices))
                    {
                        joystickGuid = deviceInstance.InstanceGuid;
                        Console.WriteLine($"[Joystick] Found gamepad: {deviceInstance.ProductName}");
                        break;
                    }
                }

                if (joystickGuid == Guid.Empty)
                {
                    Console.WriteLine("[Joystick] ERROR: No joystick or gamepad found");
                    return false;
                }

                // Instantiate the joystick
                _joystick = new Joystick(_directInput, joystickGuid);
                Console.WriteLine($"[Joystick] Initialized: {_joystick.Information.ProductName}");

                // Set cooperative level
                _joystick.Properties.BufferSize = 128;

                // Acquire the joystick
                _joystick.Acquire();

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Joystick] Initialization error: {ex.Message}");
                return false;
            }
        }

        public void Start()
        {
            if (_joystick == null)
            {
                Console.WriteLine("[Joystick] ERROR: Joystick not initialized");
                return;
            }

            if (_isRunning)
            {
                Console.WriteLine("[Joystick] Already running");
                return;
            }

            _isRunning = true;
            _cancellationSource = new CancellationTokenSource();
            _pollTask = Task.Run(() => PollLoopAsync(_cancellationSource.Token));
            Console.WriteLine("[Joystick] Started polling");
        }

        public void Stop()
        {
            if (!_isRunning) return;

            _isRunning = false;
            _cancellationSource?.Cancel();
            _pollTask?.Wait(1000);
            Console.WriteLine("[Joystick] Stopped polling");
        }

        private async Task PollLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _isRunning)
            {
                try
                {
                    if (_joystick == null) break;

                    // Poll the joystick
                    _joystick.Poll();
                    var state = _joystick.GetCurrentState();

                    // Process axes (X and Y)
                    int x = state.X;
                    int y = state.Y;

                    // Apply deadzone
                    int centerX = MAX_AXIS_VALUE / 2;
                    int centerY = MAX_AXIS_VALUE / 2;

                    int deltaX = x - centerX;
                    int deltaY = y - centerY;

                    if (Math.Abs(deltaX) < DEADZONE) deltaX = 0;
                    if (Math.Abs(deltaY) < DEADZONE) deltaY = 0;

                    // Check if movement changed
                    if (deltaX != _lastX || deltaY != _lastY)
                    {
                        _lastX = deltaX;
                        _lastY = deltaY;

                        // Convert to -100 to +100 range
                        int normalizedX = (int)((deltaX / (float)(centerX)) * 100);
                        int normalizedY = (int)((deltaY / (float)(centerY)) * 100);

                        // Invert Y (stick forward = negative, we want positive)
                        normalizedY = -normalizedY;

                        MovementChanged?.Invoke(this, new JoystickMovementEventArgs(normalizedX, normalizedY));
                    }

                    // Process buttons
                    var buttons = state.Buttons;
                    for (int i = 0; i < Math.Min(buttons.Length, _lastButtons.Length); i++)
                    {
                        bool pressed = buttons[i];
                        bool wasPressed = _lastButtons[i];

                        if (pressed && !wasPressed)
                        {
                            ButtonPressed?.Invoke(this, new JoystickButtonEventArgs(i));
                        }
                        else if (!pressed && wasPressed)
                        {
                            ButtonReleased?.Invoke(this, new JoystickButtonEventArgs(i));
                        }

                        _lastButtons[i] = pressed;
                    }

                    // Poll at 60Hz
                    await Task.Delay(16, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Joystick] Poll error: {ex.Message}");
                    await Task.Delay(1000, token);
                }
            }
        }

        public void Dispose()
        {
            Stop();
            _joystick?.Unacquire();
            _joystick?.Dispose();
            _directInput?.Dispose();
        }
    }

    public class JoystickMovementEventArgs : EventArgs
    {
        public int X { get; }  // -100 to +100 (left to right)
        public int Y { get; }  // -100 to +100 (backward to forward)

        public JoystickMovementEventArgs(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    public class JoystickButtonEventArgs : EventArgs
    {
        public int ButtonIndex { get; }

        public JoystickButtonEventArgs(int buttonIndex)
        {
            ButtonIndex = buttonIndex;
        }
    }
}
