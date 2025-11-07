using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace SmartCar
{
    public partial class VideoViewerWindow : System.Windows.Window
    {
        private WriteableBitmap _writeableBitmap;
        private readonly DispatcherTimer _fpsTimer;
        private int _frameCount;
        private readonly Stopwatch _fpsStopwatch;
        private int _droppedFrames;

        public VideoViewerWindow()
        {
            InitializeComponent();

            // Setup FPS timer
            _fpsStopwatch = Stopwatch.StartNew();
            _fpsTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _fpsTimer.Tick += UpdateFpsDisplay;
            _fpsTimer.Start();

            Closed += OnWindowClosed;
        }

        public void UpdateFrame(Mat frame)
        {
            // Validate frame before accessing any properties
            if (frame == null)
            {
                Console.WriteLine("ERROR: Frame is null");
                return;
            }

            try
            {
                // Check if frame is valid before accessing Width/Height
                if (frame.IsDisposed)
                {
                    Console.WriteLine("ERROR: Frame is disposed");
                    return;
                }

                if (frame.Empty())
                {
                    Console.WriteLine("ERROR: Frame is empty");
                    frame.Dispose();
                    return;
                }

                // Get frame dimensions before async operation
                int frameWidth = frame.Width;
                int frameHeight = frame.Height;
                var frameType = frame.Type();

                // Convert to byte array for safe transfer to UI thread
                byte[] frameData;
                if (frameType == MatType.CV_8UC3)
                {
                    frameData = new byte[frame.Total() * frame.ElemSize()];
                    System.Runtime.InteropServices.Marshal.Copy(frame.Data, frameData, 0, frameData.Length);
                }
                else
                {
                    // Convert to BGR first
                    using var bgrMat = frame.CvtColor(ColorConversionCodes.GRAY2BGR);
                    frameData = new byte[bgrMat.Total() * bgrMat.ElemSize()];
                    System.Runtime.InteropServices.Marshal.Copy(bgrMat.Data, frameData, 0, frameData.Length);
                }

                // Dispose original frame now that we have the data
                frame.Dispose();

                // Use Dispatcher to update UI thread
                Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        // Initialize WriteableBitmap on first frame
                        if (_writeableBitmap == null ||
                            _writeableBitmap.PixelWidth != frameWidth ||
                            _writeableBitmap.PixelHeight != frameHeight)
                        {
                            _writeableBitmap = new WriteableBitmap(
                                frameWidth,
                                frameHeight,
                                96, 96,
                                PixelFormats.Bgr24,
                                null);

                            VideoImage.Source = _writeableBitmap;
                            ResolutionText.Text = $"{frameWidth}x{frameHeight}";
                        }

                        // Fast bitmap update
                        _writeableBitmap.Lock();
                        try
                        {
                            unsafe
                            {
                                // Direct memory copy for best performance
                                var backBuffer = _writeableBitmap.BackBuffer;
                                var stride = _writeableBitmap.BackBufferStride;

                                fixed (byte* srcPtr = frameData)
                                {
                                    byte* dstPtr = (byte*)backBuffer;

                                    for (int y = 0; y < frameHeight; y++)
                                    {
                                        Buffer.MemoryCopy(
                                            srcPtr + y * frameWidth * 3,
                                            dstPtr + y * stride,
                                            stride,
                                            frameWidth * 3);
                                    }
                                }
                            }

                            _writeableBitmap.AddDirtyRect(new Int32Rect(
                                0, 0,
                                _writeableBitmap.PixelWidth,
                                _writeableBitmap.PixelHeight));
                        }
                        finally
                        {
                            _writeableBitmap.Unlock();
                        }

                        _frameCount++;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Frame rendering error: {ex.Message}");
                        Console.WriteLine($"Stack trace: {ex.StackTrace}");
                    }
                }, DispatcherPriority.Render);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Frame update error: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                frame?.Dispose();
            }
        }

        private void UpdateFpsDisplay(object sender, EventArgs e)
        {
            var elapsed = _fpsStopwatch.Elapsed.TotalSeconds;
            var fps = _frameCount / elapsed;

            FpsText.Text = $"{fps:F1}";

            // Show dropped frames if any
            if (_droppedFrames > 0)
            {
                FpsText.Text += $" (-{_droppedFrames})";
            }

            // Reset counters
            _frameCount = 0;
            _droppedFrames = 0;
            _fpsStopwatch.Restart();

            // Update status indicator
            if (fps > 5)
            {
                StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(78, 201, 176)); // Green
                StatusText.Text = "Streaming";
                StatusText.Foreground = new SolidColorBrush(Color.FromRgb(78, 201, 176));
            }
            else if (fps > 1)
            {
                StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(206, 145, 120)); // Orange
                StatusText.Text = "Slow";
                StatusText.Foreground = new SolidColorBrush(Color.FromRgb(206, 145, 120));
            }
            else
            {
                StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(244, 71, 71)); // Red
                StatusText.Text = "Stalled";
                StatusText.Foreground = new SolidColorBrush(Color.FromRgb(244, 71, 71));
            }
        }

        public void NotifyFrameDropped()
        {
            _droppedFrames++;
        }

        public void UpdateMode(int mode)
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
                    ? new SolidColorBrush(Color.FromRgb(255, 193, 7)) // Orange for autonomous
                    : new SolidColorBrush(Color.FromRgb(78, 201, 176)); // Green for manual
                CurrentModeText.Foreground = color;
            });
        }

        public void UpdateWiFiSignal(int signalStrength)
        {
            Dispatcher.InvokeAsync(() =>
            {
                string signalText;
                SolidColorBrush color;

                if (signalStrength >= 80)
                {
                    signalText = "Excellent";
                    color = new SolidColorBrush(Color.FromRgb(78, 201, 176)); // Green
                    WiFiIcon.Text = "📶";
                }
                else if (signalStrength >= 60)
                {
                    signalText = "Good";
                    color = new SolidColorBrush(Color.FromRgb(78, 201, 176)); // Green
                    WiFiIcon.Text = "📶";
                }
                else if (signalStrength >= 40)
                {
                    signalText = "Fair";
                    color = new SolidColorBrush(Color.FromRgb(255, 193, 7)); // Yellow
                    WiFiIcon.Text = "📶";
                }
                else if (signalStrength >= 20)
                {
                    signalText = "Weak";
                    color = new SolidColorBrush(Color.FromRgb(206, 145, 120)); // Orange
                    WiFiIcon.Text = "📶";
                }
                else
                {
                    signalText = "Poor";
                    color = new SolidColorBrush(Color.FromRgb(244, 71, 71)); // Red
                    WiFiIcon.Text = "📶";
                }

                WiFiSignalText.Text = signalText;
                WiFiSignalText.Foreground = color;
                WiFiIcon.Foreground = color;
            });
        }

        public void ShowDetectionAlert(string title, string message, int durationMs = 2000)
        {
            Dispatcher.InvokeAsync(async () =>
            {
                DetectionAlertTitle.Text = title;
                DetectionAlertMessage.Text = message;
                DetectionAlertPanel.Visibility = Visibility.Visible;

                // Auto-hide after duration
                await Task.Delay(durationMs);
                DetectionAlertPanel.Visibility = Visibility.Collapsed;
            });
        }

        public void UpdateCarStatus(string status)
        {
            Dispatcher.InvokeAsync(() =>
            {
                CarStatusText.Text = status;
            });
        }

        private void OnWindowClosed(object sender, EventArgs e)
        {
            _fpsTimer?.Stop();
        }
    }
}
