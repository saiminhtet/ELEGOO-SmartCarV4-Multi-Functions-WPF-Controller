using System;
using System.Windows;
//using System.Threading.Tasks;
//using OpenCvSharp;

namespace SmartCarExample
{
    class Program
    {
        // ==================== NEW WPF VERSION ====================
        [STAThread]
        static void Main(string[] args)
        {
            var app = new Application();
            app.Run(new MainWindow());
        }

        // ==================== OLD CONSOLE VERSION (COMMENTED FOR REFERENCE) ====================
        /*
        private static string Robot_IP = "192.168.4.1";
        private static int Robot_Port = 100;
        private static SmartCar.ConnectionManager connectionManager;
        private static SmartCar.VideoStreamViewer videoViewer;
        private static SmartCar.VideoViewerWindow videoWindow;
        private static bool isRunning = true;
        private static bool videoEnabled = false;

        [STAThread]
        static async Task Main(string[] args)
        {
            Console.WriteLine("ELEGOO Smart Car V4 Controller");
            Console.WriteLine("==============================");
            Console.WriteLine("Controls:");
            Console.WriteLine("  W - Forward");
            Console.WriteLine("  S - Backward");
            Console.WriteLine("  A - Left");
            Console.WriteLine("  D - Right");
            Console.WriteLine("  [ - Camera Left");
            Console.WriteLine("  ] - Camera Right");
            Console.WriteLine("  V - Toggle Video Stream");
            Console.WriteLine("  Q - Quit");
            Console.WriteLine("==============================\n");

            // Create connection manager
            connectionManager = new SmartCar.ConnectionManager(Robot_IP, Robot_Port);

            // Subscribe to events
            connectionManager.MessageReceived += OnMessageReceived;
            connectionManager.ConnectionStatusChanged += OnConnectionStatusChanged;

            // Create video stream viewer
            videoViewer = new SmartCar.VideoStreamViewer(Robot_IP);
            videoViewer.FrameReceived += OnFrameReceived;
            videoViewer.FrameDropped += OnFrameDropped;

            // Connect to the smart car
            bool connected = await connectionManager.ConnectAsync();

            if (!connected)
            {
                Console.WriteLine("Failed to connect to Smart Car. Press any key to exit.");
                Console.ReadKey();
                return;
            }

            // Main control loop
            while (isRunning && connectionManager.IsConnected)
            {
                if (Console.KeyAvailable)
                {
                    string key = Console.ReadKey(true).Key.ToString().ToLower();
                    string command = GetCommand(key);

                    if (command != null)
                    {
                        connectionManager.SendCommand(command);
                    }
                }

                await Task.Delay(50); // Small delay to reduce CPU usage
            }

            // Cleanup
            videoViewer?.Stop();
            videoViewer?.Dispose();

            if (videoWindow != null)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    videoWindow?.Close();
                });
            }

            connectionManager.Dispose();
            Console.WriteLine("Application terminated.");
        }

        private static void OnMessageReceived(object sender, string message)
        {
            // Handle responses from the car
            Console.WriteLine($"Car response: {message}");
        }

        private static void OnConnectionStatusChanged(object sender, bool isConnected)
        {
            if (isConnected)
            {
                Console.WriteLine(">>> Connected to Smart Car <<<");
            }
            else
            {
                Console.WriteLine(">>> Disconnected from Smart Car <<<");
            }
        }

        private static void OnFrameReceived(object sender, Mat frame)
        {
            if (videoEnabled && frame != null && videoWindow != null)
            {
                videoWindow.UpdateFrame(frame);
                frame.Dispose(); // Clean up
            }
        }

        private static void OnFrameDropped(object sender, EventArgs e)
        {
            videoWindow?.NotifyFrameDropped();
        }

        static string GetCommand(string key)
        {
            string command = null;

            if (key == "w")
                command = SmartCar.SmartCarCommands.CarControlTime(3, 100, 1000);
            else if (key == "s")
                command = SmartCar.SmartCarCommands.CarControlTime(4, 100, 1000);
            else if (key == "a")
                command = SmartCar.SmartCarCommands.CarControlTime(1, 100, 500);
            else if (key == "d")
                command = SmartCar.SmartCarCommands.CarControlTime(2, 100, 500);
            else if (key == "oem4") // [ key
                command = SmartCar.SmartCarCommands.CameraRotation(3);
            else if (key == "oem6") // ] key
                command = SmartCar.SmartCarCommands.CameraRotation(4);
            else if (key == "v")
            {
                ToggleVideoStream();
            }
            else if (key == "q")
            {
                isRunning = false;
                Console.WriteLine("Shutting down...");
            }

            return command;
        }

        static async void ToggleVideoStream()
        {
            videoEnabled = !videoEnabled;

            if (videoEnabled)
            {
                Console.WriteLine("Starting video stream...");

                // Create WPF window on UI thread
                if (Application.Current == null)
                {
                    // Initialize WPF application if not already done
                    var app = new Application();
                }

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    videoWindow = new SmartCar.VideoViewerWindow();
                    videoWindow.Show();
                });

                await videoViewer.StartAsync();
            }
            else
            {
                Console.WriteLine("Stopping video stream...");
                videoViewer.Stop();

                if (videoWindow != null)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        videoWindow.Close();
                        videoWindow = null;
                    });
                }
            }
        }
        */
    }
}
