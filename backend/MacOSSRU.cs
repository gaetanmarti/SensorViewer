using System.Diagnostics;
using System.Management;
using System.Globalization;

namespace immensive;

// It is recommended to use the stat utility: '>brew install stats'
// To get more sensor information.

public class MacOSSystemResourceUsage: SystemResourceUsage
{
    protected PercentSensor cpuUsageSensor = new("CPU Usage");
    protected PercentSensor ramUsageSensor = new("RAM Usage");

    protected List<TemperatureSensor> temperatureSensors = new ();

    protected const string smcPath = "/Applications/Stats.app/Contents/Resources/smc";

    // See https://logi.wiki/index.php/SMC_Sensor_Codes
    private Dictionary<string, string> smcTemoSensors = new ()
    {
        //{ "TC0F", "CPU Die PECI" },
        //{ "TC0P", "CPU Proximity" },
        { "TC1C", "CPU Core 1" },
        { "TC2C", "CPU Core 2" },
        { "TC3C", "CPU Core 3" },
        { "TC4C", "CPU Core 4" },
        { "TC5C", "CPU Core 5" },
        { "TC6C", "CPU Core 6" },
        { "TC7C", "CPU Core 7" },
        { "TC8C", "CPU Core 8" },
        { "TCGC", "Intel GPU" },
        //{ "TCSA", "CPU System Agent Core" },
        //{ "TCXC", "CPU PECI" },
        { "TH0A", "SSD 1" },
        { "TH0B", "SSD 2" },
        { "TH0C", "SSD 3" },
        { "TM0P", "Memory Proximity" },
        //{ "TPCD", "Platform Controller Hub Die" },
        //{ "TS0V", "Skin" },
        { "TW0P", "Airport" },
        { "TaLC", "Airflow Left" },
        { "TaRC", "Airflow Right" }
    };

    private const float smcInvalidTemperature = 129.0f;

    private Dictionary<string,string> GetSMCTemperatures ()
    {
        Dictionary<string,string> temperatures = [];
        
        string result = SystemResourceUsage.Shell(smcPath + " list -t");
        string[] lines = result.Split('\n');
        foreach (var line in lines)
        {
            string[] parts = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                continue;
            string key = parts[0].TrimStart('[').TrimEnd(']');
            string value = parts[1];
            temperatures.Add(key, value);
        }
        
        return temperatures;
    }

    public MacOSSystemResourceUsage()
    {
        SensorList.List.Add(cpuUsageSensor);
        SensorList.List.Add(ramUsageSensor);

        // Check if '/Applications/Stats.app/Contents/Resources/smc'  is installed
        string smcPath = "/Applications/Stats.app/Contents/Resources/smc";
        if (File.Exists(smcPath))
        {
            Dictionary<string,string> temperatures = GetSMCTemperatures();
            foreach (var sensor in smcTemoSensors)
            {
                if (temperatures.ContainsKey(sensor.Key))
                {
                    float temperature = smcInvalidTemperature;
                    if (float.TryParse(temperatures[sensor.Key], NumberStyles.Float, CultureInfo.InvariantCulture, out temperature) &&
                        temperature != smcInvalidTemperature)
                        temperatureSensors.Add(new TemperatureSensor(sensor.Value) { Value = temperature });
                }
            }
            foreach (var sensor in temperatureSensors)
                SensorList.List.Add(sensor);
        }
        else
        {
            Console.WriteLine("SMC is not installed. Please install it using 'brew install stats'");
        }
    }

    private static string GetCpuRamUsage(uint seconds = 1)
    {
        return SystemResourceUsage.Shell($"top -R -F -n 0 -l 2 -s {seconds} | grep -E '^CPU|^PhysMem' | tail -2");
    }

    public override void Update()
    {
        string result = GetCpuRamUsage();
        string[] parts = result.Split('\n');
        char[] separators = [' ', ',', '(', ')', '\n'];

        foreach (var line in parts)
        {
            if (line.StartsWith("CPU"))
            {
                // Parse CPU usage
                // E.g. "CPU usage: 9.21% user, 4.57% sys, 86.21% idle"
                string[] cpuParts = line.Split(separators, StringSplitOptions.RemoveEmptyEntries);
                var idle = cpuParts[6].TrimEnd('%');
                if (float.TryParse(idle, NumberStyles.Float, CultureInfo.InvariantCulture, out float cpuUsage))
                    cpuUsageSensor.Value = Math.Min (Math.Max(0, 100.0f - cpuUsage), 100.0f);
            }
            else if (line.StartsWith("PhysMem"))
            {
                // Parse RAM usage
                // E.g. "PhysMem: 16G used (4139M wired, 1332M compressor), 325M unused."
                string[] ramParts = line.Split(separators, StringSplitOptions.RemoveEmptyEntries);
                var usedMemory = ConvertToBytes(ramParts[1]);
                var wiredMemory = ConvertToBytes(ramParts[3]);
                var compressedMemory = ConvertToBytes(ramParts[5]);
                var unusedMemory = ConvertToBytes(ramParts[7]);

                long totalMemory = usedMemory + unusedMemory;
                long availableMemory = totalMemory - wiredMemory - compressedMemory + unusedMemory;

                if (totalMemory <= 0)
                    throw new Exception("Failed to parse memory usage on macOS.");
                ramUsageSensor.Value = ((float)availableMemory / (float)totalMemory) * 100.0f;
            }
        }

        // Update temperature sensors
        if (temperatureSensors.Count > 0)
        {
            Dictionary<string,string> temperatures = GetSMCTemperatures();
            foreach (var sensor in temperatureSensors)
            {
                if (temperatures.ContainsKey(sensor.Name))
                {
                    float temperature = smcInvalidTemperature;
                    if (float.TryParse(temperatures[sensor.Name], NumberStyles.Float, CultureInfo.InvariantCulture, out temperature))
                        sensor.Value = temperature;
                }
            }
        }

    }
    
    private static long ConvertToBytes(string memorySize)
    {
        if (string.IsNullOrWhiteSpace(memorySize))
        {
            throw new ArgumentException("Memory size string cannot be null or empty.");
        }

        // Extract numeric part and unit
        string unit = memorySize[^1].ToString().ToUpper(); // Last character as unit
        string numericPart = memorySize[..^1]; // All except the last character

        if (!float.TryParse(numericPart, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
        {
            throw new FormatException("Invalid memory size format.");
        }

        return unit switch
        {
            "G" => (long)(value * 1024 * 1024 * 1024), // Convert gigabytes to bytes
            "M" => (long)(value * 1024 * 1024),       // Convert megabytes to bytes
            "K" => (long)(value * 1024),              // Convert kilobytes to bytes
            "B" => (long)value,                       // Already in bytes
            _ => throw new NotSupportedException($"Unsupported memory unit: {unit}")
        };
    }
}