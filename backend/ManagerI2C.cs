using System.Device.I2c;
using Iot.Device.Mcp23xxx;

namespace immensive;

/// Manager for I2C bus operations
public class ManagerI2C
{
    public ManagerI2C(int busId = 1)
    {
        _busId = busId;

        RegisterDevice(new TMF882X());
        RegisterDevice(new VL53L5CX());
        RegisterDevice(new AMG88xx(AMG88xx.DefaultAddress));
        RegisterDevice(new AMG88xx(AMG88xx.AlternateAddress));
        RegisterDevice(new MLX90640()); // Using Meadow.Foundation library
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
    /// Try to resolve a device by address, detecting it on the bus if needed.
    /// </summary>
    public bool TryGetDevice(int address, out II2CDevice? device, CancellationToken token = default)
    {
        device = null;

        if (!Devices.TryGetValue(address, out List<II2CDevice>? value))
            return false;

        foreach (var dev in value)
        {
            token.ThrowIfCancellationRequested();
            switch (dev.Status)
            {
                case II2CDevice.DeviceStatus.Unknown:
                case II2CDevice.DeviceStatus.Detected:
                    dev.Initialize([], _busId, token);
                    break;
            }
            if (dev.Status != II2CDevice.DeviceStatus.Initialized)
                return false;
            
            device = dev;
            return true;
        }
        return false;
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
                    type = d.Type.ToString(),
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

    // API endpoint delegate to get device specifications
    public IResult DeviceSpecificationsDelegate(HttpContext context, string address)
    {
        try
        {
            if (!TryParseI2cAddress(address, out int addr))
                return Results.BadRequest(new { ok = false, error = "Invalid I2C address." });

            if (!TryGetDevice(addr, out var device, context.RequestAborted) || device == null)
                return Results.NotFound(new { ok = false, error = "Device not found." });

            // Check if device supports specifications (distance or thermal sensor)
            object? specs = null;
            if (device is II2CDistanceSensor distanceSensor)
            {
                specs = distanceSensor.CurrentSpecifications();
            }
            else if (device is II2CThermalSensor thermalSensor)
            {
                specs = thermalSensor.CurrentSpecifications();
            }
            else
            {
                return Results.Json(new { ok = false, error = "Device does not support specifications." });
            }

            return Results.Json(new
            {
                ok = true,
                address = device.Address,
                name = device.Name,
                type = device.Type.ToString(),
                specifications = specs
            });
        }
        catch (OperationCanceledException)
        {
            return Results.Json(new { ok = false, error = "Operation cancelled." });
        }
        catch (Exception ex)
        {
            CustomLogger.Log(this, CustomLogger.LogLevel.Error, $"Error in DeviceSpecificationsDelegate: {ex.Message}");
            return Results.Json(new { ok = false, error = "Internal server error.", details = ex.Message });
        }
    }

    // API endpoint delegate to get device measurement
    public IResult DeviceMeasureDelegate(HttpContext context, string address)
    {
        try
        {
            if (!TryParseI2cAddress(address, out int addr))
                return Results.BadRequest(new { ok = false, error = "Invalid I2C address." });

            if (!TryGetDevice(addr, out var device, context.RequestAborted) || device == null)
                return Results.NotFound(new { ok = false, error = "Device not found." });

            // Handle distance sensors
            if (device is II2CDistanceSensor distanceSensor)
            {
                var measurement = distanceSensor.ReadOnce(token: context.RequestAborted)
                    .Select(m => new { distMM = m.distMM, confidence = Math.Round(m.confidence, 3) })
                    .ToList();
                return Results.Json(new
                {
                    ok = true,
                    address = device.Address,
                    name = device.Name,
                    type = device.Type.ToString(),
                    measurement
                });
            }
            // Handle thermal sensors
            else if (device is II2CThermalSensor thermalSensor)
            {
                var temps = thermalSensor.ReadOnce(token: context.RequestAborted);
                int height = temps.GetLength(0);
                int width = temps.GetLength(1);
                
                // Convert 2D array to jagged array for JSON serialization
                var temperatures = new float[height][];
                for (int y = 0; y < height; y++)
                {
                    temperatures[y] = new float[width];
                    for (int x = 0; x < width; x++)
                    {
                        temperatures[y][x] = (float)Math.Round(temps[y, x], 2);
                    }
                }
                
                return Results.Json(new
                {
                    ok = true,
                    address = device.Address,
                    name = device.Name,
                    type = device.Type.ToString(),
                    measurement = new { temperatures }
                });
            }
            else
            {
                return Results.Json(new { ok = false, error = "Device does not support measurements." });
            }
        }
        catch (OperationCanceledException)
        {
            return Results.Json(new { ok = false, error = "Operation cancelled." });
        }
        catch (TimeoutException ex)
        {
            CustomLogger.Log(this, CustomLogger.LogLevel.Warning, $"Timeout in DeviceMeasureDelegate: {ex.Message}");
            return Results.Json(new { ok = false, error = "Measurement timeout.", details = ex.Message });
        }
        catch (Exception ex)
        {
            CustomLogger.Log(this, CustomLogger.LogLevel.Error, $"Error in DeviceMeasureDelegate: {ex.Message}");
            return Results.Json(new { ok = false, error = "Internal server error.", details = ex.Message });
        }
    }

    private static bool TryParseI2cAddress(string addressText, out int address)
    {
        address = 0;
        if (string.IsNullOrWhiteSpace(addressText))
            return false;

        addressText = addressText.Trim();
        if (addressText.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return int.TryParse(addressText[2..], System.Globalization.NumberStyles.HexNumber, null, out address);

        return int.TryParse(addressText, out address);
    }
}
