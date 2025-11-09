using System;
using System.IO;
using Newtonsoft.Json;

namespace SmartCar
{
    public class AppConfiguration
    {
        public RobotSettings Robot { get; set; } = new RobotSettings();
        public ControlSettings Controls { get; set; } = new ControlSettings();
        public VideoSettings Video { get; set; } = new VideoSettings();

        public static AppConfiguration Load(string filePath = "application.json")
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    Console.WriteLine($"[Config] Configuration file not found: {filePath}");
                    Console.WriteLine("[Config] Using default configuration");
                    var defaultConfig = CreateDefault();
                    SaveDefault(filePath, defaultConfig);
                    return defaultConfig;
                }

                string json = File.ReadAllText(filePath);
                var config = JsonConvert.DeserializeObject<AppConfiguration>(json);

                if (config == null)
                {
                    Console.WriteLine("[Config] Failed to parse configuration, using defaults");
                    return CreateDefault();
                }

                Console.WriteLine($"[Config] Configuration loaded from {filePath}");
                return config;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Config] Error loading configuration: {ex.Message}");
                Console.WriteLine("[Config] Using default configuration");
                return CreateDefault();
            }
        }

        private static AppConfiguration CreateDefault()
        {
            return new AppConfiguration
            {
                Robot = new RobotSettings
                {
                    IpAddress = "192.168.4.1",
                    Port = 100,
                    ConnectionTimeoutMs = 5000
                },
                Controls = new ControlSettings
                {
                    Keyboard = new KeyboardSettings
                    {
                        ForwardSpeed = 100,
                        BackwardSpeed = 100,
                        TurnSpeed = 100
                    },
                    Joystick = new JoystickSettings
                    {
                        Deadzone = 5000,
                        MaxAxisValue = 65535,
                        MaxSpeed = 200,
                        CommandThrottleMs = 100,
                        PositionChangeDelta = 3
                    },
                    RacingWheel = new RacingWheelSettings
                    {
                        SteeringDeadzone = 2000,
                        PedalDeadzone = 1000,
                        MaxAxisValue = 65535,
                        ThrottleMaxSpeed = 200,
                        BrakeMaxSpeed = 150,
                        BrakeThreshold = 15,
                        SteeringFactor = 0.65f,
                        CommandThrottleMs = 80,
                        InputChangeDelta = 5
                    }
                },
                Video = new VideoSettings
                {
                    Enabled = false,
                    StatusUpdateIntervalSeconds = 2
                }
            };
        }

        private static void SaveDefault(string filePath, AppConfiguration config)
        {
            try
            {
                string json = JsonConvert.SerializeObject(config, Formatting.Indented);
                File.WriteAllText(filePath, json);
                Console.WriteLine($"[Config] Default configuration saved to {filePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Config] Failed to save default configuration: {ex.Message}");
            }
        }
    }

    public class RobotSettings
    {
        public string IpAddress { get; set; } = "192.168.4.1";
        public int Port { get; set; } = 100;
        public int ConnectionTimeoutMs { get; set; } = 5000;
    }

    public class ControlSettings
    {
        public KeyboardSettings Keyboard { get; set; } = new KeyboardSettings();
        public JoystickSettings Joystick { get; set; } = new JoystickSettings();
        public RacingWheelSettings RacingWheel { get; set; } = new RacingWheelSettings();
    }

    public class KeyboardSettings
    {
        public int ForwardSpeed { get; set; } = 100;
        public int BackwardSpeed { get; set; } = 100;
        public int TurnSpeed { get; set; } = 100;
    }

    public class JoystickSettings
    {
        public int Deadzone { get; set; } = 5000;
        public int MaxAxisValue { get; set; } = 65535;
        public int MaxSpeed { get; set; } = 200;
        public int CommandThrottleMs { get; set; } = 100;
        public int PositionChangeDelta { get; set; } = 3;
    }

    public class RacingWheelSettings
    {
        public int SteeringDeadzone { get; set; } = 2000;
        public int PedalDeadzone { get; set; } = 1000;
        public int MaxAxisValue { get; set; } = 65535;
        public int ThrottleMaxSpeed { get; set; } = 200;
        public int BrakeMaxSpeed { get; set; } = 150;
        public int BrakeThreshold { get; set; } = 15;
        public float SteeringFactor { get; set; } = 0.7f;
        public int CommandThrottleMs { get; set; } = 100;
        public int InputChangeDelta { get; set; } = 3;
    }

    public class VideoSettings
    {
        public bool Enabled { get; set; } = false;
        public int StatusUpdateIntervalSeconds { get; set; } = 2;
    }
}
