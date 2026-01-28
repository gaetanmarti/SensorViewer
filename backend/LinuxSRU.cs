/* 
using System.Diagnostics;
using System.Management;
using System.Globalization;

namespace Connector
{
    public class LinuxSystemResourceUsage: ISystemResourceUsage
    {
        public float GetCpuUsage()
        {
            using var process = new Process();
            process.StartInfo.FileName = "sh";
            process.StartInfo.Arguments = "-c \"top -bn1 | grep 'Cpu(s)' | awk '{print $2 + $4}'\"";
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.Start();

            string result = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            if (float.TryParse(result, NumberStyles.Float, CultureInfo.InvariantCulture, out float cpuUsage))
                return cpuUsage;
        
            throw new Exception("Failed to parse CPU usage.");
        }

        public float GetRamUsage()
        {
            using var process = new Process();
            process.StartInfo.FileName = "sh";
            process.StartInfo.Arguments = "-c \"free | grep Mem | awk '{print $3/$2 * 100.0}'\"";
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.Start();

            string result = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            if (float.TryParse(result, NumberStyles.Float, CultureInfo.InvariantCulture, out float ramUsage))
                return ramUsage;
            
            throw new Exception("Failed to parse RAM usage.");
        }
    }
}
*/