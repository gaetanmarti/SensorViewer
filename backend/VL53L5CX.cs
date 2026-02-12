// Time-of-Flight distance sensor VL53L5CX from STMicroelectronics
// Multizone ranging sensor with up to 8x8 zones (64 zones)

// Hookup Guide: https://learn.sparkfun.com/tutorials/qwiic-tof-imager---vl53l5cx-hookup-guide
// Datasheet: https://www.st.com/resource/en/datasheet/vl53l5cx.pdf
// User manual: https://www.st.com/resource/en/user_manual/um2884-a-guide-to-using-the-vl53l5cx-multizone-timeofflight-ranging-sensor-with-wide-field-of-view-ultralite-driver-uld-stmicroelectronics.pdf
// Software integration: https://github.com/sparkfun/SparkFun_VL53L5CX_Arduino_Library/blob/main/documents/um2887-software-integration-guide.pdf
// GitHub driver: https://github.com/stm32duino/VL53L5CX

namespace immensive;

public class VL53L5CX : II2CDistanceSensor
{
    // VL53L5CX Register addresses
    private const byte REG_DEVICE_ID = 0x00;
    private const byte REG_I2C_SLAVE_ADDR = 0x04;
    private const ushort REG_UI_CMD_STATUS = 0x2C00;
    private const ushort REG_UI_CMD_START = 0x2C04;
    private const ushort REG_UI_CMD_END = 0x2FFF;
    
    // Device identification
    private const byte DEVICE_ID = 0xF0;
    
    // Resolution modes
    public enum Resolution : byte
    {
        Res4x4 = 16,  // 4x4 zones = 16 zones
        Res8x8 = 64   // 8x8 zones = 64 zones
    }

    // Ranging modes
    public enum RangingMode : byte
    {
        Continuous = 0x01,
        Autonomous = 0x03
    }

    // Power modes
    public enum PowerMode : byte
    {
        Sleep = 0x00,
        Wakeup = 0x01
    }

    // Firmware states
    private enum FirmwareState
    {
        NotLoaded,  // Firmware not in RAM (needs full download)
        Crashed,    // Firmware in RAM but crashed (needs MCU reset only)
        Ready       // Firmware running and ready
    }

    // Default configuration values
    private const Resolution DefaultResolution = Resolution.Res4x4;
    private const byte DefaultRangingFrequencyHz = 15; // Hz (max 15 for 8x8, 60 for 4x4)
    private const uint DefaultIntegrationTimeMs = 20; // ms

    // Default timeouts (ms)
    public int CommandTimeoutMs { get; set; } = 1000;
    public int InitTimeoutMs { get; set; } = 2000;

    private const ushort VL53L5CX_DCI_ZONE_CONFIG = 0x5450;
    private const ushort VL53L5CX_DCI_FREQ_HZ = 0x5458;
    private const ushort VL53L5CX_DCI_INT_TIME = 0x545C;
    private const ushort VL53L5CX_DCI_FW_NB_TARGET = 0x5478;
    private const ushort VL53L5CX_DCI_RANGING_MODE = 0xAD30;
    private const ushort VL53L5CX_DCI_DSS_CONFIG = 0xAD38;
    private const ushort VL53L5CX_DCI_SINGLE_RANGE = 0xCD5C;
    private const ushort VL53L5CX_DCI_PIPE_CONTROL = 0xCF78;

    private const ushort VL53L5CX_OFFSET_BUFFER_SIZE = 488;
    private const ushort VL53L5CX_XTALK_BUFFER_SIZE = 776;
    private const ushort VL53L5CX_NVM_DATA_SIZE = 492;
    private const int VL53L5CX_TEMP_BUFFER_SIZE = 1024;

    private Resolution _currentResolution = DefaultResolution;
    private byte _rangingFrequencyHz = DefaultRangingFrequencyHz;
    private RangingMode _rangingMode = RangingMode.Autonomous;
    private bool _isRanging = false;
    private ushort _currentBank = 0xFFFF; // Track current bank (0xFFFF = unknown)

    private readonly byte[] _offsetData = new byte[VL53L5CX_OFFSET_BUFFER_SIZE];
    private readonly byte[] _xtalkData = new byte[VL53L5CX_XTALK_BUFFER_SIZE];
    private readonly byte[] _tempBuffer = new byte[VL53L5CX_TEMP_BUFFER_SIZE];
    
    // Lock object for thread-safe initialization
    private static readonly object _initLock = new();

    protected override I2C.TransferMode PreferredTransferMode => I2C.TransferMode.WriteRead;

    public VL53L5CX(int address = 0x29) : base(address)
    {
        Name = "VL53L5CX Time-of-Flight Sensor";
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

            // Check device ID (VL53L5CX uses 16-bit register addressing)
            SetBank(0x00, token);
            byte deviceId = ReadReg16(0x0000, token);
            if (deviceId != DEVICE_ID && deviceId != 0x00) // 0x00 might be returned before sensor is fully booted
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

    /// <summary>
    /// Set memory bank if different from current. Avoids unnecessary I2C writes.
    /// </summary>
    private void SetBank(ushort bank, CancellationToken token = default)
    {
        if (_currentBank != bank)
        {
            WriteReg16(0x7FFF, (byte)bank, token);
            _currentBank = bank;
        }
    }

    private Specifications _specifications4x4 = new(4, 4, 15, 45, 45); // 4x4, 15Hz, 45° x 45° FoV
    private Specifications _specifications8x8 = new(8, 8, 15, 45, 45); // 8x8, 15Hz, 45° x 45° FoV

    public override Specifications CurrentSpecifications() => _currentResolution switch
    {
        Resolution.Res4x4 => _specifications4x4,
        Resolution.Res8x8 => _specifications8x8,
        _ => _specifications4x4
    };

    /// <summary>
    /// Initializes the VL53L5CX sensor with the specified configuration.
    /// </summary>
    /// <param name="config">A dictionary containing configuration parameters.
    /// Supported keys:
    /// - "resolution": "4x4" or "8x8" (default: "4x4")
    /// - "frequencyHz": Ranging frequency in Hz (default: 15, max 15 for 8x8, 60 for 4x4)
    /// - "integrationTimeMs": Integration time in milliseconds (default: 20)
    /// </param>
    /// <param name="busId">The I2C bus ID. If -1, the default bus is used.</param>
    /// <param name="token">Optional cancellation token.</param>
    public override void Initialize(Dictionary<string, string> config, int busId = -1, CancellationToken token = default)
    {
        // Protect against concurrent initialization
        lock (_initLock)
        {
            // If already initialized, skip
            if (Initialized)
            {
                CustomLogger.Log(this, CustomLogger.LogLevel.Info, "VL53L5CX already initialized, skipping.");
                return;
            }
            
            base.Initialize(config, busId, token);
            Initialized = false;

            // Parse configuration
            _currentResolution = config.TryGetValue("resolution", out string? resValue) && resValue == "8x8" 
                ? Resolution.Res8x8 
                : Resolution.Res4x4;

            _rangingFrequencyHz = config.TryGetValue("frequencyHz", out string? freqValue) 
            ? byte.Parse(freqValue) 
            : DefaultRangingFrequencyHz;

        uint integrationTimeMs = config.TryGetValue("integrationTimeMs", out string? intValue) 
            ? uint.Parse(intValue) 
            : DefaultIntegrationTimeMs;

        _rangingMode = config.TryGetValue("rangingMode", out string? modeValue)
            && modeValue.Equals("continuous", StringComparison.OrdinalIgnoreCase)
            ? RangingMode.Continuous
            : RangingMode.Autonomous;

        // Validate frequency based on resolution
        byte maxFreq = _currentResolution == Resolution.Res4x4 ? (byte)60 : (byte)15;
        if (_rangingFrequencyHz > maxFreq)
        {
            CustomLogger.Log(this, CustomLogger.LogLevel.Warning, 
                $"Frequency {_rangingFrequencyHz}Hz exceeds max {maxFreq}Hz for resolution {_currentResolution}. Clamping to {maxFreq}Hz.");
            _rangingFrequencyHz = maxFreq;
        }

        // Update specifications with actual frequency
        if (_currentResolution == Resolution.Res4x4)
            _specifications4x4 = _specifications4x4 with { UpdateRateHz = _rangingFrequencyHz };
        else
            _specifications8x8 = _specifications8x8 with { UpdateRateHz = _rangingFrequencyHz };

        // Check if firmware is already loaded BEFORE any reset (reset would erase it from RAM)
        // This is useful when re-initializing without power cycling the sensor
        FirmwareState firmwareState = CheckFirmwareState(token);
        
        if (firmwareState == FirmwareState.Ready)
        {
            CustomLogger.Log(this, CustomLogger.LogLevel.Info, 
                "VL53L5CX firmware already loaded and running, skipping reset and download.");
            CustomLogger.Log(this, CustomLogger.LogLevel.Info, 
                "Proceeding directly to DCI configuration (offset, xtalk, default config).");
            // Firmware is already running - proceed directly to DCI configuration
            // Do NOT call EnableFirmwareAccess or reset, as it would disrupt the running firmware
        }
        else if (firmwareState == FirmwareState.Crashed)
        {
            // Firmware in RAM but crashed - reset MCU to restart it
            CustomLogger.Log(this, CustomLogger.LogLevel.Warning, 
                "VL53L5CX firmware crashed, performing MCU reset (fast recovery ~1s).");
            
            ResetMCU(token);
            
            CustomLogger.Log(this, CustomLogger.LogLevel.Info, 
                "MCU reset successful, firmware restarted. Proceeding to DCI configuration.");
        }
        else // FirmwareState.NotLoaded
        {
            // Firmware not loaded - perform full initialization sequence (as per ST's C++ implementation)
            CustomLogger.Log(this, CustomLogger.LogLevel.Info, 
                "VL53L5CX firmware not loaded, performing full initialization sequence.");
            
            // 1. Software reset and boot sequence (ST lines 214-240)
            SoftwareReset(token);

            // 2. Wait for sensor to boot (ST lines 242-243)
            WaitForBoot(token);

            // 3. Enable FW access BEFORE firmware download (ST lines 244-258)
            EnableFirmwareAccess(token);

            // 4. Power ON status configuration (ST lines 261-281)
            ApplyPowerOnStatus(token);

            // 5. Download firmware to sensor (ST lines 281-302)
            if (!DownloadFirmware(token))
            {
                throw new InvalidOperationException("Failed to download firmware to VL53L5CX");
            }
            // Note: DownloadFirmware() includes MCU reset at the end (ST lines 302-313)
        }

        // After firmware is ready (either freshly downloaded or already loaded), DCI operations can proceed
        ReadNvmOffsetData(token);
        SendOffsetData(Resolution.Res4x4, token);
        Array.Copy(VL53L5CXFirmware.VL53L5CX_DEFAULT_XTALK, _xtalkData, _xtalkData.Length);
        SendXtalkData(Resolution.Res4x4, token);
        WriteDefaultConfiguration(token);
        // Note: ApplyPipeControl is NOT called during init in ST's implementation
        // It's done inside Start() via output configuration

        // Note: Default configuration already sets resolution to 4x4
        // Only call SetResolution if user wants a different resolution
        if (_currentResolution != Resolution.Res4x4)
        {
            CustomLogger.Log(this, CustomLogger.LogLevel.Info, 
                $"VL53L5CX: Configuring non-default resolution {_currentResolution}");
            SetResolution(_currentResolution, token);
        }
        else
        {
            CustomLogger.Log(this, CustomLogger.LogLevel.Info, 
                "VL53L5CX: Using default 4x4 resolution from firmware configuration");
        }
        
        // Note: ST's vl53l5cx_init() does NOT call SetRangingMode/SetRangingFrequency/SetIntegrationTime
        // These are user-configurable settings called AFTER init, not during init
        // We use firmware defaults during init, user can change them later if needed
        CustomLogger.Log(this, CustomLogger.LogLevel.Info, 
            $"VL53L5CX: Using firmware defaults (mode={_rangingMode}, freq={_rangingFrequencyHz}Hz, integration={integrationTimeMs}ms)");
        
        // Mark as initialized
        Initialized = true;
        
        // NOTE: ST's vl53l5cx_init() does NOT call vl53l5cx_start_ranging()
        // User must call Start() manually after Initialize()
        // This prevents EnsureWakeup/EnsureHostAccess from disturbing the freshly initialized MCU
        
        CustomLogger.Log(this, CustomLogger.LogLevel.Info, 
            $"VL53L5CX initialized successfully: resolution={_currentResolution}, frequency={_rangingFrequencyHz}Hz");
        CustomLogger.Log(this, CustomLogger.LogLevel.Info, 
            "VL53L5CX: Call Start() to begin ranging");
        }
    }

    private void SoftwareReset(CancellationToken token = default)
    {
        // SW reboot sequence from ST vl53l5cx_init() lines 214-240
        // All these use 16-bit addressing (WrByte in ST = WriteReg16 here)
        SetBank(0x00, token);
        WriteReg16(0x0009, 0x04, token);
        WriteReg16(0x000F, 0x40, token);
        WriteReg16(0x000A, 0x03, token);
        Thread.Sleep(1);
        
        byte tmp = ReadReg16(0x7FFF, token);  // Read bank register (ST line 222)
        WriteReg16(0x000C, 0x01, token);
        
        // Config registers (ST lines 224-231)
        WriteReg16(0x0101, 0x00, token);
        WriteReg16(0x0102, 0x00, token);
        WriteReg16(0x010A, 0x01, token);
        WriteReg16(0x4002, 0x01, token);
        WriteReg16(0x4002, 0x00, token);
        WriteReg16(0x010A, 0x03, token);
        WriteReg16(0x0103, 0x01, token);
        
        // Boot sequence (ST lines 232-240)
        WriteReg16(0x000C, 0x00, token);
        WriteReg16(0x000F, 0x43, token);
        Thread.Sleep(1);
        WriteReg16(0x000F, 0x40, token);
        WriteReg16(0x000A, 0x01, token);
        Thread.Sleep(100);
    }

    private void WaitForBoot(CancellationToken token = default)
    {
        // Wait for sensor booted (ST lines 242-243)
        SetBank(0x00, token);
        PollForAnswer(1, 0, 0x0006, 0xFF, 0x01, InitTimeoutMs, 10, token);
    }

    private void EnableFirmwareAccess(CancellationToken token = default)
    {
        // Enable FW access BEFORE firmware download (ST lines 244-258)
        CustomLogger.Log(this,  CustomLogger.LogLevel.Info, "Enabling FW access before firmware download...");
        
        WriteReg16(0x000E, 0x01, token);
        SetBank(0x02, token);
        
        // Enable FW access command
        WriteReg16(0x0003, 0x0D, token);
        SetBank(0x01, token);
        
        // Poll for firmware ready (0x21 bit 0x10 == 0x10)
        PollForAnswer(1, 0, 0x0021, 0x10, 0x10, InitTimeoutMs, 10, token);
        SetBank(0x00, token);
        
        // Enable host access to GO1
        WriteReg16(0x000C, 0x01, token);
        
        CustomLogger.Log(this, CustomLogger.LogLevel.Info, "FW access enabled successfully");
    }

    private void ApplyPowerOnStatus(CancellationToken token = default)
    {
        // Power ON status configuration (ST lines 261-281)
        SetBank(0x00, token);
        WriteReg16(0x0101, 0x00, token);
        WriteReg16(0x0102, 0x00, token);
        WriteReg16(0x010A, 0x01, token);
        WriteReg16(0x4002, 0x01, token);
        WriteReg16(0x4002, 0x00, token);
        WriteReg16(0x010A, 0x03, token);
        WriteReg16(0x0103, 0x01, token);
        WriteReg16(0x400F, 0x00, token);
        WriteReg16(0x021A, 0x43, token);
        WriteReg16(0x021A, 0x03, token);
        WriteReg16(0x021A, 0x01, token);
        WriteReg16(0x021A, 0x00, token);
        WriteReg16(0x0219, 0x00, token);
        WriteReg16(0x021B, 0x00, token);

        // Wake up MCU (ST lines 274-281)
        SetBank(0x00, token);
        WriteReg16(0x000C, 0x00, token);
        SetBank(0x01, token);
        WriteReg16(0x0020, 0x07, token);
        WriteReg16(0x0020, 0x06, token);
    }

    /// <summary>
    /// Check the current firmware state by reading status registers.
    /// Returns: Ready (firmware running), Crashed (in RAM but crashed), NotLoaded (needs download).
    /// </summary>
    private FirmwareState CheckFirmwareState(CancellationToken token = default)
    {
        try
        {
            // Check if firmware is already running by reading firmware state registers
            // This check is useful when re-initializing without power cycling the sensor
            // Note: After power cycle, firmware is lost (stored in RAM, not flash)
            
            // Register 0x06 in bank 0x00: MCU boot status (bit 0 = boot complete)
            // Register 0x21 in bank 0x01: Firmware status (0x10 = ready, 0x04 = crashed, 0x00 = not loaded)
            
            CustomLogger.Log(this, CustomLogger.LogLevel.Info, "Checking firmware state...");
            
            SetBank(0x00, token);
            byte bootStatus = ReadReg16(0x0006, token);
            CustomLogger.Log(this, CustomLogger.LogLevel.Info, $"Boot status (0x06): 0x{bootStatus:X2}");
            
            if ((bootStatus & 0x01) != 0x01)
            {
                CustomLogger.Log(this, CustomLogger.LogLevel.Info, "MCU not booted yet, firmware not loaded");
                return FirmwareState.NotLoaded;
            }
            
            SetBank(0x01, token);
            byte fwStatus = ReadReg16(0x0021, token);
            CustomLogger.Log(this, CustomLogger.LogLevel.Info, $"Firmware status (0x21): 0x{fwStatus:X2}");
            SetBank(0x00, token);
            
            // Determine state based on firmware status register
            // 0x10 = ready, 0x04 = crashed, 0x00 = not loaded
            if ((fwStatus & 0x10) == 0x10)
            {
                CustomLogger.Log(this, CustomLogger.LogLevel.Info, 
                    "✓ Firmware READY - will skip download");
                return FirmwareState.Ready;
            }
            else if (fwStatus == 0x04)
            {
                CustomLogger.Log(this, CustomLogger.LogLevel.Warning, 
                    "✗ Firmware CRASHED - will reset MCU only (~1s)");
                return FirmwareState.Crashed;
            }
            else
            {
                CustomLogger.Log(this, CustomLogger.LogLevel.Info, 
                    $"✗ Firmware NOT LOADED (status=0x{fwStatus:X2}) - will download (~10s)");
                return FirmwareState.NotLoaded;
            }
        }
        catch (Exception ex)
        {
            CustomLogger.Log(this, CustomLogger.LogLevel.Warning, 
                $"Failed to check firmware status: {ex.Message} - will assume firmware not loaded.");
            return FirmwareState.NotLoaded;
        }
    }

    /// <summary>
    /// Reset MCU to restart firmware that's already in RAM. Much faster than full firmware download.
    /// Used when firmware has crashed (status 0x04) but is still loaded in RAM.
    /// </summary>
    private void ResetMCU(CancellationToken token = default)
    {
        CustomLogger.Log(this, CustomLogger.LogLevel.Info, "Resetting MCU to restart firmware in RAM...");
        
        // MCU reset sequence (same as end of DownloadFirmware)
        SetBank(0x00, token);
        WriteReg16(0x0114, 0x00, token);
        WriteReg16(0x0115, 0x00, token);
        WriteReg16(0x0116, 0x42, token);
        WriteReg16(0x0117, 0x00, token);
        WriteReg16(0x000B, 0x00, token);
        WriteReg16(0x000C, 0x00, token);
        WriteReg16(0x000B, 0x01, token);
        
        // Wait a bit for MCU to reset
        CustomLogger.Log(this, CustomLogger.LogLevel.Info, "Waiting for MCU to reset...");
        Thread.Sleep(100);
        
        // Verify firmware is back to ready state (0x10)
        // Note: After crash, polling for 0x06==0x00 doesn't work reliably,
        // so we just wait and verify firmware status instead
        SetBank(0x01, token);
        
        int startTime = Environment.TickCount;
        bool fwReady = false;
        while (Environment.TickCount - startTime < InitTimeoutMs)
        {
            token.ThrowIfCancellationRequested();
            byte fwStatus = ReadReg16(0x0021, token);
            
            if ((fwStatus & 0x10) == 0x10)
            {
                CustomLogger.Log(this, CustomLogger.LogLevel.Info, 
                    $"Firmware ready after reset (status=0x{fwStatus:X2})");
                fwReady = true;
                break;
            }
            
            Thread.Sleep(50);
        }
        
        SetBank(0x00, token);
        
        if (!fwReady)
        {
            throw new TimeoutException("VL53L5CX: Firmware did not become ready after MCU reset");
        }
        
        CustomLogger.Log(this, CustomLogger.LogLevel.Info, "MCU reset complete, firmware restarted successfully");
    }

    private void SetResolution(Resolution resolution, CancellationToken token = default)
    {
        CustomLogger.Log(this, CustomLogger.LogLevel.Info, $"VL53L5CX: Setting resolution to {resolution}");

        if (resolution == Resolution.Res4x4)
        {
            DciReadData(_tempBuffer, VL53L5CX_DCI_DSS_CONFIG, 16, token);
            _tempBuffer[0x04] = 64;
            _tempBuffer[0x06] = 64;
            _tempBuffer[0x09] = 4;
            DciWriteData(_tempBuffer.AsSpan(0, 16).ToArray(), VL53L5CX_DCI_DSS_CONFIG, token);

            DciReadData(_tempBuffer, VL53L5CX_DCI_ZONE_CONFIG, 8, token);
            _tempBuffer[0x00] = 4;
            _tempBuffer[0x01] = 4;
            _tempBuffer[0x04] = 8;
            _tempBuffer[0x05] = 8;
            DciWriteData(_tempBuffer.AsSpan(0, 8).ToArray(), VL53L5CX_DCI_ZONE_CONFIG, token);
        }
        else
        {
            DciReadData(_tempBuffer, VL53L5CX_DCI_DSS_CONFIG, 16, token);
            _tempBuffer[0x04] = 16;
            _tempBuffer[0x06] = 16;
            _tempBuffer[0x09] = 1;
            DciWriteData(_tempBuffer.AsSpan(0, 16).ToArray(), VL53L5CX_DCI_DSS_CONFIG, token);

            DciReadData(_tempBuffer, VL53L5CX_DCI_ZONE_CONFIG, 8, token);
            _tempBuffer[0x00] = 8;
            _tempBuffer[0x01] = 8;
            _tempBuffer[0x04] = 4;
            _tempBuffer[0x05] = 4;
            DciWriteData(_tempBuffer.AsSpan(0, 8).ToArray(), VL53L5CX_DCI_ZONE_CONFIG, token);
        }

        SendOffsetData(resolution, token);
        SendXtalkData(resolution, token);

        _currentResolution = resolution;
    }

    private void SetRangingFrequency(byte frequencyHz, CancellationToken token = default)
    {
        CustomLogger.Log(this, CustomLogger.LogLevel.Info, $"VL53L5CX: Setting ranging frequency to {frequencyHz}Hz");

        DciReadData(_tempBuffer, VL53L5CX_DCI_FREQ_HZ, 4, token);
        _tempBuffer[0x01] = frequencyHz;
        DciWriteData(_tempBuffer.AsSpan(0, 4).ToArray(), VL53L5CX_DCI_FREQ_HZ, token);

        _rangingFrequencyHz = frequencyHz;
    }

    private void SetIntegrationTime(uint timeMs, CancellationToken token = default)
    {
        CustomLogger.Log(this, CustomLogger.LogLevel.Info, $"VL53L5CX: Setting integration time to {timeMs}ms");

        uint timeUs = timeMs * 1000;
        byte[] intTimeData = BitConverter.GetBytes(timeUs);
        DciWriteData(intTimeData, VL53L5CX_DCI_INT_TIME, token);
    }

    private void SetRangingMode(RangingMode mode, CancellationToken token = default)
    {
        uint singleRange = 0x00;

        DciReadData(_tempBuffer, VL53L5CX_DCI_RANGING_MODE, 8, token);

        switch (mode)
        {
            case RangingMode.Continuous:
                _tempBuffer[0x01] = 0x01;
                _tempBuffer[0x03] = 0x03;
                singleRange = 0x00;
                break;
            case RangingMode.Autonomous:
                _tempBuffer[0x01] = 0x03;
                _tempBuffer[0x03] = 0x02;
                singleRange = 0x01;
                break;
            default:
                throw new InvalidOperationException("VL53L5CX: Invalid ranging mode");
        }

        DciWriteData(_tempBuffer.AsSpan(0, 8).ToArray(), VL53L5CX_DCI_RANGING_MODE, token);
        DciWriteData(BitConverter.GetBytes(singleRange), VL53L5CX_DCI_SINGLE_RANGE, token);
    }

    public void Stop(CancellationToken token = default)
    {
        // Stop ranging command
        CustomLogger.Log(this, CustomLogger.LogLevel.Info, "VL53L5CX: Stopping ranging");
        
        SetBank(0x00, token);
        WriteReg16(0x0009, 0x04, token);
        
        // Poll for command completion
        SetBank(0x00, token);
        int startTime = Environment.TickCount;
        while (Environment.TickCount - startTime < CommandTimeoutMs)
        {
            byte[] status = ReadMultipleBytes(0x2C00, 4, token);
            if ((status[1] & 0x03) == 0x03)
                break;
            Thread.Sleep(10);
        }
        
        SetBank(0x00, token);
        _isRanging = false;
    }

    private void EnsureWakeup(CancellationToken token = default)
    {
        // Wakeup sequence from ST's vl53l5cx_set_power_mode (WAKEUP)
        SetBank(0x00, token);
        byte bootStatus = ReadReg16(0x0006, token);
        CustomLogger.Log(this, CustomLogger.LogLevel.Info,
            $"VL53L5CX: Pre-wakeup boot status=0x{bootStatus:X2}");

        WriteReg16(0x0009, 0x04, token);

        int startTime = Environment.TickCount;
        bool ready = false;
        while (Environment.TickCount - startTime < CommandTimeoutMs)
        {
            token.ThrowIfCancellationRequested();
            bootStatus = ReadReg16(0x0006, token);
            if ((bootStatus & 0x01) == 0x01)
            {
                ready = true;
                break;
            }
            Thread.Sleep(10);
        }

        if (!ready)
        {
            CustomLogger.Log(this, CustomLogger.LogLevel.Warning,
                $"VL53L5CX: Wakeup timeout, boot status=0x{bootStatus:X2}");
        }
    }

    private void EnsureHostAccess(CancellationToken token = default)
    {
        // Enable host access to GO1 (ST init sequence)
        SetBank(0x00, token);
        WriteReg16(0x000C, 0x01, token);
        byte access = ReadReg16(0x000C, token);
        CustomLogger.Log(this, CustomLogger.LogLevel.Info,
            $"VL53L5CX: Host access 0x000C=0x{access:X2}");
    }

    private void ReadNvmOffsetData(CancellationToken token = default)
    {
        SetBank(0x02, token);
        WriteReg16(0x2FD8, VL53L5CXFirmware.VL53L5CX_GET_NVM_CMD, token);
        // Note: PollForAnswer reads in current bank (0x02), as per ST's C++ implementation
        PollForAnswer(4, 0, REG_UI_CMD_STATUS, 0xFF, 0x02, 2000, 10, token);

        byte[] nvm = ReadMultipleBytes(REG_UI_CMD_START, VL53L5CX_NVM_DATA_SIZE, token);
        Array.Copy(nvm, _offsetData, Math.Min(_offsetData.Length, nvm.Length));
    }

    private void SendOffsetData(Resolution resolution, CancellationToken token = default)
    {
        byte[] footer = [0x00, 0x00, 0x00, 0x0F, 0x03, 0x01, 0x01, 0xE4];
        byte[] dss4x4 = [0x0F, 0x04, 0x04, 0x00, 0x08, 0x10, 0x10, 0x07];

        // Set bank to 0x02 for UI command region
        SetBank(0x02, token);

        Array.Copy(_offsetData, _tempBuffer, VL53L5CX_OFFSET_BUFFER_SIZE);

        if (resolution == Resolution.Res4x4)
        {
            Array.Copy(dss4x4, 0, _tempBuffer, 0x10, dss4x4.Length);
            SwapBuffer(_tempBuffer, VL53L5CX_OFFSET_BUFFER_SIZE);

            uint[] signalGrid = new uint[64];
            short[] rangeGrid = new short[64];

            Buffer.BlockCopy(_tempBuffer, 0x3C, signalGrid, 0, signalGrid.Length * sizeof(uint));
            Buffer.BlockCopy(_tempBuffer, 0x140, rangeGrid, 0, rangeGrid.Length * sizeof(short));

            for (int j = 0; j < 4; j++)
            {
                for (int i = 0; i < 4; i++)
                {
                    int baseIdx = (2 * i) + (16 * j);
                    signalGrid[i + (4 * j)] =
                        (signalGrid[baseIdx] + signalGrid[baseIdx + 1]
                         + signalGrid[baseIdx + 8] + signalGrid[baseIdx + 9]) / 4;

                    rangeGrid[i + (4 * j)] = (short)
                        ((rangeGrid[baseIdx] + rangeGrid[baseIdx + 1]
                          + rangeGrid[baseIdx + 8] + rangeGrid[baseIdx + 9]) / 4);
                }
            }

            Buffer.BlockCopy(signalGrid, 0, _tempBuffer, 0x3C, signalGrid.Length * sizeof(uint));
            Buffer.BlockCopy(rangeGrid, 0, _tempBuffer, 0x140, rangeGrid.Length * sizeof(short));
            SwapBuffer(_tempBuffer, VL53L5CX_OFFSET_BUFFER_SIZE);
        }
        else
        {
            SwapBuffer(_tempBuffer, VL53L5CX_OFFSET_BUFFER_SIZE);
        }

        for (int k = 0; k < VL53L5CX_OFFSET_BUFFER_SIZE - 4; k++)
        {
            _tempBuffer[k] = _tempBuffer[k + 8];
        }

        Array.Copy(footer, 0, _tempBuffer, 0x1E0, footer.Length);

        WriteReg16(0x2E18, _tempBuffer.AsSpan(0, VL53L5CX_OFFSET_BUFFER_SIZE).ToArray(), token);
        // Note: Bank 0x02 already active (reads in current bank)
        CustomLogger.Log(this, CustomLogger.LogLevel.Info, "SendOffsetData: Polling for command completion...");
        PollForAnswer(4, 1, REG_UI_CMD_STATUS, 0xFF, 0x03, 2000, 10, token);
        CustomLogger.Log(this, CustomLogger.LogLevel.Info, "SendOffsetData: Command completed successfully");
    }

    private void SendXtalkData(Resolution resolution, CancellationToken token = default)
    {
        byte[] res4x4 = [0x0F, 0x04, 0x04, 0x17, 0x08, 0x10, 0x10, 0x07];
        byte[] dss4x4 = [0x00, 0x78, 0x00, 0x08, 0x00, 0x00, 0x00, 0x08];
        byte[] profile4x4 = [0xA0, 0xFC, 0x01, 0x00];

        // Set bank to 0x02 for UI command region
        SetBank(0x02, token);

        Array.Copy(_xtalkData, _tempBuffer, VL53L5CX_XTALK_BUFFER_SIZE);

        // Data extrapolation is required for 4x4 Xtalk (as per ST's implementation)
        if (resolution == Resolution.Res4x4)
        {
            Array.Copy(res4x4, 0, _tempBuffer, 0x08, res4x4.Length);
            Array.Copy(dss4x4, 0, _tempBuffer, 0x20, dss4x4.Length);

            SwapBuffer(_tempBuffer, VL53L5CX_XTALK_BUFFER_SIZE);

            // Extract and process signal grid
            uint[] signalGrid = new uint[64];
            Buffer.BlockCopy(_tempBuffer, 0x34, signalGrid, 0, signalGrid.Length * sizeof(uint));

            // Average signal grid for 4x4
            for (int j = 0; j < 4; j++)
            {
                for (int i = 0; i < 4; i++)
                {
                    int baseIdx = (2 * i) + (16 * j);
                    signalGrid[i + (4 * j)] =
                        (signalGrid[baseIdx] + signalGrid[baseIdx + 1]
                         + signalGrid[baseIdx + 8] + signalGrid[baseIdx + 9]) / 4;
                }
            }

            // Clear remaining grid values
            Array.Clear(signalGrid, 0x10, 48); // 48 = 64 - 16 values

            // Copy back signal grid
            Buffer.BlockCopy(signalGrid, 0, _tempBuffer, 0x34, signalGrid.Length * sizeof(uint));
            SwapBuffer(_tempBuffer, VL53L5CX_XTALK_BUFFER_SIZE);

            // Copy profile
            Array.Copy(profile4x4, 0, _tempBuffer, 0x134, profile4x4.Length);
        }
        else
        {
            Array.Clear(_tempBuffer, 0x78, 4);
        }

        CustomLogger.Log(this, CustomLogger.LogLevel.Info, $"SendXtalkData: Writing {VL53L5CX_XTALK_BUFFER_SIZE} bytes to 0x2CF8 (resolution={resolution})");
        WriteReg16(0x2CF8, _tempBuffer.AsSpan(0, VL53L5CX_XTALK_BUFFER_SIZE).ToArray(), token);
        // Note: Bank 0x02 already active (reads in current bank)
        CustomLogger.Log(this, CustomLogger.LogLevel.Info, "SendXtalkData: Polling for command completion...");
        PollForAnswer(4, 1, REG_UI_CMD_STATUS, 0xFF, 0x03, 2000, 10, token);
        CustomLogger.Log(this, CustomLogger.LogLevel.Info, "SendXtalkData: Command completed successfully");
    }

    private void WriteDefaultConfiguration(CancellationToken token = default)
    {
        // Set bank to 0x02 for UI command region
        SetBank(0x02, token);

        // Write entire configuration buffer at once (as per ST's implementation)
        // ST writes the full config to 0x2c34 in one transaction, not in segments
        CustomLogger.Log(this, CustomLogger.LogLevel.Info, 
            $"WriteDefaultConfiguration: Writing {VL53L5CXFirmware.VL53L5CX_DEFAULT_CONFIGURATION.Length} bytes to 0x2C34");
        WriteReg16(0x2C34, VL53L5CXFirmware.VL53L5CX_DEFAULT_CONFIGURATION, token);

        // Note: Bank 0x02 already active (reads in current bank)
        CustomLogger.Log(this, CustomLogger.LogLevel.Info, "WriteDefaultConfiguration: Polling for command completion...");
        PollForAnswer(4, 1, REG_UI_CMD_STATUS, 0xFF, 0x03, 2000, 10, token);
        CustomLogger.Log(this, CustomLogger.LogLevel.Info, "WriteDefaultConfiguration: Command completed successfully");
    }

    private void ApplyPipeControl(CancellationToken token = default)
    {
        byte[] pipeCtrl = [VL53L5CXFirmware.VL53L5CX_FW_NBTAR_RANGING, 0x00, 0x01, 0x00];
        DciWriteData(pipeCtrl, VL53L5CX_DCI_PIPE_CONTROL, token);

        uint singleRange = 0x01;
        DciWriteData(BitConverter.GetBytes(singleRange), VL53L5CX_DCI_SINGLE_RANGE, token);
    }

    /// <summary>
    /// Poll for answer - reads address in CURRENT bank (does NOT change bank).
    /// Bank must be set correctly BEFORE calling this method (like in ST's C++ implementation).
    /// </summary>
    private void PollForAnswer(byte size, byte pos, ushort address, byte mask, byte expectedValue, int timeoutMs, int pollDelayMs = 10, CancellationToken token = default)
    {
        int startTime = Environment.TickCount;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            // NOTE: Do NOT change bank here - read in current bank (as per ST's C++ implementation)
            byte[] status = ReadMultipleBytes(address, size, token);
            Array.Copy(status, _tempBuffer, Math.Min(status.Length, _tempBuffer.Length));

            if (size >= 4 && (_tempBuffer[2] & 0x80) == 0x80)
            {
                string statusHex = $"[0x{_tempBuffer[0]:X2}, 0x{_tempBuffer[1]:X2}, 0x{_tempBuffer[2]:X2}, 0x{_tempBuffer[3]:X2}]";
                throw new InvalidOperationException($"VL53L5CX: MCU error, status={statusHex}");
            }

            if ((_tempBuffer[pos] & mask) == expectedValue)
                return;

            if (Environment.TickCount - startTime >= timeoutMs)
            {
                string statusHex = size >= 4
                    ? $"[0x{_tempBuffer[0]:X2}, 0x{_tempBuffer[1]:X2}, 0x{_tempBuffer[2]:X2}, 0x{_tempBuffer[3]:X2}]"
                    : $"[0x{_tempBuffer[0]:X2}]";
                throw new TimeoutException(
                    $"VL53L5CX: Command timeout (addr=0x{address:X4}, pos={pos}, mask=0x{mask:X2}, expected=0x{expectedValue:X2}, status={statusHex})");
            }

            Thread.Sleep(pollDelayMs);
        }
    }

    private void PollForAnswer(byte size, byte pos, ushort address, byte mask, byte expectedValue, CancellationToken token = default)
    {
        PollForAnswer(size, pos, address, mask, expectedValue, 2000, 10, token);
    }

    private void DciReadData(byte[] data, ushort index, ushort dataSize, CancellationToken token = default)
    {
        if (dataSize + 12 > VL53L5CX_TEMP_BUFFER_SIZE)
            throw new InvalidOperationException("VL53L5CX: DCI read buffer too large");

        byte[] cmd = [0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0F, 0x00, 0x02, 0x00, 0x08];
        cmd[0] = (byte)(index >> 8);
        cmd[1] = (byte)(index & 0xFF);
        cmd[2] = (byte)((dataSize & 0xFF0) >> 4);
        cmd[3] = (byte)((dataSize & 0x0F) << 4);

        // CRITICAL: DCI operations work in CURRENT bank (bank 0x02)
        WriteReg16((ushort)(REG_UI_CMD_END - 11), cmd, token);
        // Poll in current bank (bank 0x02)
        PollForAnswer(4, 1, REG_UI_CMD_STATUS, 0xFF, 0x03, 2000, 10, token);

        byte[] buffer = ReadMultipleBytes(REG_UI_CMD_START, dataSize + 12, token);
        SwapBuffer(buffer, buffer.Length);

        Array.Copy(buffer, 4, data, 0, dataSize);
    }

    private void DciReplaceData(byte[] data, ushort index, ushort dataSize, byte[] newData, ushort newDataPos, CancellationToken token = default)
    {
        DciReadData(data, index, dataSize, token);
        Array.Copy(newData, 0, data, newDataPos, newData.Length);
        DciWriteData(data, index, token);
    }

    // Streamcount for data ready checking
    private byte _streamcount = 255;
    private uint _dataReadSize = 0;
    
    // Block header IDX constants (for 1 target per zone)
    private const ushort VL53L5CX_DISTANCE_IDX = 0xD33C;
    private const ushort VL53L5CX_TARGET_STATUS_IDX = 0xD47C;
    private const ushort VL53L5CX_SIGNAL_RATE_IDX = 0xCFBC;
    private const ushort VL53L5CX_AMBIENT_RATE_IDX = 0x54D0;

    // Output block headers (NB_TARGET_PER_ZONE = 1)
    private const uint VL53L5CX_START_BH = 0x0000000D;
    private const uint VL53L5CX_METADATA_BH = 0x54B400C0;
    private const uint VL53L5CX_COMMONDATA_BH = 0x54C00040;
    private const uint VL53L5CX_AMBIENT_RATE_BH = 0x54D00104;
    private const uint VL53L5CX_SPAD_COUNT_BH = 0x55D00404;
    private const uint VL53L5CX_NB_TARGET_DETECTED_BH = 0xCF7C0401;
    private const uint VL53L5CX_SIGNAL_RATE_BH = 0xCFBC0404;
    private const uint VL53L5CX_RANGE_SIGMA_MM_BH = 0xD2BC0402;
    private const uint VL53L5CX_DISTANCE_BH = 0xD33C0402;
    private const uint VL53L5CX_REFLECTANCE_BH = 0xD43C0401;
    private const uint VL53L5CX_TARGET_STATUS_BH = 0xD47C0401;
    private const uint VL53L5CX_MOTION_DETECT_BH = 0xCC5008C0;

    // DCI addresses used by start ranging
    private const ushort VL53L5CX_DCI_OUTPUT_CONFIG = 0xCD60;
    private const ushort VL53L5CX_DCI_OUTPUT_ENABLES = 0xCD68;
    private const ushort VL53L5CX_DCI_OUTPUT_LIST = 0xCD78;
    public void Start(CancellationToken token = default)
    {
        if (!Initialized)
            throw new InvalidOperationException("VL53L5CX: Sensor not initialized");

        if (_isRanging)
        {
            CustomLogger.Log(this, CustomLogger.LogLevel.Info, "VL53L5CX: Already ranging, skipping start.");
            return;
        }

        CustomLogger.Log(this, CustomLogger.LogLevel.Info, "VL53L5CX: Starting ranging");

        try
        {
            // CRITICAL: Ensure bank 0x02 is active for DCI operations
            // ST's vl53l5cx_start_ranging() expects bank 0x02 (set during init and never changed)
            SetBank(0x02, token);
            
            // NOTE: Do NOT call EnsureWakeup/EnsureHostAccess here!
            // ST's vl53l5cx_start_ranging() does not do this - sensor is already awake after init
            // Calling them here perturbs DCI operations and causes timeouts

            // Output list and enables follow ST reference (vl53l5cx_start_ranging)
            uint[] output =
            [
                VL53L5CX_START_BH,
                VL53L5CX_METADATA_BH,
                VL53L5CX_COMMONDATA_BH,
                VL53L5CX_AMBIENT_RATE_BH,
                VL53L5CX_SPAD_COUNT_BH,
                VL53L5CX_NB_TARGET_DETECTED_BH,
                VL53L5CX_SIGNAL_RATE_BH,
                VL53L5CX_RANGE_SIGMA_MM_BH,
                VL53L5CX_DISTANCE_BH,
                VL53L5CX_REFLECTANCE_BH,
                VL53L5CX_TARGET_STATUS_BH,
                VL53L5CX_MOTION_DETECT_BH
            ];

            // Enable mandatory + all optional outputs (ST default)
            // 0x0FFF = 12 bits (outputs 0-11), 0x1FFF would enable invalid bit 12
            uint[] output_bh_enable = [0x00000FFF, 0x00000000, 0x00000000, 0xC0000000];

            byte resolution = (byte)_currentResolution;
            byte nbTargets = VL53L5CXFirmware.VL53L5CX_FW_NBTAR_RANGING;

            uint dataReadSize = 0;
            uint outputListCount = (uint)output.Length;

            for (uint i = 0; i < output.Length; i++)
            {
                if (output[i] == 0)
                    continue;

                uint enableMask = 1u << (int)(i % 32);
                if ((output_bh_enable[i / 32] & enableMask) == 0)
                    continue;

                uint type = output[i] & 0xF;
                uint size = (output[i] >> 4) & 0xFFF;
                uint idx = (output[i] >> 16) & 0xFFFF;

                if (type >= 0x1 && type < 0x0D)
                {
                    if (idx >= 0x54D0 && idx < (0x54D0 + 960))
                    {
                        size = resolution;
                    }
                    else
                    {
                        size = (uint)(resolution * nbTargets);
                    }

                    output[i] = type | (size << 4) | (idx << 16);
                    dataReadSize += type * size;
                }
                else
                {
                    dataReadSize += size;
                }

                dataReadSize += 4; // block header size
            }

            dataReadSize += 20; // footer size

            _dataReadSize = dataReadSize;

            uint[] headerConfig = [dataReadSize, outputListCount + 1];

            CustomLogger.Log(this, CustomLogger.LogLevel.Info,
                $"VL53L5CX: Writing DCI outputs, dataReadSize={dataReadSize}, outputListCount={outputListCount}, output_bh_enable[0]=0x{output_bh_enable[0]:X8}");

            DciWriteData(UIntArrayToBytes(output), VL53L5CX_DCI_OUTPUT_LIST, token);
            DciWriteData(UIntArrayToBytes(headerConfig), VL53L5CX_DCI_OUTPUT_CONFIG, token);
            DciWriteData(UIntArrayToBytes(output_bh_enable), VL53L5CX_DCI_OUTPUT_ENABLES, token);
            
            // Verify firmware is still ready after DCI writes
            CustomLogger.Log(this, CustomLogger.LogLevel.Info, "VL53L5CX: Verifying firmware status after DCI writes...");
            SetBank(0x01, token);
            byte fwStatus = ReadReg16(0x0021, token);
            CustomLogger.Log(this, CustomLogger.LogLevel.Info, $"VL53L5CX: Firmware status after DCI writes: 0x{fwStatus:X2}");
            
            if ((fwStatus & 0x10) != 0x10)
            {
                throw new InvalidOperationException($"VL53L5CX: Firmware not ready after DCI writes (status=0x{fwStatus:X2})");
            }

            // Start xshut bypass (interrupt mode) - ST's sequence
            SetBank(0x00, token);
            WriteReg16(0x0009, 0x05, token);
            
            // Small delay to let xshut bypass take effect
            Thread.Sleep(10);
            
            SetBank(0x02, token);

            // Verify UI command region is accessible before sending ranging command
            CustomLogger.Log(this, CustomLogger.LogLevel.Info, "VL53L5CX: Checking UI command status before ranging command...");
            byte[] preStatus = ReadMultipleBytes(0x2C00, 4, token);
            CustomLogger.Log(this, CustomLogger.LogLevel.Info, 
                $"VL53L5CX: Pre-ranging UI status: [0x{preStatus[0]:X2}, 0x{preStatus[1]:X2}, 0x{preStatus[2]:X2}, 0x{preStatus[3]:X2}]");

            // Start ranging session (UI command region 0x2C00-0x2FFF is in bank 0x02)
            // NOTE: Stay in bank 0x02 - do NOT switch to bank 0x00 here!
            byte[] cmd = [0x00, 0x03, 0x00, 0x00];
            CustomLogger.Log(this, CustomLogger.LogLevel.Info, "VL53L5CX: Writing ranging start command to 0x2FFC...");
            WriteReg16(0x2FFC, cmd, token);

            // Poll for command accepted (in bank 0x02 where we already are)
            CustomLogger.Log(this, CustomLogger.LogLevel.Info, "VL53L5CX: Polling for ranging command acceptance...");
            PollForAnswer(4, 1, 0x2C00, 0xFF, 0x03, CommandTimeoutMs, 10, token);

            _streamcount = 255;
            _isRanging = true;

            CustomLogger.Log(this, CustomLogger.LogLevel.Info, "VL53L5CX: Ranging started successfully");
        }
        catch (Exception ex)
        {
            CustomLogger.Log(this, CustomLogger.LogLevel.Error, $"VL53L5CX: Error in Start(): {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Swap buffer bytes for endianness (VL53L5CX uses big-endian)
    /// </summary>
    private void SwapBuffer(byte[] buffer, int size)
    {
        for (int i = 0; i < size; i += 4)
        {
            if (i + 3 < size)
            {
                byte tmp = buffer[i];
                buffer[i] = buffer[i + 3];
                buffer[i + 3] = tmp;
                tmp = buffer[i + 1];
                buffer[i + 1] = buffer[i + 2];
                buffer[i + 2] = tmp;
            }
        }
    }

    private static bool IsAllZero(byte[] buffer)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            if (buffer[i] != 0)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Check if new ranging data is ready (ST's vl53l5cx_check_data_ready)
    /// </summary>
    private bool CheckDataReady(CancellationToken token = default)
    {
        // Read 4 bytes from address 0x0 in current bank (bank 0x02 for ranging data)
        // ST's C++: RdMulti(&(p_dev->platform), 0x0, p_dev->temp_buffer, 4)
        SetBank(0x02, token);
        byte[] statusBytes = ReadMultipleBytes(0x0000, 4, token);

        // ST's condition:
        // (temp_buffer[0] != streamcount) && (temp_buffer[0] != 255)
        // && (temp_buffer[1] == 0x5)
        // && ((temp_buffer[2] & 0x5) == 0x5)
        // && ((temp_buffer[3] & 0x10) == 0x10)
        
        if ((statusBytes[0] != _streamcount)
            && (statusBytes[0] != 255)
            && (statusBytes[1] == 0x05)
            && ((statusBytes[2] & 0x05) == 0x05)
            && ((statusBytes[3] & 0x10) == 0x10))
        {
            _streamcount = statusBytes[0];
            return true;
        }

        return false;
    }

    private void WaitForMeasurement(int timeoutMs, CancellationToken token = default)
    {
        int startTime = Environment.TickCount;
        while (Environment.TickCount - startTime < timeoutMs)
        {
            token.ThrowIfCancellationRequested();
            if (CheckDataReady(token))
                return;
            Thread.Sleep(5);
        }
        throw new TimeoutException("VL53L5CX: Measurement timeout");
    }

    public override List<(int distMM, float confidence)> ReadOnce(int TimeoutMs = 1000, CancellationToken token = default)
    {
        if (!Initialized)
            throw new InvalidOperationException("VL53L5CX: Sensor not initialized");

        // Auto-start ranging if not already started (ST pattern: user calls start manually, but we auto-start for simplicity)
        if (!_isRanging)
        {
            CustomLogger.Log(this, CustomLogger.LogLevel.Info, "VL53L5CX: Auto-starting ranging for ReadOnce()");
            Start(token);
        }

        int dataSize = _dataReadSize > 0 ? (int)_dataReadSize : 1024;
        byte[] buffer = new byte[dataSize];
        int startTime = Environment.TickCount;

        for (int attempt = 0; attempt < 2; attempt++)
        {
            int elapsed = Environment.TickCount - startTime;
            int remaining = TimeoutMs - elapsed;
            if (remaining <= 0)
                throw new TimeoutException("VL53L5CX: Measurement timeout");

            CustomLogger.Log(this, CustomLogger.LogLevel.Info, "VL53L5CX: ReadOnce - waiting for measurement");
            WaitForMeasurement(remaining, token);

            SetBank(0x02, token);
            buffer = ReadMultipleBytes(0x0000, dataSize, token);
            SwapBuffer(buffer, dataSize);

            if (!IsAllZero(buffer))
                break;

            CustomLogger.Log(this, CustomLogger.LogLevel.Warning,
                "VL53L5CX: ReadOnce - empty frame (all zeros), retrying");
        }

        int numZones = (int)_currentResolution;
        List<(int distMM, float confidence)> results = new(numZones);

        short[] distances = new short[numZones];
        byte[] targetStatuses = new byte[numZones];

        int i = 16; // Skip metadata
        while (i < dataSize - 4)
        {
            token.ThrowIfCancellationRequested();

            uint blockHeader = BitConverter.ToUInt32(buffer, i);
            uint type = blockHeader & 0xF;
            uint size = (blockHeader >> 4) & 0xFFF;
            uint idx = (blockHeader >> 16) & 0xFFFF;

            if (type == 0 || type >= 0x0D)
                break;

            int msize = (type >= 1 && type < 0x0D) ? (int)(type * size) : (int)size;

            if (idx == VL53L5CX_DISTANCE_IDX)
            {
                int dataOffset = i + 4;
                for (int z = 0; z < numZones && dataOffset + 1 < dataSize; z++)
                {
                    distances[z] = BitConverter.ToInt16(buffer, dataOffset);
                    dataOffset += 2;
                }
            }
            else if (idx == VL53L5CX_TARGET_STATUS_IDX)
            {
                int dataOffset = i + 4;
                for (int z = 0; z < numZones && dataOffset < dataSize; z++)
                {
                    targetStatuses[z] = buffer[dataOffset];
                    dataOffset += 1;
                }
            }

            i += msize + 4;
        }

        for (int z = 0; z < numZones; z++)
        {
            int distMM = distances[z] / 4;
            if (distMM < 0)
                distMM = 0;

            float confidence = (targetStatuses[z] == 5 || targetStatuses[z] == 9) ? 1.0f : 0.0f;
            results.Add((distMM, confidence));
        }

        return results;
    }

    private void DciWriteData(byte[] data, ushort index, CancellationToken token = default)
    {
        ushort dataSize = (ushort)data.Length;
        if (dataSize + 12 > VL53L5CX_TEMP_BUFFER_SIZE)
            throw new InvalidOperationException("VL53L5CX: DCI write buffer too large");

        byte[] headers = new byte[4];
        headers[0] = (byte)(index >> 8);
        headers[1] = (byte)(index & 0xFF);
        headers[2] = (byte)((dataSize & 0xFF0) >> 4);
        headers[3] = (byte)((dataSize & 0x0F) << 4);

        byte[] footer =
        [
            0x00, 0x00, 0x00, 0x0F, 0x05, 0x01,
            (byte)((dataSize + 8) >> 8),
            (byte)((dataSize + 8) & 0xFF)
        ];

        byte[] payload = new byte[data.Length];
        Array.Copy(data, payload, data.Length);
        SwapBuffer(payload, payload.Length);

        Array.Copy(headers, 0, _tempBuffer, 0, headers.Length);
        Array.Copy(payload, 0, _tempBuffer, 4, payload.Length);
        Array.Copy(footer, 0, _tempBuffer, 4 + payload.Length, footer.Length);

        ushort address = (ushort)(REG_UI_CMD_END - (dataSize + 12) + 1);
        
        CustomLogger.Log(this, CustomLogger.LogLevel.Info, 
            $"DciWriteData: index=0x{index:X4}, dataSize={dataSize}, address=0x{address:X4}");
        
        // CRITICAL FIX: DCI operations work in bank 0x02 (NOT bank 0x00)
        // ST's vl53l5cx_dci_write_data() does NOT change bank - uses current bank (0x02)
        // Bank 0x02 must be active before calling DciWriteData (set by caller)
        // DO NOT call SetBank here - it causes DCI operations to fail!
        WriteReg16(address, _tempBuffer.AsSpan(0, dataSize + 12).ToArray(), token);
        
        // Poll in current bank (bank 0x02) - ST's code does NOT change bank
        CustomLogger.Log(this, CustomLogger.LogLevel.Info, 
            "DciWriteData: Polling for command completion...");
        PollForAnswer(4, 1, REG_UI_CMD_STATUS, 0xFF, 0x03, 2000, 10, token);
        CustomLogger.Log(this, CustomLogger.LogLevel.Info, 
            "DciWriteData: Command completed successfully");
    }

    private static byte[] UIntArrayToBytes(uint[] values)
    {
        byte[] buffer = new byte[values.Length * 4];
        Buffer.BlockCopy(values, 0, buffer, 0, buffer.Length);
        return buffer;
    }

    // Helper methods for 16-bit register access (VL53L5CX uses 16-bit addresses)
    private void WriteReg16(ushort reg, byte value, CancellationToken token = default)
    {
        byte[] buffer = [(byte)((reg >> 8) & 0xFF), (byte)(reg & 0xFF), value];
        I2C.WriteBytes(buffer, 1, token);
    }

    /// <summary>
    /// Write multiple bytes to a 16-bit register address
    /// </summary>
    private void WriteReg16(ushort reg, byte[] data, CancellationToken token = default)
    {
        byte[] buffer = new byte[2 + data.Length];
        buffer[0] = (byte)((reg >> 8) & 0xFF);
        buffer[1] = (byte)(reg & 0xFF);
        Array.Copy(data, 0, buffer, 2, data.Length);
        I2C.WriteBytes(buffer, 1, token);
    }

    private byte ReadReg16(ushort reg, CancellationToken token = default)
    {
        byte[] addrBuffer = [(byte)((reg >> 8) & 0xFF), (byte)(reg & 0xFF)];
        return I2C.ReadWithPointer(addrBuffer, 1, 1, token)[0];
    }
    
    /// <summary>
    /// Read multiple bytes from a 16-bit register address in a single I2C transaction
    /// </summary>
    private byte[] ReadMultipleBytes(ushort reg, int count, CancellationToken token = default)
    {
        byte[] addrBuffer = [(byte)((reg >> 8) & 0xFF), (byte)(reg & 0xFF)];
        return I2C.ReadWithPointer(addrBuffer, count, 1, token);
    }

    // NOTE: Firmware download implementation would go here
    // The VL53L5CX requires uploading ~84KB of firmware during initialization
    // This firmware is provided by ST Microelectronics:
    // - Can be extracted from: https://github.com/stm32duino/VL53L5CX/blob/main/src/vl53l5cx_firmware.c
    // - Or obtained from ST's website
    //
    private bool DownloadFirmware(CancellationToken token = default)
    {
        // Check if firmware is available
        if (!VL53L5CXFirmware.IsFirmwareAvailable())
        {
            CustomLogger.Log(this, CustomLogger.LogLevel.Error, 
                "ERROR: Firmware data not loaded. Please populate VL53L5CXFirmware.cs with data from vl53l5cx_buffers.h");
            return false;
        }

        CustomLogger.Log(this, CustomLogger.LogLevel.Info, "Starting firmware download (~10 seconds)...");

        try
        {
            // Set memory bank for firmware download
            CustomLogger.Log(this, CustomLogger.LogLevel.Info, "Setting memory bank to 0x09 for firmware chunk 1");
            SetBank(0x09, token);
            
            // Download firmware in 3 chunks as done by ST's driver
            // Chunk 1: 0x8000 bytes at address 0x0000
            CustomLogger.Log(this, CustomLogger.LogLevel.Info, "Downloading firmware chunk 1/3 (32KB)...");
            byte[] chunk1 = new byte[0x8000];
            Array.Copy(VL53L5CXFirmware.VL53L5CX_FIRMWARE, 0, chunk1, 0, 0x8000);
            // Write firmware chunk 1 in 32-byte segments
            for (int offset = 0; offset < chunk1.Length; offset += 32)
            {
                int size = Math.Min(32, chunk1.Length - offset);
                byte[] segment = new byte[size];
                Array.Copy(chunk1, offset, segment, 0, size);
                WriteReg16((ushort)offset, segment, token);
            }
            CustomLogger.Log(this, CustomLogger.LogLevel.Info, "Firmware chunk 1 downloaded successfully");
            
            token.ThrowIfCancellationRequested();
            
            // Chunk 2: 0x8000 bytes at address 0x8000
            CustomLogger.Log(this, CustomLogger.LogLevel.Info, "Setting memory bank to 0x0A for firmware chunk 2");
            SetBank(0x0A, token);
            CustomLogger.Log(this, CustomLogger.LogLevel.Info, "Downloading firmware chunk 2/3 (32KB)...");
            byte[] chunk2 = new byte[0x8000];
            Array.Copy(VL53L5CXFirmware.VL53L5CX_FIRMWARE, 0x8000, chunk2, 0, 0x8000);
            // Write firmware chunk 2 in 32-byte segments
            for (int offset = 0; offset < chunk2.Length; offset += 32)
            {
                int size = Math.Min(32, chunk2.Length - offset);
                byte[] segment = new byte[size];
                Array.Copy(chunk2, offset, segment, 0, size);
                WriteReg16((ushort)offset, segment, token);
            }
            CustomLogger.Log(this, CustomLogger.LogLevel.Info, "Firmware chunk 2 downloaded successfully");
            
            token.ThrowIfCancellationRequested();
            
            // Chunk 3: 0x5000 bytes at address 0x10000
            CustomLogger.Log(this, CustomLogger.LogLevel.Info, "Setting memory bank to 0x0B for firmware chunk 3");
            SetBank(0x0B, token);
            CustomLogger.Log(this, CustomLogger.LogLevel.Info, "Downloading firmware chunk 3/3 (20KB)...");
            byte[] chunk3 = new byte[0x5000];
            Array.Copy(VL53L5CXFirmware.VL53L5CX_FIRMWARE, 0x10000, chunk3, 0, 0x5000);
            // Write firmware chunk 3 in 32-byte segments
            for (int offset = 0; offset < chunk3.Length; offset += 32)
            {
                int size = Math.Min(32, chunk3.Length - offset);
                byte[] segment = new byte[size];
                Array.Copy(chunk3, offset, segment, 0, size);
                WriteReg16((ushort)offset, segment, token);
            }
            CustomLogger.Log(this, CustomLogger.LogLevel.Info, "Firmware chunk 3 downloaded successfully");
            
            // Switch to bank 0x01 after firmware download (as per ST line 295)
            SetBank(0x01, token);
            
            // Check if firmware correctly downloaded (as per ST's implementation lines 297-302)
            CustomLogger.Log(this, CustomLogger.LogLevel.Info, "Verifying firmware download...");
            SetBank(0x02, token);
            Thread.Sleep(5);  // Small delay after bank switch
            WriteReg16(0x0003, 0x0D, token);  // Verification command
            SetBank(0x01, token);
            Thread.Sleep(5);  // Small delay after bank switch
            
            // Poll for firmware ready bit (0x21 bit 0x10 == 0x10)
            // Note: This checks if firmware was correctly loaded into RAM
            PollForAnswer(1, 0, 0x0021, 0x10, 0x10, InitTimeoutMs, 10, token);
            CustomLogger.Log(this, CustomLogger.LogLevel.Info, "Firmware verification successful - firmware loaded correctly");
            
            // Switch to bank 0x00 after verification (as per ST line 302)
            SetBank(0x00, token);
            
            // Enable host access to GO1
            WriteReg16(0x000C, 0x01, token);  // Host access command
            
            // Reset MCU to start the firmware
            CustomLogger.Log(this, CustomLogger.LogLevel.Info, "Resetting MCU to start firmware...");
            WriteReg16(0x0114, 0x00, token);
            WriteReg16(0x0115, 0x00, token);
            WriteReg16(0x0116, 0x42, token);
            WriteReg16(0x0117, 0x00, token);
            WriteReg16(0x000B, 0x00, token);
            WriteReg16(0x000C, 0x00, token);
            WriteReg16(0x000B, 0x01, token);
            
            CustomLogger.Log(this, CustomLogger.LogLevel.Info, "Waiting for MCU reset (register 0x06 should become 0x00)");
            // Wait for MCU to enter reset state (register 0x06 should become 0x00)
            // Note: Bank 0x00 already active, do NOT change bank in loop
            try
            {
                int startTime = Environment.TickCount;
                int iterations = 0;
                while (Environment.TickCount - startTime < InitTimeoutMs)
                {
                    token.ThrowIfCancellationRequested();
                    // Note: Do NOT change bank - already in bank 0x00
                    byte bootStatus = ReadReg16(0x0006, token);
                    iterations++;
                    
                    if (iterations <= 5 || iterations % 50 == 0)
                    {
                        CustomLogger.Log(this, CustomLogger.LogLevel.Info, 
                            $"MCU reset check #{iterations}: 0x06=0x{bootStatus:X2}");
                    }
                    
                    if (bootStatus == 0x00)
                    {
                        CustomLogger.Log(this, CustomLogger.LogLevel.Info, 
                            $"MCU entered reset state after {iterations} iterations");
                        break;
                    }
                    
                    Thread.Sleep(10);
                }
            }
            catch (Exception ex)
            {
                CustomLogger.Log(this, CustomLogger.LogLevel.Error,
                    $"ERROR: MCU reset timeout ({ex.Message})");
                return false;
            }
            
            // Switch to bank 0x02 (as per ST's C++ implementation)
            // Firmware is now ready for DCI operations
            SetBank(0x02, token);

            CustomLogger.Log(this, CustomLogger.LogLevel.Info, "Firmware download and MCU reset successful - firmware is ready");
            return true;
        }
        catch (OperationCanceledException)
        {
            CustomLogger.Log(this, CustomLogger.LogLevel.Warning, "Firmware download cancelled");
            return false;
        }
        catch (Exception ex)
        {
            CustomLogger.Log(this, CustomLogger.LogLevel.Error, $"ERROR downloading firmware: {ex.Message}");
            return false;
        }
    }
}
