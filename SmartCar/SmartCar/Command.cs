using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;


namespace SmartCar
{
    public static class SmartCarCommands
    {
        private static int SequenceNumber = 0;

        public static string CameraRotation(int direction)
        {
            if (direction < 1 || direction > 5)
                throw new ArgumentException("The direction must be between 1 and 5, " + direction + " is out of range.");

            var command = new
            {
                N = 106,
                D1 = direction
            };

            return JsonConvert.SerializeObject(command);
        }

        public static string CarControl(int direction, int speed)
        {
            if (direction < 1 || direction > 4)
                throw new ArgumentException("The direction must be between 1 and 4, " + direction + " is out of range.");
            if (speed < 0 || speed > 255)
                throw new ArgumentException("The speed must be between 0 and 255, " + speed + " is out of range.");

            SequenceNumber++;
            var command = new
            {
                H = SequenceNumber.ToString(),
                N = 3,
                D1 = direction,
                D2 = speed
            };

            return JsonConvert.SerializeObject(command);
        }

        public static string CarControlTime(int direction, int speed, int time)
        {
            if (direction < 1 || direction > 4)
                throw new ArgumentException("The direction must be between 1 and 4, " + direction + " is out of range.");
            if (speed < 0 || speed > 255)
                throw new ArgumentException("The speed must be between 0 and 255, " + speed + " is out of range.");

            SequenceNumber++;
            var command = new
            {
                H = SequenceNumber.ToString(),
                N = 2,
                D1 = direction,
                D2 = speed,
                T = time
            };

            return JsonConvert.SerializeObject(command);
        }

        public static string InfraredStatus(int sensor)
        {
            if (sensor < 0 || sensor > 2)
                throw new ArgumentException("The sensor must be between 0 and 2, " + sensor + " is out of range.");

            var command = new
            {
                N = 22,
                D1 = sensor
            };

            return JsonConvert.SerializeObject(command);
        }

        public static string JoystickClear()
        {
            var command = new
            {
                N = 100
            };

            return JsonConvert.SerializeObject(command);
        }

        public static string JoystickMovement(int direction, int speed = 0)
        {
            if (direction < 0 || direction > 9)
                throw new ArgumentException("The direction must be between 0 and 9, " + direction + " is out of range.");

            var command = new
            {
                N = 102,
                D1 = direction
            };

            return JsonConvert.SerializeObject(command);
        }

        public static string LeftGround()
        {
            var command = new
            {
                N = 23
            };

            return JsonConvert.SerializeObject(command);
        }

        public static string MotorControl(int motor, int speed, int direction)
        {
            if (motor < 0 || motor > 2)
                throw new ArgumentException($"The motor must be between 0 and 2, {motor} is out of range.");

            if (speed < 0 || speed > 255)
                throw new ArgumentException($"The speed must be between 0 and 255, {speed} is out of range.");

            if (direction != 1 && direction != 2)
                throw new ArgumentException($"The direction can only be 1 or 2, {direction} is out of range.");

            SequenceNumber += 1;

            var command = new
            {
                H = SequenceNumber.ToString(),
                N = 1,
                D1 = motor,
                D2 = speed,
                D3 = direction
            };

            return JsonConvert.SerializeObject(command);
        }

        public static string MotorControlSpeed(int leftsped, int rightsped)
        {
            if (leftsped < 0 || leftsped > 255)
                throw new ArgumentException($"The leftsped must be between 0 and 255, {leftsped} is out of range.");

            if (rightsped < 0 || rightsped > 255)
                throw new ArgumentException($"The rightsped must be between 0 and 255, {rightsped} is out of range.");

            SequenceNumber += 1;

            var command = new
            {
                H = SequenceNumber.ToString(),
                N = 4,
                D1 = leftsped,
                D2 = rightsped
            };

            return JsonConvert.SerializeObject(command);
        }

        public static string ProgramingClear()
        {
            SequenceNumber += 1;

            var command = new
            {
                H = SequenceNumber.ToString(),
                N = 110
            };

            return JsonConvert.SerializeObject(command);
        }

        public static string ServoControl(int servo, int angle)
        {
            if (servo != 1 && servo != 2)
                throw new ArgumentException($"The servo can only be 1 or 2, {servo} is out of range.");

            if (angle < 0 || angle > 180)
                throw new ArgumentException($"The angle must be between 0 and 180, {angle} is out of range.");

            SequenceNumber += 1;

            var command = new
            {
                H = SequenceNumber.ToString(),
                N = 5,
                D1 = servo,
                D2 = angle
            };

            return JsonConvert.SerializeObject(command);
        }

        public static string SwitchMode(int mode)
        {
            if (mode < 1 || mode > 3)
                throw new ArgumentException($"The mode must be between 1 and 3, {mode} is out of range.");

            var command = new
            {
                N = 101,
                D1 = mode
            };

            return JsonConvert.SerializeObject(command);
        }

        public static string UltrasonicStatus(int mode)
        {
            if (mode != 1 && mode != 2)
                throw new ArgumentException($"The mode can only be 1 or 2, {mode} is out of range.");

            var command = new
            {
                N = 21,
                D1 = mode
            };

            return JsonConvert.SerializeObject(command);
        }

        public static string CarStop()
        {
            SequenceNumber++;
            var command = new
            {
                H = SequenceNumber.ToString(),
                N = 2,
                D1 = 5, // Direction 5 = stop
                D2 = 0,
                T = 0
            };

            return JsonConvert.SerializeObject(command);
        }
    }
}
