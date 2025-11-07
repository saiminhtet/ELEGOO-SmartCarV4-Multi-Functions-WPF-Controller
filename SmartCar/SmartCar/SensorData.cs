using System;

namespace SmartCar
{
    public class SensorData
    {
        // Ultrasonic sensor data
        public int UltrasonicDistance { get; set; } // Distance in cm
        public DateTime UltrasonicTimestamp { get; set; }

        // Line tracking sensors (IR sensors)
        public bool LeftLineDetected { get; set; }
        public bool MiddleLineDetected { get; set; }
        public bool RightLineDetected { get; set; }
        public DateTime LineTrackingTimestamp { get; set; }

        // General status
        public bool IsSensorDataAvailable =>
            (DateTime.Now - UltrasonicTimestamp).TotalSeconds < 5 ||
            (DateTime.Now - LineTrackingTimestamp).TotalSeconds < 5;

        public SensorData()
        {
            UltrasonicDistance = -1;
            UltrasonicTimestamp = DateTime.MinValue;
            LineTrackingTimestamp = DateTime.MinValue;
        }
    }
}
