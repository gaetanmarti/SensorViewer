/*
using System.Diagnostics;
using System.Management;

// Alternative solution for Windows:
// https://www.experts-exchange.com/questions/27096048/C-get-total-CPU-usage-WITHOUT-using-WMI-or-pPerformanceCounter.html
// Temperatures: 
// https://schwabencode.com/blog/2019/10/09/Read-System-Properties-with-NET

namespace Connector
{
    public class WindowsSystemResourceUsage: ISystemResourceUsage
    {
        public float GetCpuUsage()
        {
            var cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            cpuCounter.NextValue(); // Initial call to initialize
            System.Threading.Thread.Sleep(500); // Wait for accurate value
            return cpuCounter.NextValue();
        }

        public float GetRamUsage()
        {
            var ramCounter = new PerformanceCounter("Memory", "Available MBytes");
            var availableRam = ramCounter.NextValue();
            var totalRam = GetTotalRam();
            return (totalRam - availableRam) / totalRam * 100.0f;
        }

        private float GetTotalRam()
        {
            var searcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
            foreach (var obj in searcher.Get())
            {
                return Convert.ToSingle(obj["TotalPhysicalMemory"]) / (1024 * 1024); // Convert to MB
            }
            throw new Exception("Failed to retrieve total RAM.");
        }
    }
}
*/