using System.Device.I2c;
using Iot.Device.Mcp23xxx;

namespace immensive;

/// Manager for I2C bus operations
public class ManagerI2C
{
    private const byte VirtualSensorBaseAddress = 0x80; // Base address for virtual sensors (not real I2C addresses)

    private readonly int _busId;

    public Dictionary<int, List<II2CDevice>> Devices {get; private set;} = [];

    public ManagerI2C(int busId = 1)
    {
        _busId = busId;

        RegisterDevice(new TMF882X());
        RegisterDevice(new VL53L5CX());
        RegisterDevice(new AMG88xx(AMG88xx.DefaultAddress));
        RegisterDevice(new AMG88xx(AMG88xx.AlternateAddress));
        RegisterDevice(new MLX90640()); // Using Meadow.Foundation library
        RegisterDevice(new STHS34PF80());
        RegisterDevice(new BME680());
        RegisterDevice(new BME680(0x76));

        // For every detected thermal sensor, we will create a corresponding virtual human presence sensor
        List<II2CThermalSensor> thermalSensors = [];
        foreach (var deviceList in Devices.Values)
            foreach (var device in deviceList)
                if (device is II2CThermalSensor thermalSensor)
                    thermalSensors.Add(thermalSensor);
        foreach (var thermalSensor in thermalSensors)
            RegisterVirtualHumanPresenceSensor(thermalSensor);

        // For every detected distance sensor, we will create a corresponding virtual human distance sensor
        List<II2CDistanceSensor> distanceSensors = [];
        foreach (var deviceList in Devices.Values)
            foreach (var device in deviceList)
                if (device is II2CDistanceSensor distanceSensor)
                    distanceSensors.Add(distanceSensor);
        foreach (var distanceSensor in distanceSensors)
            RegisterVirtualHumanDistanceSensor(distanceSensor);
    }

    // For every detected thermal sensor, we will create a corresponding virtual human presence sensor
    public void RegisterVirtualHumanPresenceSensor(II2CThermalSensor thermalSensor)
    {
        var virtualAddress = thermalSensor.Address + VirtualSensorBaseAddress;
        var virtualPresenceSensor = new ThermalHumanPresenceSensor(thermalSensor, virtualAddress);
        RegisterDevice(virtualPresenceSensor);
        CustomLogger.Log(this, CustomLogger.LogLevel.Info, 
            $"Registered virtual human presence sensor for thermal sensor: {thermalSensor.Name}");
    }

    // For every detected distance sensor, we will create a corresponding virtual human distance sensor
    public void RegisterVirtualHumanDistanceSensor(II2CDistanceSensor distanceSensor)
    {
        var virtualAddress = distanceSensor.Address + VirtualSensorBaseAddress;
        var virtualDistanceSensor = new DistanceHumanPresenceSensor(distanceSensor, virtualAddress);
        RegisterDevice(virtualDistanceSensor);
        CustomLogger.Log(this, CustomLogger.LogLevel.Info,
            $"Registered virtual human distance sensor for distance sensor: {distanceSensor.Name}");
    }

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
                            
                            // If a thermal sensor is detected, also add the virtual human presence sensor
                            if (device is II2CThermalSensor thermalSensor)
                            {
                                int virtualAddress = thermalSensor.Address + VirtualSensorBaseAddress;
                                if (Devices.TryGetValue(virtualAddress, out List<II2CDevice>? virtualDevices))
                                    foreach (var virtualDevice in virtualDevices)
                                        if (virtualDevice is ThermalHumanPresenceSensor presenceSensor &&
                                            presenceSensor.ThermalSensor == thermalSensor)
                                        {
                                            devices.Add(presenceSensor);
                                            CustomLogger.Log(this, CustomLogger.LogLevel.Info,
                                                $"Also detected virtual human presence sensor for thermal sensor: {thermalSensor.Name}");
                                            break;
                                        }
                            }

                            // If a distance sensor is detected, also add the virtual human distance sensor
                            if (device is II2CDistanceSensor distanceSensor)
                            {
                                int virtualAddress = distanceSensor.Address + VirtualSensorBaseAddress;
                                if (Devices.TryGetValue(virtualAddress, out List<II2CDevice>? virtualDevices))
                                    foreach (var virtualDevice in virtualDevices)
                                        if (virtualDevice is DistanceHumanPresenceSensor humanDistanceSensor &&
                                            humanDistanceSensor.DistanceSensor == distanceSensor)
                                        {
                                            devices.Add(humanDistanceSensor);
                                            CustomLogger.Log(this, CustomLogger.LogLevel.Info,
                                                $"Also detected virtual human distance sensor for distance sensor: {distanceSensor.Name}");
                                            break;
                                        }
                            }
                        
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

            // Check if device supports specifications (distance, thermal, human presence, human distance, or environmental sensor)
            object? specs = null;
            if (device is II2CHumanDistanceSensor humanDistanceSensor)
            {
                specs = humanDistanceSensor.CurrentSpecifications();
            }
            else if (device is II2CDistanceSensor distanceSensor)
            {
                specs = distanceSensor.CurrentSpecifications();
            }
            else if (device is II2CThermalSensor thermalSensor)
            {
                specs = thermalSensor.CurrentSpecifications();
            }
            else if (device is II2CHumanPresenceSensor presenceSensor)
            {
                specs = presenceSensor.CurrentSpecifications();
            }
            else if (device is II2CEnvironmentalSensor environmentalSensor)
            {
                specs = environmentalSensor.CurrentSpecifications();
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

    // API endpoint delegate to get device data
    public IResult DeviceDataDelegate(HttpContext context, string address)
    {
        try
        {
            if (!TryParseI2cAddress(address, out int addr))
                return Results.BadRequest(new { ok = false, error = "Invalid I2C address." });

            if (!TryGetDevice(addr, out var device, context.RequestAborted) || device == null)
                return Results.NotFound(new { ok = false, error = "Device not found." });

            // Handle virtual human distance sensors (must be checked before II2CDistanceSensor)
            if (device is II2CHumanDistanceSensor humanDistanceSensor)
            {
                var data = humanDistanceSensor.ReadOnce(token: context.RequestAborted);
                return Results.Json(new
                {
                    ok = true,
                    address = device.Address,
                    name = device.Name,
                    type = device.Type.ToString(),
                    measurement = new
                    {
                        presence = data.Presence,
                        position = data.Position == null ? null : new
                        {
                            x        = MathF.Round(data.Position.X, 3),
                            y        = MathF.Round(data.Position.Y, 3),
                            z        = MathF.Round(data.Position.Z, 3),
                            distance = MathF.Round(data.Position.Distance(), 3),
                        },
                        quality01 = MathF.Round(data.Quality01, 3),
                    }
                });
            }
            // Handle distance sensors
            else if (device is II2CDistanceSensor distanceSensor)
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
            // Handle human presence sensors
            else if (device is II2CHumanPresenceSensor presenceSensor)
            {
                var data = presenceSensor.ReadOnce(token: context.RequestAborted);
                return Results.Json(new
                {
                    ok = true,
                    address = device.Address,
                    name = device.Name,
                    type = device.Type.ToString(),
                    measurement = new
                    {
                        presenceDetected = data.PresenceDetected,
                        motionDetected = data.MotionDetected,
                        ambientShockDetected = data.AmbientShockDetected,
                        ambientTemperatureCelsius = Math.Round(data.AmbientTemperatureCelsius, 2),
                        objectTemperatureCelsius = Math.Round(data.ObjectTemperatureCelsius, 2),
                        presenceValue = data.PresenceValue,
                        motionValue = data.MotionValue,
                        ambientShockValue = data.AmbientShockValue
                    }
                });
            }
            // Handle environmental sensors
            else if (device is II2CEnvironmentalSensor environmentalSensor)
            {
                var envData = environmentalSensor.ReadOnce(token: context.RequestAborted);
                envData.TryGetValue(II2CEnvironmentalSensor.MeasurementType.Temperature, out float tempC);
                envData.TryGetValue(II2CEnvironmentalSensor.MeasurementType.Humidity, out float humPct);
                envData.TryGetValue(II2CEnvironmentalSensor.MeasurementType.Pressure, out float presHPa);
                envData.TryGetValue(II2CEnvironmentalSensor.MeasurementType.Gas, out float gasOhm);
                return Results.Json(new
                {
                    ok = true,
                    address = device.Address,
                    name = device.Name,
                    type = device.Type.ToString(),
                    measurement = new
                    {
                        temperatureCelsius = envData.ContainsKey(II2CEnvironmentalSensor.MeasurementType.Temperature) ? (float?)MathF.Round(tempC, 2) : null,
                        humidityPercent    = envData.ContainsKey(II2CEnvironmentalSensor.MeasurementType.Humidity)    ? (float?)MathF.Round(humPct, 2) : null,
                        pressureHPa        = envData.ContainsKey(II2CEnvironmentalSensor.MeasurementType.Pressure)    ? (float?)MathF.Round(presHPa, 2) : null,
                        gasResistanceOhms  = envData.ContainsKey(II2CEnvironmentalSensor.MeasurementType.Gas)         ? (float?)MathF.Round(gasOhm, 0) : null,
                        iaqIndex           = envData.TryGetValue(II2CEnvironmentalSensor.MeasurementType.IAQ, out float iaq) ? (float?)MathF.Round(iaq, 1) : null,
                    }
                });
            }
            else
            {
                return Results.Json(new { ok = false, error = "Device does not support data readings." });
            }
        }
        catch (OperationCanceledException)
        {
            return Results.Json(new { ok = false, error = "Operation cancelled." });
        }
        catch (TimeoutException ex)
        {
            CustomLogger.Log(this, CustomLogger.LogLevel.Warning, $"Timeout in DeviceDataDelegate: {ex.Message}");
            return Results.Json(new { ok = false, error = "Data read timeout.", details = ex.Message });
        }
        catch (Exception ex)
        {
            CustomLogger.Log(this, CustomLogger.LogLevel.Error, $"Error in DeviceDataDelegate: {ex.Message}");
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
