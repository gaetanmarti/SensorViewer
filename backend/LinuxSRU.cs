using System.Diagnostics;
using System.Globalization;

namespace immensive;

// Linux system resource usage implementation
// This class retrieves CPU, RAM, and temperature sensor information on Linux systems
// Temperature sensors are read from /sys/class/thermal/thermal_zone* directories

public class LinuxSystemResourceUsage : SystemResourceUsage
{
    protected class TemperatureSensorWithPath(string name, string thermalPath) : TemperatureSensor(name)
    {
        public string ThermalPath { get; set; } = thermalPath;
    }

    protected PercentSensor cpuUsageSensor = new("CPU Usage");
    protected PercentSensor ramUsageSensor = new("RAM Usage");

    protected List<TemperatureSensorWithPath> temperatureSensors = [];

    // Store previous CPU stats for delta calculation
    private long prevTotal = 0;
    private long prevIdle = 0;

    // Thermal zone base path
    private const string thermalBasePath = "/sys/class/thermal";

    // Discover and initialize all available temperature sensors
    private void DiscoverTemperatureSensors()
    {
        if (!Directory.Exists(thermalBasePath))
        {
            Console.WriteLine("Thermal zone directory not found. Temperature sensors will not be available.");
            return;
        }

        var thermalZones = Directory.GetDirectories(thermalBasePath, "thermal_zone*");
        foreach (var zonePath in thermalZones)
        {
            string typePath = Path.Combine(zonePath, "type");
            string tempPath = Path.Combine(zonePath, "temp");

            // Check if both type and temp files exist
            if (File.Exists(typePath) && File.Exists(tempPath))
            {
                try
                {
                    string sensorName = File.ReadAllText(typePath).Trim();
                    string tempStr = File.ReadAllText(tempPath).Trim();

                    // Temperature is in millidegrees Celsius, convert to Celsius
                    if (float.TryParse(tempStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float tempMilliC))
                    {
                        float tempC = tempMilliC / 1000.0f;
                        // Only add sensors with valid temperature readings
                        if (tempC > -50.0f && tempC < 150.0f)
                        {
                            var sensor = new TemperatureSensorWithPath(sensorName, tempPath) { Value = tempC };
                            temperatureSensors.Add(sensor);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to read thermal zone {zonePath}: {ex.Message}");
                }
            }
        }
    }

    public LinuxSystemResourceUsage()
    {
        SensorList.List.Add(cpuUsageSensor);
        SensorList.List.Add(ramUsageSensor);

        // Discover and add temperature sensors
        DiscoverTemperatureSensors();
        foreach (var sensor in temperatureSensors)
        {
            SensorList.List.Add(sensor);
        }

        // Initialize CPU stats for first measurement
        Update();
    }

    // Read CPU stats from /proc/stat
    // Returns tuple of (total, idle) CPU time
    private (long total, long idle) ReadCpuStats()
    {
        try
        {
            string[] lines = File.ReadAllLines("/proc/stat");
            foreach (var line in lines)
            {
                if (line.StartsWith("cpu "))
                {
                    // Format: cpu user nice system idle iowait irq softirq steal guest guest_nice
                    string[] parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 5)
                    {
                        long user = long.Parse(parts[1]);
                        long nice = long.Parse(parts[2]);
                        long system = long.Parse(parts[3]);
                        long idle = long.Parse(parts[4]);
                        long iowait = parts.Length > 5 ? long.Parse(parts[5]) : 0;
                        long irq = parts.Length > 6 ? long.Parse(parts[6]) : 0;
                        long softirq = parts.Length > 7 ? long.Parse(parts[7]) : 0;
                        long steal = parts.Length > 8 ? long.Parse(parts[8]) : 0;

                        long totalIdle = idle + iowait;
                        long totalActive = user + nice + system + irq + softirq + steal;
                        long total = totalIdle + totalActive;

                        return (total, totalIdle);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to read CPU stats: {ex.Message}");
        }

        return (0, 0);
    }

    // Read memory information from /proc/meminfo
    private void UpdateRamUsage()
    {
        try
        {
            string[] lines = File.ReadAllLines("/proc/meminfo");
            long memTotal = 0;
            long memAvailable = 0;

            foreach (var line in lines)
            {
                if (line.StartsWith("MemTotal:"))
                {
                    // Format: MemTotal:       16384000 kB
                    string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                        memTotal = long.Parse(parts[1]);
                }
                else if (line.StartsWith("MemAvailable:"))
                {
                    // Format: MemAvailable:   8192000 kB
                    string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                        memAvailable = long.Parse(parts[1]);
                }

                // Break early if we have both values
                if (memTotal > 0 && memAvailable > 0)
                    break;
            }

            if (memTotal > 0)
            {
                float usedPercent = ((float)(memTotal - memAvailable) / (float)memTotal) * 100.0f;
                ramUsageSensor.Value = Math.Min(Math.Max(0, usedPercent), 100.0f);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to read memory stats: {ex.Message}");
        }
    }

    // Update temperature sensors by reading from thermal zone files
    private void UpdateTemperatureSensors()
    {
        foreach (var sensor in temperatureSensors)
        {
            try
            {
                string tempStr = File.ReadAllText(sensor.ThermalPath).Trim();
                if (float.TryParse(tempStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float tempMilliC))
                {
                    float tempC = tempMilliC / 1000.0f;
                    // Validate temperature range
                    if (tempC > -50.0f && tempC < 150.0f)
                    {
                        sensor.Value = tempC;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to read temperature sensor {sensor.Name}: {ex.Message}");
            }
        }
    }

    public override void Update()
    {
        // Update CPU usage
        var (currentTotal, currentIdle) = ReadCpuStats();
        
        if (prevTotal > 0)
        {
            long totalDelta = currentTotal - prevTotal;
            long idleDelta = currentIdle - prevIdle;

            if (totalDelta > 0)
            {
                float cpuUsage = ((float)(totalDelta - idleDelta) / (float)totalDelta) * 100.0f;
                cpuUsageSensor.Value = Math.Min(Math.Max(0, cpuUsage), 100.0f);
            }
        }

        prevTotal = currentTotal;
        prevIdle = currentIdle;

        // Update RAM usage
        UpdateRamUsage();

        // Update temperature sensors
        UpdateTemperatureSensors();
    }
}