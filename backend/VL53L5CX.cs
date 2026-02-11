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

    // Default configuration values
    private const Resolution DefaultResolution = Resolution.Res4x4;
    private const byte DefaultRangingFrequencyHz = 15; // Hz (max 15 for 8x8, 60 for 4x4)
    private const uint DefaultIntegrationTimeMs = 20; // ms

    // Default timeouts (ms)
    public int CommandTimeoutMs { get; set; } = 1000;
    public int InitTimeoutMs { get; set; } = 100;

    private Resolution _currentResolution = DefaultResolution;
    private byte _rangingFrequencyHz = DefaultRangingFrequencyHz;
    private bool _isRanging = false;
    
    // Lock object for thread-safe initialization
    private static readonly object _initLock = new();

    public VL53L5CX(int address = 0x29) : base(address)
    {
        Name = "VL53L5CX Time-of-Flight Sensor";
    }

    public override bool TryDetect(int busId, CancellationToken token = default)
    {
        try
        {
            I2C = new I2C(busId, Address);
            if (!I2C.Ping(token))
            {
                Reset();
                return false;
            }

            // Check device ID
            byte deviceId = I2C.ReadReg(REG_DEVICE_ID, token);
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
        bool firmwareAlreadyLoaded = IsFirmwareLoaded(token);
        
        if (firmwareAlreadyLoaded)
        {
            CustomLogger.Log(this, CustomLogger.LogLevel.Info, 
                "VL53L5CX firmware already loaded, skipping reset and download.");
        }
        else
        {
            // Software reset and boot sequence
            SoftwareReset(token);
            
            // Wait for sensor to boot
            WaitForBoot(token);

            // Download firmware to sensor
            if (!DownloadFirmware(token))
            {
                throw new InvalidOperationException("Failed to download firmware to VL53L5CX");
            }
        }

        // Configure sensor settings
        SetResolution(_currentResolution, token);
        SetRangingFrequency(_rangingFrequencyHz, token);
        SetIntegrationTime(integrationTimeMs, token);
        
        // Mark as initialized BEFORE starting (Start() checks Initialized flag)
        Initialized = true;
        
        // Start continuous ranging (CRITICAL: start once and leave running)
        Start(token);
        
        CustomLogger.Log(this, CustomLogger.LogLevel.Info, 
            $"VL53L5CX initialized: resolution={_currentResolution}, frequency={_rangingFrequencyHz}Hz");
        }
    }

    private void SoftwareReset(CancellationToken token = default)
    {
        // Software reset sequence from VL53L5CX API
        WriteReg16(0x7fff, 0x00, token);
        WriteReg16(0x0009, 0x04, token);
        WriteReg16(0x000F, 0x40, token);
        WriteReg16(0x000A, 0x03, token);
        Thread.Sleep(1); // Wait 1ms
        WriteReg16(0x0009, 0x00, token);
        WriteReg16(0x000F, 0x43, token);
        Thread.Sleep(1);
        WriteReg16(0x000F, 0x40, token);
        WriteReg16(0x000A, 0x01, token);
        Thread.Sleep(100); // Wait 100ms for boot
    }

    private void WaitForBoot(CancellationToken token = default)
    {
        int startTime = Environment.TickCount;
        while (Environment.TickCount - startTime < InitTimeoutMs)
        {
            token.ThrowIfCancellationRequested();
            WriteReg16(0x7fff, 0x00, token);
            byte status = ReadReg16(0x06, token);
            if (status == 0x01)
                return;
            Thread.Sleep(1);
        }
        throw new TimeoutException("VL53L5CX: Sensor boot timeout");
    }

    private bool IsFirmwareLoaded(CancellationToken token = default)
    {
        try
        {
            // Check if firmware is already running by reading firmware state register
            // Register 0x06 bit 0 = MCU boot complete
            // Register 0x21 bit 4 = firmware ready
            WriteReg16(0x7FFF, 0x00, token);
            byte bootStatus = ReadReg16(0x06, token);
            
            if ((bootStatus & 0x01) != 0x01)
                return false;
            
            WriteReg16(0x7FFF, 0x01, token);
            byte fwStatus = ReadReg16(0x21, token);
            WriteReg16(0x7FFF, 0x00, token);
            
            // Firmware is loaded if bit 4 is set
            bool isLoaded = (fwStatus & 0x10) == 0x10;
            
            if (isLoaded)
            {
                CustomLogger.Log(this, CustomLogger.LogLevel.Info, 
                    "VL53L5CX firmware already loaded, skipping download.");
            }
            
            return isLoaded;
        }
        catch
        {
            return false;
        }
    }

    private void SetResolution(Resolution resolution, CancellationToken token = default)
    {
        CustomLogger.Log(this, CustomLogger.LogLevel.Info, $"VL53L5CX: Setting resolution to {resolution}");
        
        // Write resolution via DCI (Direct Configuration Interface)
        // Resolution is configured at register 0x5450 (VL53L5CX_DCI_ZONE_CONFIG)
        byte rows = resolution == Resolution.Res4x4 ? (byte)4 : (byte)8;
        byte cols = resolution == Resolution.Res4x4 ? (byte)4 : (byte)8;
        
        // Read current DCI data (8 bytes)
        WriteReg16(0x7fff, 0x01, token);
        byte[] dciData = ReadMultipleBytes(0x5450, 8, token);
        
        // Update resolution values
        dciData[0] = cols;
        dciData[1] = rows;
        
        // Write back
        WriteReg16(0x5450, dciData, token);
        WriteReg16(0x7fff, 0x00, token);
        
        _currentResolution = resolution;
    }

    private void SetRangingFrequency(byte frequencyHz, CancellationToken token = default)
    {
        CustomLogger.Log(this, CustomLogger.LogLevel.Info, $"VL53L5CX: Setting ranging frequency to {frequencyHz}Hz");
        
        // Write frequency via DCI at register 0x5458 (VL53L5CX_DCI_FREQ_HZ)
        WriteReg16(0x7fff, 0x01, token);
        byte[] freqData = ReadMultipleBytes(0x5458, 4, token);
        freqData[1] = frequencyHz; // Frequency is at offset 1
        WriteReg16(0x5458, freqData, token);
        WriteReg16(0x7fff, 0x00, token);
        
        _rangingFrequencyHz = frequencyHz;
    }

    private void SetIntegrationTime(uint timeMs, CancellationToken token = default)
    {
        CustomLogger.Log(this, CustomLogger.LogLevel.Info, $"VL53L5CX: Setting integration time to {timeMs}ms");
        
        // Write integration time via DCI at register 0x545C (VL53L5CX_DCI_INT_TIME)
        // Integration time is stored as microseconds (timeMs * 1000)
        uint timeUs = timeMs * 1000;
        byte[] intTimeData = BitConverter.GetBytes(timeUs);
        
        WriteReg16(0x7fff, 0x01, token);
        WriteReg16(0x545C, intTimeData, token);
        WriteReg16(0x7fff, 0x00, token);
    }

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
        
        // Configure output (what data blocks to send)
        // output_bh_enable bits: bit 0 = distance, bit 4 = target_status
        ushort output_bh_enable = 0x11; // Enable distance and target_status
        
        // Write output configuration header (8 bytes of zeros)
        byte[] header_config = new byte[8];
        WriteReg16(0x2FD8, header_config, token);
        
        // Write streamcount
        WriteReg16(0x2FE0, _streamcount, token);
        
        // Write footer with output enable flags
        byte[] footer = [
            0x00, 0x00, 0x00, 0x0F, 0x05, 0x01,
            (byte)((output_bh_enable >> 8) & 0xFF),
            (byte)(output_bh_enable & 0xFF),
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        ];
        WriteReg16(0x2FE1, footer, token);
        
        // Start ranging command
        WriteReg16(0x7fff, 0x00, token);
        I2C.WriteReg(0x09, 0x04, token); // 0x04 = start command (NOT 0x05!)
        WriteReg16(0x7fff, 0x02, token);
        
        // Poll for MCU ready (bit 0 of register 0x2C00)
        WriteReg16(0x7fff, 0x01, token);
        int startTime = Environment.TickCount;
        while (Environment.TickCount - startTime < CommandTimeoutMs)
        {
            token.ThrowIfCancellationRequested();
            byte status = ReadReg16(0x2C00, token);
            if ((status & 0x01) == 0x01)
                break;
            Thread.Sleep(10);
        }
        
        WriteReg16(0x7fff, 0x00, token);
        _streamcount = 255; // Reset streamcount for new ranging session
        _isRanging = true;
    }

    public void Stop(CancellationToken token = default)
    {
        // Stop ranging command
        CustomLogger.Log(this, CustomLogger.LogLevel.Info, "VL53L5CX: Stopping ranging");
        
        WriteReg16(0x7fff, 0x00, token);
        I2C.WriteReg(0x09, 0x04, token);
        
        // Poll for command completion
        WriteReg16(0x7fff, 0x01, token);
        int startTime = Environment.TickCount;
        while (Environment.TickCount - startTime < CommandTimeoutMs)
        {
            byte[] status = ReadMultipleBytes(0x2C00, 4, token);
            if ((status[1] & 0x03) == 0x03)
                break;
            Thread.Sleep(10);
        }
        
        WriteReg16(0x7fff, 0x00, token);
        _isRanging = false;
    }

    // Streamcount for data ready checking
    private byte _streamcount = 255;
    
    // Block header IDX constants (for 1 target per zone)
    private const ushort VL53L5CX_DISTANCE_IDX = 0xD33C;
    private const ushort VL53L5CX_TARGET_STATUS_IDX = 0xD47C;
    private const ushort VL53L5CX_SIGNAL_RATE_IDX = 0xCFBC;
    private const ushort VL53L5CX_AMBIENT_RATE_IDX = 0x54D0;
    
    private bool CheckDataReady(CancellationToken token = default)
    {
        // Read 4 bytes at address 0x0 to check data ready status (in single I2C transaction)
        byte[] statusBytes = ReadMultipleBytes(0x0000, 4, token);
        
        // Check conditions from ST's vl53l5cx_check_data_ready:
        // - streamcount changed (statusBytes[0])
        // - statusBytes[1] == 0x5
        // - (statusBytes[2] & 0x5) == 0x5
        // - (statusBytes[3] & 0x10) == 0x10
        if (statusBytes[0] != _streamcount 
            && statusBytes[0] != 255
            && statusBytes[1] == 0x5
            && (statusBytes[2] & 0x5) == 0x5
            && (statusBytes[3] & 0x10) == 0x10)
        {
            _streamcount = statusBytes[0];
            return true;
        }
        
        return false;
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
            
        if (!_isRanging)
            throw new InvalidOperationException("VL53L5CX: Sensor not in ranging mode. Call Start() first.");

        // Wait for data ready (sensor is already ranging continuously)
        WaitForMeasurement(TimeoutMs, token);

        // Calculate data size based on resolution
        // Simplified: use fixed size for now (enough for distance + status)
        // Full implementation would calculate based on enabled outputs
        int dataSize = 1024; // Sufficient for results
        
        // Read data from address 0x0 in a single I2C transaction
        byte[] buffer = ReadMultipleBytes(0x0000, dataSize, token);
        
        // Swap buffer for endianness
        SwapBuffer(buffer, dataSize);
        
        // Parse results
        int numZones = (int)_currentResolution;
        List<(int distMM, float confidence)> results = new(numZones);
        
        // Arrays to store parsed data
        short[] distances = new short[numZones];
        byte[] targetStatuses = new byte[numZones];
        
        // Parse block headers starting at offset 16 (skip metadata)
        for (int i = 16; i < dataSize - 4; i += 4)
        {
            token.ThrowIfCancellationRequested();
            
            // Read block header (4 bytes = 32 bits)
            uint blockHeader = BitConverter.ToUInt32(buffer, i);
            
            // Extract fields from block header
            uint type = blockHeader & 0xF;           // 4 bits
            uint size = (blockHeader >> 4) & 0xFFF;  // 12 bits  
            uint idx = (blockHeader >> 16) & 0xFFFF; // 16 bits
            
            // Skip if invalid block
            if (type == 0 || type >= 0x0D)
                break;
                
            // Calculate memory size for this block
            int msize;
            if (type >= 1 && type < 0x0D)
            {
                msize = (int)(type * size);
            }
            else
            {
                msize = (int)size;
            }
            
            // Parse data based on IDX
            if (idx == VL53L5CX_DISTANCE_IDX)
            {
                // Distance data (16-bit signed values)
                int dataOffset = i + 4;
                for (int z = 0; z < numZones && dataOffset + 1 < dataSize; z++)
                {
                    distances[z] = BitConverter.ToInt16(buffer, dataOffset);
                    dataOffset += 2;
                }
            }
            else if (idx == VL53L5CX_TARGET_STATUS_IDX)
            {
                // Target status (8-bit values)
                int dataOffset = i + 4;
                for (int z = 0; z < numZones && dataOffset < dataSize; z++)
                {
                    targetStatuses[z] = buffer[dataOffset];
                    dataOffset += 1;
                }
            }
            
            // Move to next block
            i += msize;
        }
        
        // Apply conversions and create result list
        for (int z = 0; z < numZones; z++)
        {
            // Distance must be divided by 4 per ST specification
            int distMM = distances[z] / 4;
            if (distMM < 0)
                distMM = 0;
                
            // Target status 5 or 9 means valid ranging
            float confidence = (targetStatuses[z] == 5 || targetStatuses[z] == 9) ? 1.0f : 0.0f;
            
            results.Add((distMM, confidence));
        }

        return results;
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
        I2C.WriteBytes(addrBuffer, 1, token);
        return I2C.ReadByte(1, token);
    }
    
    /// <summary>
    /// Read multiple bytes from a 16-bit register address in a single I2C transaction
    /// </summary>
    private byte[] ReadMultipleBytes(ushort reg, int count, CancellationToken token = default)
    {
        byte[] addrBuffer = [(byte)((reg >> 8) & 0xFF), (byte)(reg & 0xFF)];
        I2C.WriteBytes(addrBuffer, 1, token);
        
        byte[] data = new byte[count];
        for (int i = 0; i < count; i++)
        {
            data[i] = I2C.ReadByte(1, token);
        }
        return data;
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
            WriteReg16(0x7FFF, 0x09);
            
            // Download firmware in 3 chunks as done by ST's driver
            // Chunk 1: 0x8000 bytes at address 0x0000
            CustomLogger.Log(this, CustomLogger.LogLevel.Info, "Downloading firmware chunk 1/3 (32KB)...");
            byte[] chunk1 = new byte[0x8000];
            Array.Copy(VL53L5CXFirmware.VL53L5CX_FIRMWARE, 0, chunk1, 0, 0x8000);
            // Write firmware chunk 1 using 16-bit addressing
            for (int offset = 0; offset < chunk1.Length; offset += 32)
            {
                int size = Math.Min(32, chunk1.Length - offset);
                byte[] segment = new byte[size];
                Array.Copy(chunk1, offset, segment, 0, size);
                WriteReg16((ushort)offset, segment);
            }
            
            token.ThrowIfCancellationRequested();
            
            // Chunk 2: 0x8000 bytes at address 0x8000
            WriteReg16(0x7FFF, 0x0A);
            CustomLogger.Log(this, CustomLogger.LogLevel.Info, "Downloading firmware chunk 2/3 (32KB)...");
            byte[] chunk2 = new byte[0x8000];
            Array.Copy(VL53L5CXFirmware.VL53L5CX_FIRMWARE, 0x8000, chunk2, 0, 0x8000);
            // Write firmware chunk 2 using 16-bit addressing
            for (int offset = 0; offset < chunk2.Length; offset += 32)
            {
                int size = Math.Min(32, chunk2.Length - offset);
                byte[] segment = new byte[size];
                Array.Copy(chunk2, offset, segment, 0, size);
                WriteReg16((ushort)offset, segment);
            }
            
            token.ThrowIfCancellationRequested();
            
            // Chunk 3: 0x5000 bytes at address 0x10000
            WriteReg16(0x7FFF, 0x0B);
            CustomLogger.Log(this, CustomLogger.LogLevel.Info, "Downloading firmware chunk 3/3 (20KB)...");
            byte[] chunk3 = new byte[0x5000];
            Array.Copy(VL53L5CXFirmware.VL53L5CX_FIRMWARE, 0x10000, chunk3, 0, 0x5000);
            // Write firmware chunk 3 using 16-bit addressing
            for (int offset = 0; offset < chunk3.Length; offset += 32)
            {
                int size = Math.Min(32, chunk3.Length - offset);
                byte[] segment = new byte[size];
                Array.Copy(chunk3, offset, segment, 0, size);
                WriteReg16((ushort)offset, segment);
            }
            
            // Switch back to standard memory bank
            WriteReg16(0x7FFF, 0x01);
            
            // Verify firmware download
            CustomLogger.Log(this, CustomLogger.LogLevel.Info, "Verifying firmware download...");
            WriteReg16(0x7FFF, 0x02);
            WriteReg16(0x0003, 0x0D);
            WriteReg16(0x7FFF, 0x01);
            
            // Poll for firmware ready (bit 4 of register 0x21 should be set)
            bool firmwareReady = false;
            for (int i = 0; i < 100; i++)
            {
                byte status = ReadReg16(0x0021);
                if ((status & 0x10) == 0x10)
                {
                    firmwareReady = true;
                    break;
                }
                Thread.Sleep(10);
                token.ThrowIfCancellationRequested();
            }
            
            if (!firmwareReady)
            {
                CustomLogger.Log(this, CustomLogger.LogLevel.Error, "ERROR: Firmware verification failed");
                return false;
            }
            
            WriteReg16(0x7FFF, 0x00);
            
            // Load default configuration
            CustomLogger.Log(this, CustomLogger.LogLevel.Info, "Loading default configuration...");
            // Write default configuration using 16-bit addressing
            for (int offset = 0; offset < VL53L5CXFirmware.VL53L5CX_DEFAULT_CONFIGURATION.Length; offset += 32)
            {
                int size = Math.Min(32, VL53L5CXFirmware.VL53L5CX_DEFAULT_CONFIGURATION.Length - offset);
                byte[] segment = new byte[size];
                Array.Copy(VL53L5CXFirmware.VL53L5CX_DEFAULT_CONFIGURATION, offset, segment, 0, size);
                WriteReg16((ushort)(0x2C34 + offset), segment);
            }
            
            // Load default Xtalk
            CustomLogger.Log(this, CustomLogger.LogLevel.Info, "Loading default Xtalk...");
            // TODO: Implement Xtalk loading if needed
            
            CustomLogger.Log(this, CustomLogger.LogLevel.Info, "Firmware download successful");
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
