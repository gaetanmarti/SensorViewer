// MLX90640 32x24 thermopile array from Melexis
// Implementation using Meadow.Foundation library
// Datasheet: https://cdn.sparkfun.com/assets/3/1/c/6/f/MLX90640-Datasheet.pdf

using Meadow.Foundation.Sensors.Camera;
using Meadow.Hardware;
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

            CustomLogger.Log(this, CustomLogger.LogLevel.Info, 
                $"MLX90640 initialized using Meadow.Foundation library");

            Initialized = true;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to initialize MLX90640 sensor: {ex.Message}", ex);
        }
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
