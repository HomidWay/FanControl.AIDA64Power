using FanControl.Plugins;
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
        private readonly List<AIDA64PluginSensor> _sensors = [];

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
                        try
                        {
                            if (sensor.Id == updatedSensor.Element("id")?.Value)
                                continue;

                            sensor.Update(GetValue(updatedSensor));
                        }
                        catch
                        {
                            continue;
                        }
                    }
                }
            }
            catch
            { 
                throw;
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
                var sensor = new AIDA64PluginSensor(sensorData);

                if (sensor.SensorType is "temp" or "pwr")
                {
                    container.TempSensors.Add(sensor);
                    _sensors.Add(sensor);
                }
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

                buffer[offset -1] = (char)b;
            }

            return new string(buffer, 0, offset -1);
        }

        private float GetValue(XElement sensor)
        {
            string? valueString = sensor.Element("value")?.Value;

            if (float.TryParse(valueString, out float result))
            {
                return result;
            }

            throw new AIDACaptureFailedException("xml element doesn't contain element \"value\": " + sensor.ToString());
        }
    }

    public class AIDA64PluginSensor : IPluginSensor
    {
        private readonly string _sensorType;
        private readonly string _sensorID;
        private readonly string _sensorName;
        private float _sensorValue;

        public AIDA64PluginSensor(XElement sensorData)
        {
            _sensorType = sensorData.Name.LocalName;
            _sensorID = sensorData.Element("id")?.Value ?? throw new AIDACaptureFailedException("Empty sensor ID");
            _sensorName = _sensorType switch
            {
                "temp" => sensorData.Element("label")?.Value ?? "Unknown Temp",
                "pwr" => "[POWER SENSOR] " + (sensorData.Element("label")?.Value ?? "Unknown Power"),
                _ => throw new AIDACaptureFailedException("Unsupported sensor type")
            };

            if (sensorData.Element("value")?.Value is string valueStr &&
                float.TryParse(valueStr, out float result))
            {
                _sensorValue = _sensorType switch
                {
                    "temp" => result,
                    "pwr" => result / 10,
                    _ => throw new AIDACaptureFailedException("Unsupported sensor type")
                };
            }
            else
            {
                throw new AIDACaptureFailedException("Failed to read sensor value");
            }
        }

        public string SensorType
        {
            get { return _sensorType; }
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
            get { return _sensorValue;}
        }

        public void Update() { }

        public void Update(float newValue)
        {
            _sensorValue = _sensorType switch
            {
                "temp" => newValue,
                "pwr" => newValue / 10,
                _ => 0,
            };
        }
    }

    public class AIDACaptureFailedException : Exception
    {
        public AIDACaptureFailedException() : base("Failed to capture AIDA64 Shared memory") { }

        public AIDACaptureFailedException(string message) : base(message) { }

        public AIDACaptureFailedException(string message, Exception? innerException) : base(message, innerException) { }
    }
}