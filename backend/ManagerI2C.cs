using System.Device.I2c;

namespace immensive;

/// Manager for I2C bus operations
public class ManagerI2C
{
    public ManagerI2C(int busId = 1)
    {
        _busId = busId;

        RegisterDevice(new TMF882X());
    }

    private readonly int _busId;

    public Dictionary<int, List<II2CDevice>> Devices {get;private set;} = [];

    public void RegisterDevice(II2CDevice device)
    {
        if (!Devices.TryGetValue(device.Address, out List<II2CDevice>? value))
            Devices[device.Address] = [];
        else if (value.Contains(device))
        {
            CustomLogger.Log(this, CustomLogger.LogLevel.Warning, $"Device {device.Name} at address 0x{device.Address:X2} is already registered.");
            return;
        }
        Devices[device.Address].Add(device);
    }

    // Try to auto-detect all I2C devices on the bus
    // Returns the List of auto-detected devices
    public List<II2CDevice> AutoDetectDevices(CancellationToken token = default)
    {
        var devices = new List<II2CDevice>();
        byte[] buffer = new byte[1];

        for (int addr = 0x03; addr <= 0x77; addr++)
        {
            token.ThrowIfCancellationRequested();
            if (I2C.TryDetectDevice(_busId, addr, buffer) == I2CDetectionState.Present) {
                bool found = false;
                if (Devices.TryGetValue(addr, out List<II2CDevice>? value))
                    foreach (var device in value) {
                        if (device.TryDetect(_busId, token)) {
                            devices.Add(device);
                            found = true;
                            break;
                        }
                    }
                if (!found) {
                    CustomLogger.Log(this, CustomLogger.LogLevel.Info, $"Unknown I2C device detected at address 0x{addr:X2}");
                    devices.Add(new UnknownII2CDevice(addr));
                }
            }
        }

        return devices;
    }

    /// <summary>
    /// Try to resolve a distance sensor by address, detecting it on the bus if needed.
    /// </summary>
    public bool TryGetDistanceSensor(int address, out II2CDistanceSensor? sensor, CancellationToken token = default)
    {
        sensor = null;

        if (!Devices.TryGetValue(address, out List<II2CDevice>? value))
            return false;

        foreach (var device in value)
        {
            token.ThrowIfCancellationRequested();
            II2CDistanceSensor? distanceSensor = device as II2CDistanceSensor;
            if (distanceSensor == null)
                continue;
            switch (distanceSensor.Status)
            {
                case II2CDevice.DeviceStatus.Unknown:
                case II2CDevice.DeviceStatus.Detected:
                    distanceSensor.Initialize([], _busId, token);
                    break;
            }
            if (distanceSensor.Status != II2CDevice.DeviceStatus.Initialized)
                return false;
            
            sensor = distanceSensor;
            return true;
        }
        return false;
    }

    // API endpoint delegate to get detected I2C devices
    public IResult DevicesDelegate(HttpContext context)
    {
        try
        {
            var detectedDevices = AutoDetectDevices(context.RequestAborted);
            
            return Results.Json(new
            {
                ok = true,
                devices = detectedDevices.Select(d => new
                {
                    address = d.Address,
                    name = d.Name,
                    type = d.GetType().Name
                }).ToList()
            });
        }
        catch (OperationCanceledException)
        {
            return Results.Json(new { ok = false, error = "Operation cancelled." });
        }
        catch (Exception ex)
        {
            CustomLogger.Log(this, CustomLogger.LogLevel.Error, $"Error in DevicesDelegate: {ex.Message}");
            return Results.Json(new { ok = false, error = "Internal server error.", details = ex.Message });
        }
    }
}
