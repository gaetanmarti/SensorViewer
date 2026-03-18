// Datasheet: https://www.st.com/resource/en/datasheet/sths34pf80.pdf
// Hookup guide: https://docs.sparkfun.com/SparkFun_Qwiic_Human_Presence_Sensor-STHS34PF80/software_overview/
// Arduino library: https://github.com/sparkfun/SparkFun_STHS34PF80_Arduino_Library
// I2c guide: https://www.st.com/resource/en/application_note/an5867-sths34pf80-lowpower-highsensitivity-infrared-ir-sensor-for-presence-and-motion-detection-stmicroelectronics.pdf

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

    // Embedded function registers access
    private const byte REG_FUNC_CFG_ADDR = 0x08;      // Address pointer for embedded register access
    private const byte REG_FUNC_CFG_DATA = 0x09;      // Data register for embedded register access
    private const byte REG_PAGE_RW = 0x11;            // Page Read/Write control
    
    // Normal configuration registers (direct access, no func_cfg needed)
    private const byte REG_AVG_TRIM = 0x1E;           // Averaging config: bits 2:0 = avg_tmos, bits 4:3 = avg_t
    private const byte REG_LPF1 = 0x33;               // Low-pass filter config 1: bits 2:0 = lpf_m, bits 6:4 = lpf_p_m
    private const byte REG_LPF2 = 0x34;               // Low-pass filter config 2: bits 2:0 = lpf_p, bits 6:4 = lpf_a_t
    
    // Embedded register addresses (accessed via FUNC_CFG_ADDR + FUNC_CFG_DATA sequence)
    private const byte EMB_PRESENCE_THS = 0x20;       // Presence threshold (little-endian 16-bit)
    private const byte EMB_MOTION_THS = 0x22;         // Motion threshold (little-endian 16-bit)
    private const byte EMB_TAMB_SHOCK_THS = 0x24;     // Ambient shock threshold (little-endian 16-bit)
    private const byte EMB_HYST_PRESENCE = 0x2C;      // Presence hysteresis (8-bit)
    private const byte EMB_HYST_MOTION = 0x2D;        // Motion hysteresis (8-bit)
    private const byte EMB_HYST_TAMB_SHOCK = 0x2E;    // Ambient shock hysteresis (8-bit)
    private const byte EMB_ALGO_CONFIG = 0x28;        // Algorithm config (bit 1: sel_abs, bit 2: comp_type, bit 3: int_pulsed)
    private const byte EMB_RESET_ALGO = 0x2A;          // Algorithm reset: write 0x01 to reset IIR filters (ALGO_ENABLE_RESET bit 0)

    // Configuration
    private float _updateRateHz = 15.0f;
    private float _detectionRangeMeters = 4.0f;
    
    // Thresholds (in 0.01°C units) - Optimized for indoor human detection
    private int _presenceThreshold = 150;      // Default: 1.50°C (more sensitive than 200)
    private int _motionThreshold = 150;        // Default: 1.50°C
    private int _ambientShockThreshold = 200;  // Default: 2.00°C
    
    // Hysteresis (in 0.01°C units) - Prevents false triggering oscillations
    private int _presenceHysteresis = 50;      // Default: 0.50°C
    private int _motionHysteresis = 50;        // Default: 0.50°C
    private int _ambientShockHysteresis = 50;  // Default: 0.50°C

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

        // Parse hysteresis (in 0.01°C units, e.g. 50 = 0.50°C)
        if (config.TryGetValue("presenceHysteresis", out string? presHystStr) && int.TryParse(presHystStr, out int presHyst))
            _presenceHysteresis = presHyst;
        
        if (config.TryGetValue("motionHysteresis", out string? motHystStr) && int.TryParse(motHystStr, out int motHyst))
            _motionHysteresis = motHyst;
        
        if (config.TryGetValue("ambientShockHysteresis", out string? ambHystStr) && int.TryParse(ambHystStr, out int ambHyst))
            _ambientShockHysteresis = ambHyst;

        CustomLogger.Log(this, CustomLogger.LogLevel.Info, $"STHS34PF80: Config - UpdateRate: {_updateRateHz} Hz, Range: {_detectionRangeMeters} m");
        CustomLogger.Log(this, CustomLogger.LogLevel.Info, $"STHS34PF80: Thresholds - Presence: {_presenceThreshold/100.0:F2}°C, Motion: {_motionThreshold/100.0:F2}°C, Shock: {_ambientShockThreshold/100.0:F2}°C");
        CustomLogger.Log(this, CustomLogger.LogLevel.Info, $"STHS34PF80: Hysteresis - Presence: {_presenceHysteresis/100.0:F2}°C, Motion: {_motionHysteresis/100.0:F2}°C, Shock: {_ambientShockHysteresis/100.0:F2}°C");

        // ── Hard reset ─────────────────────────────────────────────────────────
        // The BOOT bit is only reliable when the sensor is in power-down (ODR = 0).
        // Sequence: force power-down → BOOT → poll until cleared → flush pipeline.

        // 1. Force power-down (ODR = 0, keep BDU bit if set)
        var ctrl1Current = I2C.ReadReg(REG_CTRL1, token);
        I2C.WriteReg(REG_CTRL1, (byte)(ctrl1Current & 0xF0), token);
        Thread.Sleep(20); // allow the current conversion to finish

        // 2. Issue BOOT (CTRL2 bit 7): reloads all registers from OTP and clears
        //    the internal algorithm accumulators
        CustomLogger.Log(this, CustomLogger.LogLevel.Info, "STHS34PF80: Issuing BOOT (power-down mode)");
        I2C.WriteReg(REG_CTRL2, 0x80, token);

        // 3. Poll until the BOOT bit self-clears (< 2 ms typical, 500 ms timeout)
        var bootTimeout = DateTime.UtcNow.AddMilliseconds(500);
        while (DateTime.UtcNow < bootTimeout)
        {
            Thread.Sleep(5);
            if ((I2C.ReadReg(REG_CTRL2, token) & 0x80) == 0)
            {
                CustomLogger.Log(this, CustomLogger.LogLevel.Info, "STHS34PF80: BOOT complete");
                break;
            }
        }

        // 4. Flush the output data pipeline: dummy-read all output registers so
        //    stale values from the previous run cannot surface on the first ReadOnce()
        Thread.Sleep(10);
        I2C.ReadReg(REG_STATUS, token);
        I2C.ReadRegShort(REG_TAMBIENT_L, I2C.ByteOrder.LittleEndian, token);
        I2C.ReadRegShort(REG_TOBJECT_L,  I2C.ByteOrder.LittleEndian, token);
        I2C.ReadRegShort(REG_TPRESENCE_L, I2C.ByteOrder.LittleEndian, token);
        I2C.ReadRegShort(REG_TMOTION_L,  I2C.ByteOrder.LittleEndian, token);
        I2C.ReadRegShort(REG_TAMB_SHOCK_L, I2C.ByteOrder.LittleEndian, token);

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

        CustomLogger.Log(this, CustomLogger.LogLevel.Info, "STHS34PF80: Configuring averaging and filters");
        
        // Configure averaging (AVG_TRIM register - normal register, direct access)
        // bits 2:0 = avg_tmos (0=2, 1=8, 2=32, 3=128, 4=256, 5=512, 6=1024, 7=2048)
        // bits 4:3 = avg_t (0=8, 1=4, 2=2, 3=1)
        // Using AVG_TMOS_8 = 1, AVG_T_8 = 0 for faster response and better stability
        byte avgTrim = (byte)((0 << 3) | 1); // avg_t=8, avg_tmos=8
        I2C.WriteReg(REG_AVG_TRIM, avgTrim, token);
        
        // Configure low-pass filters (LPF1, LPF2 - normal registers, direct access)
        // lpf_m=0, lpf_p_m=0, lpf_p=0, lpf_a_t=3 → ODR/9 for motion and presence (fast
        // response), ODR/100 for ambient temperature (changes very slowly).
        // Note: ODR/50 was previously used for all filters but was far too sluggish
        // (0.3 Hz at 15 Hz ODR), causing near-zero presence/motion readings.
        // LPF1: bits 2:0 = lpf_m, bits 6:4 = lpf_p_m
        byte lpf1 = (byte)((0 << 4) | 0); // ODR/9 for motion and presence-motion
        I2C.WriteReg(REG_LPF1, lpf1, token);
        
        // LPF2: bits 2:0 = lpf_p, bits 6:4 = lpf_a_t
        byte lpf2 = (byte)((3 << 4) | 0); // lpf_a_t=ODR/100 (ambient changes slowly), lpf_p=ODR/9
        I2C.WriteReg(REG_LPF2, lpf2, token);

        CustomLogger.Log(this, CustomLogger.LogLevel.Info, "STHS34PF80: Configuring embedded algorithm registers + resetting algorithm");

        // AN5867 §7.4: all embedded register writes (thresholds, algo config, RESET_ALGO)
        // must happen in a SINGLE FUNC_CFG_ACCESS session. The ODR is restored only once
        // at the very end — that rising ODR edge is what actually triggers the IIR reset.
        byte[] presThsBytes = BitConverter.GetBytes((ushort)_presenceThreshold);
        byte[] motThsBytes  = BitConverter.GetBytes((ushort)_motionThreshold);
        byte[] ambThsBytes  = BitConverter.GetBytes((ushort)_ambientShockThreshold);
        if (!BitConverter.IsLittleEndian)
        {
            Array.Reverse(presThsBytes);
            Array.Reverse(motThsBytes);
            Array.Reverse(ambThsBytes);
        }
        byte algoConfig = 0x00; // comp_type=0, sel_abs=0
        // comp_type=1 (ambient compensation) overcorrects during the convergence period
        // after a clean RESET_ALGO: the compensation model starts at zero and effectively
        // subtracts the presence signal, resulting in near-zero TPRESENCE for several seconds.

        WriteEmbeddedRegs(
            token,
            (EMB_PRESENCE_THS,  presThsBytes),
            (EMB_MOTION_THS,    motThsBytes),
            (EMB_TAMB_SHOCK_THS, ambThsBytes),
            (EMB_ALGO_CONFIG,   new[] { algoConfig }),
            (EMB_RESET_ALGO,    new byte[] { 0x01 })   // must be last, triggers reset on ODR restore
        );

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

    protected override PresenceMeasurement ReadOnceInternal(int TimeoutMs = 1000, CancellationToken token = default)
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
        // Using hysteresis to prevent oscillations: once detected, value must drop below (threshold - hysteresis) to clear
        // Note: We use absolute value for comparison since signatures can be negative or positive
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

    /// <summary>
    /// Writes multiple embedded function registers in a single FUNC_CFG_ACCESS session
    /// (AN5867 §7.4). ODR is saved, set to 0, all registers written, then ODR restored
    /// once at the end — the rising ODR edge triggers any pending RESET_ALGO.
    /// </summary>
    private void WriteEmbeddedRegs(CancellationToken token, params (byte addr, byte[] data)[] entries)
    {
        // 1. Save current ODR and enter power-down
        var ctrl1 = I2C.ReadReg(REG_CTRL1, token);
        var savedOdr = (byte)(ctrl1 & 0x0F);
        I2C.WriteReg(REG_CTRL1, (byte)(ctrl1 & 0xF0), token);
        Thread.Sleep(10);

        // 2. Enable embedded function access
        var ctrl2 = I2C.ReadReg(REG_CTRL2, token);
        I2C.WriteReg(REG_CTRL2, (byte)(ctrl2 | 0x10), token);

        // 3. Enable write mode
        I2C.WriteReg(REG_PAGE_RW, 0x40, token);

        // 4. Write all registers (address auto-increments after each FUNC_CFG_DATA write)
        foreach (var (addr, data) in entries)
        {
            I2C.WriteReg(REG_FUNC_CFG_ADDR, addr, token);
            foreach (var b in data)
                I2C.WriteReg(REG_FUNC_CFG_DATA, b, token);
        }

        // 5. Disable write mode
        I2C.WriteReg(REG_PAGE_RW, 0x00, token);

        // 6. Disable embedded function access
        ctrl2 = I2C.ReadReg(REG_CTRL2, token);
        I2C.WriteReg(REG_CTRL2, (byte)(ctrl2 & ~0x10), token);

        // 7. Restore ODR — this rising edge triggers RESET_ALGO if it was written
        ctrl1 = I2C.ReadReg(REG_CTRL1, token);
        I2C.WriteReg(REG_CTRL1, (byte)((ctrl1 & 0xF0) | savedOdr), token);
    }

    /// <summary>
    /// Read from embedded function registers using the func_cfg access sequence.
    /// Based on ST's sths34pf80_func_cfg_read() implementation.
    /// </summary>
    /// <param name="embeddedRegAddress">Embedded register address</param>
    /// <param name="length">Number of bytes to read</param>
    /// <param name="token">Cancellation token</param>
    /// <returns>Data bytes read</returns>
    private byte[] ReadEmbeddedReg(byte embeddedRegAddress, int length, CancellationToken token = default)
    {
        // 1. Save current ODR and enter Power-Down mode
        var ctrl1 = I2C.ReadReg(REG_CTRL1, token);
        var savedOdr = (byte)(ctrl1 & 0x0F);
        I2C.WriteReg(REG_CTRL1, (byte)(ctrl1 & 0xF0), token);
        
        Thread.Sleep(10);

        // 2. Enable access to embedded functions
        var ctrl2 = I2C.ReadReg(REG_CTRL2, token);
        I2C.WriteReg(REG_CTRL2, (byte)(ctrl2 | 0x10), token);
        
        // 3. Enable read mode (PAGE_RW bit 5 = FUNC_CFG_READ)
        I2C.WriteReg(REG_PAGE_RW, 0x20, token);
        
        // 4. Read each byte from embedded register
        var result = new byte[length];
        for (int i = 0; i < length; i++)
        {
            I2C.WriteReg(REG_FUNC_CFG_ADDR, (byte)(embeddedRegAddress + i), token);
            result[i] = I2C.ReadReg(REG_FUNC_CFG_DATA, token);
        }
        
        // 5. Disable read mode
        I2C.WriteReg(REG_PAGE_RW, 0x00, token);
        
        // 6. Disable access to embedded functions
        ctrl2 = I2C.ReadReg(REG_CTRL2, token);
        I2C.WriteReg(REG_CTRL2, (byte)(ctrl2 & ~0x10), token);
        
        // 7. Restore ODR
        ctrl1 = I2C.ReadReg(REG_CTRL1, token);
        I2C.WriteReg(REG_CTRL1, (byte)((ctrl1 & 0xF0) | savedOdr), token);
        
        return result;
    }
}