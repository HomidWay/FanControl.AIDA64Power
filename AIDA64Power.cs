using FanControl.Plugins;
using LibreHardwareMonitor.Hardware;
using System.IO.MemoryMappedFiles;
using System.Runtime.Versioning;
using System.Text;
using System.Xml.Linq;

namespace FanControl.AIDA64Power
{
    [SupportedOSPlatform("windows")]
    public class AIDA64PowerPlugin : IPlugin2
    {
        private const string SharedMemoryName = "AIDA64_SensorValues";
        private const int MaxBufferSize = 65536;
        private readonly List<IUpdatablePluginSensor> _sensors = [];

        public string Name => "AIDA64 Power";

        public void Initialize() { }

        public void Close() => _sensors.Clear();

        public void Load(IPluginSensorsContainer container)
        {
            try
            {
                var sensors = LoadAIDA64Sensors();
                if (sensors == null) return;

                foreach (var sensor in sensors)
                {
                    AddSensor(container, sensor);
                }
            }
            catch
            {
                throw;
            }
        }

        public void Update()
        {
            try
            {
                var updatedSensors = LoadAIDA64Sensors();
                if (updatedSensors == null) return;

                foreach (var sensor in _sensors)
                {
                    foreach (var updatedSensor in updatedSensors)
                    {
                        if (sensor.Id != updatedSensor.Element("id")?.Value)
                            continue;

                        sensor.SetValue(GetValue(updatedSensor));
                    }
                }
            }
            catch
            {
                return;
            }
        }
        public IEnumerable<XElement> LoadAIDA64Sensors()
        {
            try
            {
                using var mmf = MemoryMappedFile.OpenExisting(SharedMemoryName);
                using var accessor = mmf.CreateViewAccessor();

                var buffer = ReadSharedMemory(accessor);
                if (string.IsNullOrWhiteSpace(buffer)) throw new Exception("Buffer is NULL of Whitespace");

                var wrappedXml = "<root>" + buffer + "</root>";
                var document = XDocument.Parse(wrappedXml) ?? throw new AIDACaptureFailedException("Failed to read XML from AIDA64 Shared Memory");
                return document.Descendants();
            }
            catch (Exception ex)
            {
                throw new AIDACaptureFailedException("Failed to read AIDA64 Shared memory pool with error", ex);
            }
        }

        private void AddSensor(IPluginSensorsContainer container, XElement sensorData)
        {
            try
            {

                IUpdatablePluginSensor sensor;

                string sensorType = sensorData.Name.LocalName;

                switch (sensorType) {
                    case "temp":
                        sensor = new AIDA64TemperatureSensor(sensorData);
                        break;
                    case "pwr":
                        sensor = new AIDA64PowerSensor(sensorData);
                        break;
                    default:
                        return;
                }

                container.TempSensors.Add(sensor);
                _sensors.Add(sensor);
            }
            catch
            {
                return;
            }

        }

        private string ReadSharedMemory(MemoryMappedViewAccessor accessor)
        {
            var buffer = new char[MaxBufferSize];
            int offset = 0;

            while (offset < MaxBufferSize)
            {
                byte b = accessor.ReadByte(offset++);
                if (b == 0) break;

                buffer[offset - 1] = (char)b;
            }

            return new string(buffer, 0, offset - 1);
        }

        private float? GetValue(XElement sensor)
        {
            string? valueString = sensor.Element("value")?.Value;

            if (float.TryParse(valueString, out float result))
            {
                return result;
            }

            return null;
        }
    }

    public class AIDA64TemperatureSensor : IUpdatablePluginSensor
    {
        private readonly string _sensorID;
        private readonly string _sensorName;
        private float _sensorValue;

        public AIDA64TemperatureSensor(XElement sensorData)
        {
            _sensorID = sensorData.Element("id")?.Value ?? throw new AIDACaptureFailedException("Empty sensor ID");
            _sensorName = sensorData.Element("label")?.Value ?? "Unknown Temp";

            if (sensorData.Element("value")?.Value is string valueStr &&
                float.TryParse(valueStr, out float result))
            {
                _sensorValue = result;
            }
            else
            {
                _sensorValue = 0;
            }
        }

        public string Id
        {
            get { return _sensorID; }
        }

        public string Name
        {
            get { return _sensorName; }
        }

        public float? Value
        {
            get { return _sensorValue; }
        }

        public void Update() { }

        public void SetValue(float? newValue)
        {
            _sensorValue = newValue ?? _sensorValue;
        }
    }

    public class AIDA64PowerSensor : IUpdatablePluginSensor
    {
        private readonly string _sensorID;
        private readonly string _sensorName;
        private float _sensorValue;

        public AIDA64PowerSensor(XElement sensorData)
        {
            _sensorID = sensorData.Element("id")?.Value ?? throw new AIDACaptureFailedException("Empty sensor ID");
            _sensorName = "[POWER SENSOR] " + (sensorData.Element("label")?.Value ?? "Unknown Power");

            if (sensorData.Element("value")?.Value is string valueStr &&
                float.TryParse(valueStr, out float result))
            {
                _sensorValue = result / 10;
            }
            else
            {
                _sensorValue = 0;
            }
        }

        public string Id
        {
            get { return _sensorID; }
        }

        public string Name
        {
            get { return _sensorName; }
        }

        public float? Value
        {
            get { return _sensorValue; }
        }

        public void Update() { }

        public void SetValue(float? newValue)
        {
            float value = newValue ?? _sensorValue;
            _sensorValue = value / 10;
        }
    }

    public interface IUpdatablePluginSensor: IPluginSensor
    {
        void SetValue(float? newValue);
    }

    public class AIDACaptureFailedException : Exception
    {
        public AIDACaptureFailedException() : base("Failed to capture AIDA64 Shared memory") { }

        public AIDACaptureFailedException(string message) : base(message) { }

        public AIDACaptureFailedException(string message, Exception? innerException) : base(message, innerException) { }
    }
}