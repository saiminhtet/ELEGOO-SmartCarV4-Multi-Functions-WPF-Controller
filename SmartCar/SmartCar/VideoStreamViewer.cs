using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;

namespace SmartCar
{
    public class VideoStreamViewer : IDisposable
    {
        private readonly string _streamUrl;
        private readonly HttpClient _httpClient;
        private CancellationTokenSource _cancellationSource;
        private Task _streamTask;
        private bool _isRunning;
        private long _lastFrameTime = 0;
        private const int TARGET_FRAME_TIME_MS = 50; // ~20 FPS (reduced from 30 to save bandwidth)
        private int _consecutiveFrameDrops = 0;

        public event EventHandler<Mat> FrameReceived;
        public event EventHandler FrameDropped;
        public bool IsRunning => _isRunning;

        public VideoStreamViewer(string ipAddress)
        {
            _streamUrl = $"http://{ipAddress}:81/stream";
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(60) // Increased timeout for WiFi stability
            };

            // Set larger buffer size for smoother streaming
            _httpClient.DefaultRequestHeaders.Connection.Add("keep-alive");
        }

        public async Task StartAsync()
        {
            if (_isRunning)
            {
                Console.WriteLine("Video stream already running.");
                return;
            }

            Console.WriteLine($"Starting video stream from: {_streamUrl}");
            _isRunning = true;
            _cancellationSource = new CancellationTokenSource();
            _streamTask = StreamVideoAsync(_cancellationSource.Token);
            Console.WriteLine($"Video stream task started");
        }

        public void Stop()
        {
            if (!_isRunning) return;

            _isRunning = false;
            _cancellationSource?.Cancel();

            try
            {
                _streamTask?.Wait(2000);
            }
            catch (Exception)
            {
                // Ignore cancellation exceptions
            }

            Console.WriteLine("Video stream stopped.");
        }

        private async Task StreamVideoAsync(CancellationToken token)
        {
            int retryCount = 0;
            const int maxRetries = 999; // Effectively unlimited retries

            while (!token.IsCancellationRequested && retryCount < maxRetries)
            {
                try
                {
                    Console.WriteLine($"[Video] Connecting to stream... (attempt {retryCount + 1})");
                    using var response = await _httpClient.GetAsync(_streamUrl, HttpCompletionOption.ResponseHeadersRead, token);
                    response.EnsureSuccessStatusCode();

                    Console.WriteLine("[Video] Stream connected successfully");
                    retryCount = 0; // Reset retry count on success

                    using var stream = await response.Content.ReadAsStreamAsync(token);
                    var buffer = new byte[1024 * 100]; // 100KB buffer
                    int bytesRead;
                    var frameBuffer = new System.Collections.Generic.List<byte>();

                    // MJPEG stream parsing
                    while (!token.IsCancellationRequested && (bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                    {
                        for (int i = 0; i < bytesRead; i++)
                        {
                            frameBuffer.Add(buffer[i]);

                            // Look for JPEG end marker (FF D9)
                            if (frameBuffer.Count >= 2 &&
                                frameBuffer[frameBuffer.Count - 2] == 0xFF &&
                                frameBuffer[frameBuffer.Count - 1] == 0xD9)
                            {
                                // Find JPEG start marker (FF D8)
                                int startIndex = -1;
                                for (int j = 0; j < frameBuffer.Count - 1; j++)
                                {
                                    if (frameBuffer[j] == 0xFF && frameBuffer[j + 1] == 0xD8)
                                    {
                                        startIndex = j;
                                        break;
                                    }
                                }

                                if (startIndex >= 0)
                                {
                                    // Extract JPEG frame
                                    var frameData = frameBuffer.GetRange(startIndex, frameBuffer.Count - startIndex).ToArray();
                                    ProcessFrame(frameData);

                                    // Clear buffer
                                    frameBuffer.Clear();
                                }
                            }

                            // Prevent buffer from growing too large
                            if (frameBuffer.Count > 1024 * 500) // 500KB max
                            {
                                frameBuffer.Clear();
                            }
                        }
                    }

                    // Stream ended, retry if still running
                    if (_isRunning)
                    {
                        Console.WriteLine("[Video] Stream ended, retrying in 2 seconds...");
                        await Task.Delay(2000, token);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Normal cancellation
                    Console.WriteLine("[Video] Stream cancelled");
                    break;
                }
                catch (Exception ex)
                {
                    retryCount++;
                    Console.WriteLine($"[Video] Stream error: {ex.Message}");

                    if (_isRunning && retryCount < maxRetries)
                    {
                        int delaySeconds = Math.Min(retryCount * 2, 10); // Exponential backoff, max 10s
                        Console.WriteLine($"[Video] Retrying in {delaySeconds} seconds...");
                        await Task.Delay(delaySeconds * 1000, token);
                    }
                }
            }

            Console.WriteLine("[Video] Stream task ended");
        }

        private void ProcessFrame(byte[] frameData)
        {
            try
            {
                // Frame rate limiting to prevent overwhelming the UI
                var currentTime = Environment.TickCount64;
                var timeSinceLastFrame = currentTime - _lastFrameTime;

                if (timeSinceLastFrame < TARGET_FRAME_TIME_MS && _lastFrameTime > 0)
                {
                    // Drop frame to maintain target frame rate
                    FrameDropped?.Invoke(this, EventArgs.Empty);
                    _consecutiveFrameDrops++;

                    // Warn if too many consecutive drops
                    if (_consecutiveFrameDrops > 100)
                    {
                        Console.WriteLine($"[Video] WARNING: {_consecutiveFrameDrops} consecutive frames dropped - network may be overloaded");
                        _consecutiveFrameDrops = 0;
                    }
                    return;
                }

                _lastFrameTime = currentTime;
                _consecutiveFrameDrops = 0; // Reset on successful frame

                // Decode JPEG to OpenCV Mat
                using var mat = Cv2.ImDecode(frameData, ImreadModes.Color);
                if (mat == null)
                {
                    Console.WriteLine("[Video] ERROR: Failed to decode frame - mat is null");
                    return;
                }

                if (mat.Empty())
                {
                    Console.WriteLine("[Video] ERROR: Decoded frame is empty");
                    return;
                }

                // Clone and notify subscribers
                var clonedMat = mat.Clone();
                if (clonedMat == null || clonedMat.Empty())
                {
                    Console.WriteLine("[Video] ERROR: Failed to clone frame");
                    clonedMat?.Dispose();
                    return;
                }

                FrameReceived?.Invoke(this, clonedMat);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Video] Frame decode error: {ex.Message}");
            }
        }

        public void Dispose()
        {
            Stop();
            _httpClient?.Dispose();
            _cancellationSource?.Dispose();
        }
    }

    public class VideoWindow : IDisposable
    {
        private readonly string _windowName;
        private bool _isOpen;

        public VideoWindow(string windowName = "Smart Car Camera")
        {
            _windowName = windowName;
        }

        public void Show(Mat frame)
        {
            if (!_isOpen)
            {
                Cv2.NamedWindow(_windowName, WindowFlags.Normal);
                Cv2.ResizeWindow(_windowName, 640, 480);
                _isOpen = true;
            }

            Cv2.ImShow(_windowName, frame);
            Cv2.WaitKey(1); // Required for window update
        }

        public void Dispose()
        {
            if (_isOpen)
            {
                Cv2.DestroyWindow(_windowName);
                _isOpen = false;
            }
        }
    }
}
