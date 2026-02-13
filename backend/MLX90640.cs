// MLX90640 32x24 thermopile array from Melexis
// Implementation using Meadow.Foundation library
// Hookup guide: https://learn.sparkfun.com/tutorials/qwiic-ir-array-mlx90640-hookup-guide/all
// Datasheet: https://cdn.sparkfun.com/assets/3/1/c/6/f/MLX90640-Datasheet.pdf
// Meadow library documentation: https://www.nuget.org/packages/Meadow.Foundation.Sensors.Camera.Mlx90640/2.5.0.5-beta

using Meadow.Foundation.Sensors.Camera;
using Meadow.Hardware;
using Meadow.Units;
using System.Device.I2c;

namespace immensive;

public class MLX90640 : II2CThermalSensor
{
    // MLX90640 I2C addresses
    public const int DefaultAddress = 0x33; // Default address

    // Sensor specifications
    private const int SensorWidth = 32;
    private const int SensorHeight = 24;
    private const float DefaultUpdateRateHz = 8.0f;
    private const float HorizontalFOVDegrees = 55.0f;
    private const float VerticalFOVDegrees = 35.0f;
    private const float MinTemperatureCelsius = -40.0f;
    private const float MaxTemperatureCelsius = 300.0f;
    private const float TemperatureResolutionCelsius = 0.1f;

    private readonly Specifications _specifications;
    private Mlx90640? _meadowSensor = null;
    private I2cDevice? _i2cDevice = null;

   
    public MLX90640(int address = DefaultAddress) : base(address)
    {
        Name = "MLX90640 Thermal Camera (Meadow)";
        _specifications = new Specifications(
            SensorWidth, 
            SensorHeight, 
            DefaultUpdateRateHz, 
            VerticalFOVDegrees, 
            HorizontalFOVDegrees,
            MinTemperatureCelsius,
            MaxTemperatureCelsius,
            TemperatureResolutionCelsius
        );
    }

    protected override I2C.TransferMode PreferredTransferMode => I2C.TransferMode.WriteRead;

    public override bool TryDetect(int busId, CancellationToken token = default)
    {
        try
        {
            I2C = new I2C(busId, Address, PreferredTransferMode);
            if (!I2C.Ping(token))
            {
                Reset();
                return false;
            }

            return true;
        }
        catch
        {
            Reset();
            return false;
        }
    }

    public override void Initialize(Dictionary<string, string> config, int busId = -1, CancellationToken token = default)
    {
        base.Initialize(config, busId, token);
        token.ThrowIfCancellationRequested();

        try
        {
            // Create System.Device.I2c wrapper for Meadow library
            var settings = new I2cConnectionSettings(busId, Address);
            _i2cDevice = I2cDevice.Create(settings);

            // Create Meadow I2cBus adapter
            var i2cBus = new MeadowI2cBusAdapter(_i2cDevice);

            // Initialize Meadow MLX90640 sensor
            _meadowSensor = new Mlx90640(i2cBus, (byte)Address);
            _meadowSensor.SetRefreshRate(Mlx90640.RefreshRate._2hz); // Default to 2Hz, can be overridden by config
            _meadowSensor.SetResolution(Mlx90640.Resolution.NineteenBit);
            _meadowSensor.SetMode(Mlx90640.Mode.Interleaved); // Chess mode for better noise performance

            // Configure emissivity if specified (default 0.95)
            // Human skin: 0.98, walls: 0.85-0.95, metal: 0.1-0.3
            if (config.TryGetValue("emissivity", out string? emissivityStr))
            {
                if (float.TryParse(emissivityStr, out float emissivity))
                {
                    emissivity = Math.Clamp(emissivity, 0.1f, 1.0f);
                    _meadowSensor.Emissivity = emissivity;
                    CustomLogger.Log(this, CustomLogger.LogLevel.Info, 
                        $"MLX90640 emissivity set to: {emissivity:F2}");
                }
            }

            // Configure frame rate if specified
            if (config.TryGetValue("framerate", out string? frameRateStr))
            {
                var refreshRate = ParseRefreshRate(frameRateStr);
                if (refreshRate.HasValue)
                {
                    _meadowSensor.SetRefreshRate(refreshRate.Value);
                    CustomLogger.Log(this, CustomLogger.LogLevel.Info, 
                        $"MLX90640 refresh rate set to: {_meadowSensor.GetRefreshRate()}");
                }
                else
                {
                    CustomLogger.Log(this, CustomLogger.LogLevel.Warning, 
                        $"Invalid framerate value: {frameRateStr}. Using default.");
                }
            }

            CustomLogger.Log(this, CustomLogger.LogLevel.Info, 
                $"MLX90640 initialized using Meadow.Foundation library. Serial number: {_meadowSensor.SerialNumber}");
            CustomLogger.Log(this, CustomLogger.LogLevel.Info, 
                $"Current settings - Frame rate: {_meadowSensor.GetRefreshRate()}, Emissivity: {_meadowSensor.Emissivity:F2}");

            Initialized = true;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to initialize MLX90640 sensor: {ex.Message}", ex);
        }
    }

    private Mlx90640.RefreshRate? ParseRefreshRate(string value)
    {
        // Remove "Hz" suffix if present and parse
        var normalized = value.Replace("Hz", "").Replace("hz", "").Trim();
        
        return normalized switch
        {
            "0.5" => Mlx90640.RefreshRate._0_5hz,
            "1" => Mlx90640.RefreshRate._1hz,
            "2" => Mlx90640.RefreshRate._2hz,
            "4" => Mlx90640.RefreshRate._4hz,
            "8" => Mlx90640.RefreshRate._8hz,
            "16" => Mlx90640.RefreshRate._16hz,
            "32" => Mlx90640.RefreshRate._32hz,
            "64" => Mlx90640.RefreshRate._64hz,
            _ => null
        };
    }

    public override Specifications CurrentSpecifications()
    {
        return _specifications;
    }

    public override float[,] ReadOnce(int TimeoutMs = 1000, CancellationToken token = default)
    {
        if (!Initialized || _meadowSensor == null)
            throw new InvalidOperationException("Sensor not initialized. Call Initialize() first.");

        try
        {
            token.ThrowIfCancellationRequested();

            var result = new float[SensorHeight, SensorWidth];

            // Read frame from Meadow sensor
            var frame = _meadowSensor.ReadRawData();

            //CustomLogger.Log(this, CustomLogger.LogLevel.Info, 
            //    $"MLX90640 emissivity: {_meadowSensor.Emissivity:F2}, reflected temp: {_meadowSensor.ReflectedTemperature}");

            // Convert to 2D array
            for (int y = 0; y < SensorHeight; y++)
            {
                for (int x = 0; x < SensorWidth; x++)
                {
                    int index = y * SensorWidth + x;
                    result[y, x] = frame[index];
                }
            }

            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException($"Failed to read from MLX90640 sensor: {ex.Message}", ex);
        }
    }

    public new void Reset()
    {
        if (_meadowSensor != null)
        {
            try
            {
                // Dispose Meadow sensor
                _meadowSensor = null;
            }
            catch { }
        }

        if (_i2cDevice != null)
        {
            try
            {
                _i2cDevice.Dispose();
                _i2cDevice = null;
            }
            catch { }
        }

        base.Reset();
    }
}

/// <summary>
/// Adapter class to adapt System.Device.I2c.I2cDevice to Meadow.Hardware.II2cBus
/// </summary>
internal class MeadowI2cBusAdapter : II2cBus
{
    private readonly I2cDevice _device;

    public MeadowI2cBusAdapter(I2cDevice device)
    {
        _device = device;
    }

    public I2cBusSpeed BusSpeed { get; set; } = I2cBusSpeed.Standard;

    public void Exchange(byte peripheralAddress, Span<byte> writeBuffer, Span<byte> readBuffer)
    {
        if (writeBuffer.Length > 0 && readBuffer.Length > 0)
        {
            _device.WriteRead(writeBuffer, readBuffer);
        }
        else if (writeBuffer.Length > 0)
        {
            _device.Write(writeBuffer);
        }
        else if (readBuffer.Length > 0)
        {
            _device.Read(readBuffer);
        }
    }

    public void Read(byte peripheralAddress, Span<byte> readBuffer)
    {
        _device.Read(readBuffer);
    }

    public void Write(byte peripheralAddress, Span<byte> writeBuffer)
    {
        _device.Write(writeBuffer);
    }

    public byte[] WriteData(byte peripheralAddress, params byte[] data)
    {
        _device.Write(data);
        return Array.Empty<byte>();
    }

    public byte[] ReadData(byte peripheralAddress, int numberOfBytes)
    {
        byte[] buffer = new byte[numberOfBytes];
        _device.Read(buffer);
        return buffer;
    }

    public void Dispose()
    {
        // Device disposal is handled by the caller
    }
}
