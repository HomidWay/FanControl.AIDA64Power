using FanControl.Plugins;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Xml.Linq;

namespace FanControl.AIDA64Power
{
    public class AIDA64PowerPlugin : IPlugin2
    {
        private const string SharedMemoryName = "AIDA64_SensorValues";
        private const int MaxBufferSize = 65536;

        public string Name => "AIDA64 Power";

        private readonly List<AIDA64PluginSensor> _sensors = new();

        public void Initialize() { }

        public void Close() => _sensors.Clear();

        public void Load(IPluginSensorsContainer container)
        {
            var sensors = LoadAIDA64Sensors();
            if (sensors == null) return;

            foreach (var sensor in sensors)
            {
                AddSensor(container, sensor);
            }
        }

        public void Update()
        {
            var updatedSensors = LoadAIDA64Sensors();
            if (updatedSensors == null) return;

            foreach (var sensor in _sensors)
            {
                foreach (var update in updatedSensors)
                {
                    if (sensor.Id == GetId(update))
                    {
                        sensor.Update(update);
                    }
                }
            }
        }

        private static string GetId(XElement sensor)
        {
            return sensor.Element("id")?.Value ?? string.Empty;
        }

        private void AddSensor(IPluginSensorsContainer container, XElement sensorData)
        {
            var sensor = new AIDA64PluginSensor(sensorData);

            if (sensor.SensorType is "temp" or "pwr")
            {
                container.TempSensors.Add(sensor);
                _sensors.Add(sensor);
            }
        }

        public IEnumerable<XElement>? LoadAIDA64Sensors()
        {
            try
            {
                using var mmf = MemoryMappedFile.OpenExisting(SharedMemoryName);
                using var accessor = mmf.CreateViewAccessor();

                var buffer = ReadSharedMemory(accessor);
                if (string.IsNullOrWhiteSpace(buffer)) return null;

                var wrappedXml = "<root>" + buffer + "</root>";
                var document = XDocument.Parse(wrappedXml);
                return document.Descendants();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AIDA64Plugin] Error reading shared memory: {ex.Message}");
                return null;
            }
        }

        private string ReadSharedMemory(MemoryMappedViewAccessor accessor)
        {
            int offset = 0;
            var sb = new StringBuilder();

            while (offset < MaxBufferSize)
            {
                byte b = accessor.ReadByte(offset++);
                if (b == 0) break;

                sb.Append((char)b);
            }

            return sb.ToString();
        }
    }

    public class AIDA64PluginSensor : IPluginSensor
    {
        private XElement _sensorData;
        private float? _value;

        public AIDA64PluginSensor(XElement sensorData)
        {
            _sensorData = sensorData;
        }

        public string SensorType => _sensorData.Name.LocalName;

        public string Id => _sensorData.Element("id")?.Value ?? string.Empty;

        public string Name
        {
            get
            {
                return SensorType switch
                {
                    "temp" => _sensorData.Element("label")?.Value ?? "Unknown Temp",
                    "pwr" => "[POWER SENSOR] " + (_sensorData.Element("label")?.Value ?? "Unknown Power"),
                    _ => "Unsupported Sensor"
                };
            }
        }

        public float? Value
        {
            get
            {
                return SensorType switch
                {
                    "temp" => _value,
                    "pwr" => _value / 10,
                    _ => 0
                };
            }
        }

        public void Update()
        {
            if (_sensorData.Element("value")?.Value is string valueStr &&
                float.TryParse(valueStr, out float result))
            {
                _value = result;
            }
            else
            {
                _value = null;
            }
        }

        public void Update(XElement data)
        {
            _sensorData = data;
            Update();
        }
    }
}