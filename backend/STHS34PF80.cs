// Datasheet: https://www.st.com/resource/en/datasheet/sths34pf80.pdf
// Hookup guide: https://docs.sparkfun.com/SparkFun_Qwiic_Human_Presence_Sensor-STHS34PF80/software_overview/
// Arduino library: https://github.com/sparkfun/SparkFun_STHS34PF80_Arduino_Library

namespace immensive;

public class STHS34PF80 : II2CHumanPresenceSensor
{
    private const int DefaultAddress = 0x5A;

    // Register addresses
    private const byte REG_WHO_AM_I = 0x0F;
    private const byte WHO_AM_I_VALUE = 0xD3;
    
    private const byte REG_CTRL1 = 0x20;
    private const byte REG_CTRL2 = 0x21;
    private const byte REG_CTRL3 = 0x22;
    private const byte REG_STATUS = 0x23;
    
    private const byte REG_FUNC_STATUS = 0x25;
    private const byte REG_TOBJECT_L = 0x26;
    private const byte REG_TOBJECT_H = 0x27;
    private const byte REG_TAMBIENT_L = 0x28;
    private const byte REG_TAMBIENT_H = 0x29;
    
    private const byte REG_TOBJ_COMP_L = 0x38;
    private const byte REG_TOBJ_COMP_H = 0x39;
    private const byte REG_TPRESENCE_L = 0x3A;
    private const byte REG_TPRESENCE_H = 0x3B;
    private const byte REG_TMOTION_L = 0x3C;
    private const byte REG_TMOTION_H = 0x3D;
    private const byte REG_TAMB_SHOCK_L = 0x3E;
    private const byte REG_TAMB_SHOCK_H = 0x3F;

    // Configuration
    private float _updateRateHz = 4.0f;
    private float _detectionRangeMeters = 4.0f;
    private int _presenceThreshold = 200;      // Default: 2.00°C
    private int _motionThreshold = 200;        // Default: 2.00°C
    private int _ambientShockThreshold = 200;  // Default: 2.00°C

    public STHS34PF80(int address = DefaultAddress) : base(address)
    {
        Name = "STHS34PF80";
    }

    public override bool TryDetect(int busId, CancellationToken token = default)
    {
        try
        {
            using var i2c = new I2C(busId, Address, I2C.TransferMode.Auto);
            var whoAmI = i2c.ReadReg(REG_WHO_AM_I, token);
            return whoAmI == WHO_AM_I_VALUE;
        }
        catch
        {
            return false;
        }
    }

    public override void Initialize(Dictionary<string, string> config, int busId = -1, CancellationToken token = default)
    {
        base.Initialize(config, busId, token);
        
        if (!Initialized) return;

        CustomLogger.Log(this, CustomLogger.LogLevel.Info, $"STHS34PF80: Starting initialization at address 0x{Address:X2}");

        // Parse configuration
        if (config.TryGetValue("updateRateHz", out string? rateStr) && float.TryParse(rateStr, out float rate))
            _updateRateHz = rate;
        
        if (config.TryGetValue("detectionRangeMeters", out string? rangeStr) && float.TryParse(rangeStr, out float range))
            _detectionRangeMeters = range;

        // Parse thresholds (in 0.01°C units, e.g. 200 = 2.00°C)
        if (config.TryGetValue("presenceThreshold", out string? presStr) && int.TryParse(presStr, out int presThs))
            _presenceThreshold = presThs;
        
        if (config.TryGetValue("motionThreshold", out string? motStr) && int.TryParse(motStr, out int motThs))
            _motionThreshold = motThs;
        
        if (config.TryGetValue("ambientShockThreshold", out string? ambStr) && int.TryParse(ambStr, out int ambThs))
            _ambientShockThreshold = ambThs;

        CustomLogger.Log(this, CustomLogger.LogLevel.Info, $"STHS34PF80: Config - UpdateRate: {_updateRateHz} Hz, Range: {_detectionRangeMeters} m");

        // Reset device
        CustomLogger.Log(this, CustomLogger.LogLevel.Info, "STHS34PF80: Performing software reset (BOOT)");
        I2C.WriteReg(REG_CTRL2, 0x80, token); // BOOT bit (bit 7) - reboot memory content
        
        // Wait for BOOT bit to return to 0 (reset complete)
        var bootTimeout = DateTime.UtcNow.AddSeconds(1);
        while (DateTime.UtcNow < bootTimeout)
        {
            var ctrl2 = I2C.ReadReg(REG_CTRL2, token);
            if ((ctrl2 & 0x80) == 0) // BOOT bit cleared
            {
                CustomLogger.Log(this, CustomLogger.LogLevel.Info, "STHS34PF80: Software reset complete");
                break;
            }
            Thread.Sleep(10);
        }
        
        // Additional delay for sensor stabilization
        Thread.Sleep(50);

        // Verify WHO_AM_I after reset
        var whoAmI = I2C.ReadReg(REG_WHO_AM_I, token);
        CustomLogger.Log(this, CustomLogger.LogLevel.Info, $"STHS34PF80: WHO_AM_I = 0x{whoAmI:X2} (expected 0x{WHO_AM_I_VALUE:X2})");
        
        if (whoAmI != WHO_AM_I_VALUE)
        {
            CustomLogger.Log(this, CustomLogger.LogLevel.Error, $"STHS34PF80: WHO_AM_I mismatch! Sensor not responding correctly.");
            Initialized = false;
            return;
        }

        // Configure device
        // CTRL1: Set ODR (Output Data Rate) in bits 0-3, BDU (Block Data Update) in bit 4
        byte odrValue = _updateRateHz switch
        {
            >= 30 => 0x08, // 30 Hz (1xxx)
            >= 15 => 0x07, // 15 Hz
            >= 8 => 0x06,  // 8 Hz
            >= 4 => 0x05,  // 4 Hz
            >= 2 => 0x04,  // 2 Hz
            >= 1 => 0x03,  // 1 Hz
            >= 0.5f => 0x02,  // 0.5 Hz
            >= 0.25f => 0x01, // 0.25 Hz
            _ => 0x05      // Default: 4 Hz
        };
        byte ctrl1Value = (byte)(0x10 | odrValue);
        I2C.WriteReg(REG_CTRL1, ctrl1Value, token);

        // CTRL3: Enable interrupts and configure data ready
        I2C.WriteReg(REG_CTRL3, 0x04, token);

        CustomLogger.Log(this, CustomLogger.LogLevel.Info, "STHS34PF80: Initialization complete");
        Initialized = true;
    }

    public override Specifications CurrentSpecifications()
    {
        return new Specifications(
            UpdateRateHz: _updateRateHz,
            VerticalFOVDeg: 80.0f,
            HorizontalFOVDeg: 80.0f,
            MinTempCelsius: -10.0f,
            MaxTempCelsius: 60.0f,
            ResolutionCelsius: 0.01f,
            DetectionRangeMeters: _detectionRangeMeters
        );
    }

    public override PresenceMeasurement ReadOnce(int TimeoutMs = 1000, CancellationToken token = default)
    {
        if (!Initialized)
            throw new InvalidOperationException($"Device {Name} not initialized");

        var startTime = DateTime.UtcNow;
        
        // Wait for data ready
        while ((DateTime.UtcNow - startTime).TotalMilliseconds < TimeoutMs)
        {
            token.ThrowIfCancellationRequested();
            
            var status = I2C.ReadReg(REG_STATUS, token);
            if ((status & 0x04) != 0) // Data ready for TAMBIENT, TOBJECT, TAMB_SHOCK, TPRESENCE, TMOTION. 
                break;
                
            Thread.Sleep(10);
        }

        // Read temperature values (all values are signed 16-bit integers in LSB = 0.01°C)
        var tAmbientRaw = I2C.ReadRegShort(REG_TAMBIENT_L, I2C.ByteOrder.LittleEndian, token);
        var tObjectRaw = I2C.ReadRegShort(REG_TOBJECT_L, I2C.ByteOrder.LittleEndian, token);
        var tPresenceRaw = I2C.ReadRegShort(REG_TPRESENCE_L, I2C.ByteOrder.LittleEndian, token);
        var tMotionRaw = I2C.ReadRegShort(REG_TMOTION_L, I2C.ByteOrder.LittleEndian, token);
        var tAmbShockRaw = I2C.ReadRegShort(REG_TAMB_SHOCK_L, I2C.ByteOrder.LittleEndian, token);

        // Calculate detection flags by comparing raw values to configured thresholds
        // Note: We use absolute value for comparison since signatures can be negative
        bool presenceDetected = Math.Abs(tPresenceRaw) >= _presenceThreshold;
        bool motionDetected = Math.Abs(tMotionRaw) >= _motionThreshold;
        bool ambientShockDetected = Math.Abs(tAmbShockRaw) >= _ambientShockThreshold;

        // Convert to Celsius (LSB = 0.01°C per datasheet, values are in two's complement)
        float ambientTemp = tAmbientRaw / 100.0f;
        float objectTempRelative = tObjectRaw / 100.0f;
        // Object temperature is relative to ambient, so we add them to get absolute temperature
        float objectTemp = ambientTemp + objectTempRelative;

        return new PresenceMeasurement(
            PresenceDetected: presenceDetected,
            MotionDetected: motionDetected,
            AmbientShockDetected: ambientShockDetected,
            AmbientTemperatureCelsius: ambientTemp,
            ObjectTemperatureCelsius: objectTemp,
            PresenceValue: tPresenceRaw,
            MotionValue: tMotionRaw,
            AmbientShockValue: tAmbShockRaw
        );
    }
}