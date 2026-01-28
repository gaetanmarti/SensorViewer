using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Serialization;

namespace immensive;

public abstract class SystemResourceUsage
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum Unit { None, Percent, Temperature }
    
    public class Sensor
    {
        [JsonPropertyName("name")]
        public string Name { get; protected set; } = "";
        [JsonPropertyName("unit")]
        public Unit Unit { get; protected set; } = Unit.None;
        [JsonPropertyName("value")]
        public string ValueAsString { get; protected set; } = "";

        protected string GetUnitSuffix()
        {
            return Unit switch
            {
                Unit.Percent => "%",
                Unit.Temperature => "°C",
                _ => ""
            };
        }
        public override string ToString()
        {
            return Name + ": " + ValueAsString + GetUnitSuffix();
        }
    }

    public class FloatSensor : Sensor
    {
        private float _value;
        public float Value 
        { 
            get { return _value; }
            internal set
            {
                _value = value;
                ValueAsString = _value.ToString("F2", CultureInfo.InvariantCulture);        
            } 
        }
    }

    public class PercentSensor : FloatSensor
    {
        public PercentSensor(string name) {
            Name = name; 
            Unit = Unit.Percent;
        }
    }

    public class TemperatureSensor : FloatSensor
    {
        public TemperatureSensor(string name) {
            Name = name; 
            Unit = Unit.Temperature;
        }
    }

    public class Sensors
    {
        public List<Sensor> List { get; protected set; } = [];
        public override string ToString()
        {
            string result = "";
            foreach (var sensor in List)
                result += sensor.ToString() + "\n";
            return result;
        }
    }

    // List the sensors available for this system
    public Sensors SensorList { get; protected set; } = new Sensors();
    
    // Update the sensor values
    public abstract void Update();

    public static SystemResourceUsage Get()
    {
        /*if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new WindowsSystemResourceUsage();
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return new LinuxSystemResourceUsage();
        else*/ if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return new MacOSSystemResourceUsage();
        else
            throw new NotSupportedException("Unsupported operating system.");
    }

    protected static string Shell (string command)
    {
        using var process = new Process();
        process.StartInfo.FileName = "sh";
        process.StartInfo.Arguments = $"-c \"{command}\"";
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.Start();
        string result = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return result;
    }
}
