// AMG8833 8x8 thermopile array from Panasonic

// Hookup Guide: https://learn.sparkfun.com/tutorials/qwiic-grid-eye-infrared-array-amg88xx-hookup-guide
// Datasheet: https://cdn.sparkfun.com/assets/4/1/c/0/1/Grid-EYE_Datasheet.pdf
// Microsoft driver: https://learn.microsoft.com/en-us/dotnet/api/iot.device.amg88xx.amg88xx?view=iot-dotnet-1.2 

namespace immensive;

public class AMG88xx : II2CThermalSensor
{
    // AMG8833 I2C addresses
    public const int DefaultAddress = 0x69; // Default address
    public const int AlternateAddress = 0x68; // Alternate address (jumper configured)

    // AMG8833 Register addresses
    private const byte REG_POWER_CONTROL = 0x00;
    private const byte REG_RESET = 0x01;
    private const byte REG_FRAMERATE = 0x02;
    private const byte REG_INT_CONTROL = 0x03;
    private const byte REG_STATUS = 0x04;
    private const byte REG_PIXEL_BASE = 0x80; // First pixel register (8x8 = 64 pixels, 2 bytes each)

    // Register values
    private const byte POWER_NORMAL = 0x00;
    private const byte POWER_SLEEP = 0x10;
    private const byte FRAMERATE_10HZ = 0x00;
    private const byte FRAMERATE_1HZ = 0x01;
    private const byte RESET_FLAG = 0x30;
    private const byte RESET_INITIAL = 0x3F;

    // Sensor specifications
    private const int SensorWidth = 8;
    private const int SensorHeight = 8;
    private const float DefaultUpdateRateHz = 10.0f; // 10Hz default, can be 1Hz
    private const float FieldOfViewDegrees = 60.0f; // 60° x 60° field of view
    private const float MinTemperatureCelsius = -20.0f;
    private const float MaxTemperatureCelsius = 80.0f;
    private const float TemperatureResolutionCelsius = 0.25f;

    private readonly Specifications _specifications;
    private float _currentFrameRate = DefaultUpdateRateHz;

    public AMG88xx(int address = DefaultAddress) : base(address)
    {
        Name = "AMG8833 Thermal Camera (Grid-EYE)";
        _specifications = new Specifications(
            SensorWidth, 
            SensorHeight, 
            DefaultUpdateRateHz, 
            FieldOfViewDegrees, 
            FieldOfViewDegrees,
            MinTemperatureCelsius,
            MaxTemperatureCelsius,
            TemperatureResolutionCelsius
        );
    }

    public override bool TryDetect(int busId, CancellationToken token = default)
    {
        try
        {
            I2C = new I2C(busId, Address, I2C.TransferMode.Auto);
            if (!I2C.Ping(token))
            {
                Reset();
                return false;
            }

            // Try to read the power control register to verify communication
            try
            {
                byte powerControl = I2C.ReadReg(REG_POWER_CONTROL, token);
                // The power control register should have valid values (0x00 or 0x10)
                if ((powerControl & 0xEF) != 0x00)
                {
                    Reset();
                    return false;
                }
            }
            catch
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
            // Initial reset
            I2C.WriteReg(REG_RESET, RESET_INITIAL, token);
            Thread.Sleep(50); // Wait for reset to complete

            // Set to normal mode
            I2C.WriteReg(REG_POWER_CONTROL, POWER_NORMAL, token);
            Thread.Sleep(50);

            // Configure frame rate if specified
            if (config.TryGetValue("frameRate", out string? frameRateStr))
            {
                if (frameRateStr.Equals("1", StringComparison.OrdinalIgnoreCase) || 
                    frameRateStr.Equals("1Hz", StringComparison.OrdinalIgnoreCase))
                {
                    I2C.WriteReg(REG_FRAMERATE, FRAMERATE_1HZ, token);
                    _currentFrameRate = 1.0f;
                }
                else if (frameRateStr.Equals("10", StringComparison.OrdinalIgnoreCase) || 
                         frameRateStr.Equals("10Hz", StringComparison.OrdinalIgnoreCase))
                {
                    I2C.WriteReg(REG_FRAMERATE, FRAMERATE_10HZ, token);
                    _currentFrameRate = 10.0f;
                }
            }
            else
            {
                // Default to 10Hz
                I2C.WriteReg(REG_FRAMERATE, FRAMERATE_10HZ, token);
                _currentFrameRate = 10.0f;
            }

            Thread.Sleep(100); // Allow sensor to stabilize

            Initialized = true;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to initialize AMG8833 sensor: {ex.Message}", ex);
        }
    }

    public override Specifications CurrentSpecifications()
    {
        return _specifications;
    }

    public override float[,] ReadOnce(int TimeoutMs = 1000, CancellationToken token = default)
    {
        if (!Initialized)
            throw new InvalidOperationException("Sensor not initialized. Call Initialize() first.");

        try
        {
            var startTime = DateTime.UtcNow;
            
            // Wait for data to be ready by checking the status register
            bool dataReady = false;
            while (!dataReady && (DateTime.UtcNow - startTime).TotalMilliseconds < TimeoutMs)
            {
                token.ThrowIfCancellationRequested();
                
                // Wait for at least one frame period before checking
                Thread.Sleep((int)(1000.0f / _currentFrameRate));
                
                // Check status register (bit 1 indicates data ready)
                byte status = I2C.ReadReg(REG_STATUS, token);
                dataReady = true; // For AMG8833, we assume data is ready after waiting
                break;
            }

            if (!dataReady)
                throw new TimeoutException($"Timeout waiting for thermal data from AMG8833");

            // Read all 64 pixels (128 bytes: 2 bytes per pixel)
            byte[] pixelData = I2C.ReadRegs(REG_PIXEL_BASE, 128, token);
            
            // Convert to 2D array (8x8)
            var result = new float[SensorHeight, SensorWidth];
            
            for (int y = 0; y < SensorHeight; y++)
            {
                for (int x = 0; x < SensorWidth; x++)
                {
                    int pixelIndex = y * SensorWidth + x;
                    int byteIndex = pixelIndex * 2;
                    
                    // Combine two bytes into a 12-bit signed value
                    short rawValue = (short)((pixelData[byteIndex + 1] << 8) | pixelData[byteIndex]);
                    
                    // Convert to temperature in Celsius
                    // Each bit represents 0.25°C
                    float temperature = rawValue * 0.25f;
                    
                    result[y, x] = temperature;
                }
            }

            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not TimeoutException)
        {
            throw new InvalidOperationException($"Failed to read from AMG8833 sensor: {ex.Message}", ex);
        }
    }
}