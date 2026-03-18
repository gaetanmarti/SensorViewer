// Time-of-Flight distance sensors TMF882X
// Tested with TMF8820 and TMF8821 (same driver, different SPAD map for different FoV)

// Hookup guide: https://learn.sparkfun.com/tutorials/qwiic-dtof-imager-tmf882x-hookup-guide/all
// Datasheet: https://cdn.sparkfun.com/assets/learn_tutorials/2/2/8/9/TMF882X_DataSheet.pdf
// Host driver communication: https://look.ams-osram.com/m/5bfe4f6d8a09e607/original/TMF882X-Host-Driver-Communication-AN001015.pdf
// Firmware images: 
/*
 TMF8820 / TMF8821
 =================
 
  * Navigate to https://ams-osram.com/tmf8821
  * Scroll down to Software dropdown menu
  * Download TMF882x_Driver_Linux_v3.56.zip
  * Unzip TMF882x_Driver_Linux_v3.56.zip
  * The latest firmware file is tmf8x2x_application_4x4.hex
  
 TMF8828
 =======
 
  * Navigate to https://ams-osr§m.com/tmf8828
  * Scroll down to Software dropdown menu
  * Download TMF882x_Driver_Linux_v3.56.zip
  * Unzip TMF882x_Driver_Linux_v3.56.zip
  * The latest firmware file is tmf8x2x_application_8x8.hex
*/

namespace immensive;

public class TMF882X: II2CDistanceSensor
{
    // TMF882X Register addresses
    private const byte REG_APPID = 0x00;
    private const byte REG_BUILD_VERSION = 0x03; // bit 4 set = short range accuracy supported (§4.2.1)
    private const byte REG_ACTIVE_RANGE = 0x19;  // 0x6E = short, 0x6F = long, 0x00 = not supported (§4.2.2)
    private const byte REG_CMD_STAT = 0x08;
    private const byte REG_INT_STATUS = 0xE1;
    private const byte REG_ENABLE = 0xE0;
    private const byte REG_CONFIG_RESULT = 0x20;

    // When page "common config" is loaded :
    private const byte REG_PERIOD_LSB = 0x24;
    private const byte REG_PERIOD_MSB = 0x25;
    private const byte REG_KILO_ITER_LSB = 0x26;
    private const byte REG_KILO_ITER_MSB = 0x27;
    private const byte REG_SPAD_MAP_ID = 0x34;

    // When the "results" page is active :
    private const byte REG_CONF0 = 0x38;
    private const byte REG_DIST0_LSB = 0x39;
    private const byte REG_DIST0_MSB = 0x3A;
    
    // Commands 
    private const byte CMD_MEASURE = 0x10;
    private const byte CMD_WRITE_CONFIG_PAGE = 0x15;
    private const byte CMD_LOAD_CONFIG_PAGE_COMMON = 0x16;
    private const byte CMD_SHORT_RANGE_ACCURACY = 0x6E; // Switch to short-range mode (§4.2.3)
    private const byte CMD_LONG_RANGE_ACCURACY = 0x6F;  // Switch to long-range mode (§4.2.3)
    private const byte CMD_RESET = 0xFE;
    private const byte CMD_STOP = 0xFF;

    // Bootloader commands (chapter 3)
    private const byte CMD_DOWNLOAD_INIT = 0x14;
    private const byte CMD_W_RAM = 0x41;
    private const byte CMD_SET_ADDR = 0x43;
    private const byte CMD_RAMREMAP_RESET = 0x11;
    private const byte CMD_STAT = 0x08;
    
    public TMF882X(int address = 0x41) : base(address)
    {
        Name = "TMF882X Time-of-Flight Sensor";
    }

    protected override I2C.TransferMode PreferredTransferMode => I2C.TransferMode.WriteThenRead;

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
            // Will generate an exception if device doesn't respond or doesn't have expected registers
            /*void*/ GetAppId();
            return true;
        }
        catch
        {
            Reset();
            return false;
        }
    }

    private readonly Specifications _specifications = new(3, 3, 30, 33, 32);
    public override Specifications CurrentSpecifications() => _specifications;

    public enum AppId : byte
    {
        Bootloader = 0x80, // Bootloader running
        Application = 0x03 // Measurement application running
    }

    // See §4.2 Short Range Accuracy Operation
    public enum RangeMode : byte
    {
        Short = CMD_SHORT_RANGE_ACCURACY, // 0x6E – improved accuracy at close range
        Long  = CMD_LONG_RANGE_ACCURACY,  // 0x6F – default mode at firmware startup
    }

    // See 7.4.1 SPAD Mask and Mode Selection (p.21) for these settings
    const ushort DefaultPeriodMs = 34; // Measurement period in milliseconds
    const ushort DefaultKiloIterations = 550; // Measurement iterations times * 1024

    // See 4.2 Short Range Accuracy Operation
    const bool DefaultShortRange = false; // Set to true to enable short-range accuracy mode (if supported by firmware)

    // Store the current measurement period for frame rate calculation
    private ushort _currentPeriodMs = DefaultPeriodMs;

    // Default timeouts (ms)
    public int CommandTimeoutMs { get; set; } = 1000;
    public int AppIdTimeoutMs { get; set; } = 10;

    /// <summary>
    /// Override FrameRateHz to compute from the measurement period.
    /// Frame rate (Hz) = 1000 / periodMs
    /// </summary>
    public override float FrameRateHz
    {
        get => _currentPeriodMs > 0 ? 1000.0f / _currentPeriodMs : 0.0f;
        protected set { } // Ignore sets, frame rate is determined by period
    }

    /// <summary>
    /// Reads the current measurement period from the device registers.
    /// Returns the period in milliseconds.
    /// Note: This method assumes the device is in Common Config page mode.
    /// </summary>
    public ushort ReadPeriodFromDevice(CancellationToken token = default)
    {
        byte lsb = I2C.ReadReg(REG_PERIOD_LSB, token);
        byte msb = I2C.ReadReg(REG_PERIOD_MSB, token);
        ushort periodMs = (ushort)(lsb | ((ushort)msb << 8));
        _currentPeriodMs = periodMs;
        return periodMs;
    }

    /// <summary>
    /// Gets the current measurement period in milliseconds from the cached value.
    /// </summary>
    public ushort GetPeriodMs() => _currentPeriodMs;

    /// <summary>
    /// Computes the frame rate (Hz) based on the given period in milliseconds.
    /// Formula: frameRateHz = 1000.0 / periodMs
    /// </summary>
    /// <param name="periodMs">The measurement period in milliseconds.</param>
    /// <returns>The computed frame rate in Hz.</returns>
    public static float ComputeFrameRate(ushort periodMs)
    {
        return periodMs > 0 ? 1000.0f / periodMs : 0.0f;
    }

    /// <summary>
    /// Computes the measurement period (system ticks in milliseconds) from the given frame rate.
    /// Formula: periodMs = 1000.0 / frameRateHz
    /// </summary>
    /// <param name="frameRateHz">The desired frame rate in Hz.</param>
    /// <returns>The computed period in milliseconds.</returns>
    public static ushort ComputePeriodFromFrameRate(float frameRateHz)
    {
        if (frameRateHz <= 0)
            return DefaultPeriodMs;
        return (ushort)Math.Round(1000.0f / frameRateHz);
    }

    /// <summary>
    /// Initializes the TMF882X sensor with the specified configuration.
    /// </summary>
    /// <param name="config">A dictionary containing configuration parameters.
    /// Supported keys:
    /// - "periodMs": Measurement period in milliseconds (default: 34)
    /// - "kiloIterations": Measurement iterations times 1024 (default: 550)
    /// - "shortRange": "true" for short-range accuracy mode, "false" for long-range (default: false)
    /// </param>
    /// <param name="busId">The I2C bus ID. If -1, the default bus is used.</param>
    /// <param name="token">Optional cancellation token.</param>
    public override void Initialize(Dictionary<string, string> config, int busId = -1, CancellationToken token = default)
    {
        base.Initialize(config, busId, token);
        Initialized = false; // Will be set to true at the end of initialization if successful

        var appId = GetAppId();
        switch (appId)
        {
            case AppId.Application:
                // Stop any ongoing measurement (if device was already running)        
                I2C.WriteReg(REG_CMD_STAT, CMD_STOP, token);
                WaitCmdDone(token: token);
                break;
            case AppId.Bootloader:
                // Download bootloader and reset in APP mode
                DownloadImage(token);
                break;
            default:
                throw new Exception($"TMF882X: unexpected APPID=0x{appId:X2}.");
        }

        var periodMs = config.TryGetValue("periodMs", out string? value) ? ushort.Parse(value) : DefaultPeriodMs;
        var kiloIterations = config.TryGetValue("kiloIterations", out value) ? ushort.Parse(value) : DefaultKiloIterations;
        var shortRange = config.TryGetValue("shortRange", out value) ? bool.Parse(value) : DefaultShortRange;

        _currentPeriodMs = periodMs;

        if (!IsEnabled())
            throw new Exception("TMF882X is not enabled after initialization.");

        // Load the common configuration page with command
        I2C.WriteReg(REG_CMD_STAT, CMD_LOAD_CONFIG_PAGE_COMMON, token);
        WaitCmdDone(token: token);

        // Configure period/iterations
        I2C.WriteReg(REG_PERIOD_LSB, (byte)(periodMs & 0xFF), token);
        I2C.WriteReg(REG_PERIOD_MSB, (byte)((periodMs >> 8) & 0xFF), token);
        I2C.WriteReg(REG_KILO_ITER_LSB, (byte)(kiloIterations & 0xFF), token);
        I2C.WriteReg(REG_KILO_ITER_MSB, (byte)((kiloIterations >> 8) & 0xFF), token);
        
        // Configure SPAD map to 3x3 normal mode 33°x32° FoV
        I2C.WriteReg(REG_SPAD_MAP_ID, 0x01, token);

        // Commit config
        I2C.WriteReg(REG_CMD_STAT, CMD_WRITE_CONFIG_PAGE, token);
        WaitCmdDone(token: token);

        // Configure range mode (§4.2.2) – only if firmware supports it
        var rangeMode = GetActiveRange(token);
        var expectedRangeMode = shortRange ? RangeMode.Short : RangeMode.Long;
        if (rangeMode.HasValue && rangeMode.Value != expectedRangeMode)
            SetRange(expectedRangeMode, token);
    
        Initialized = true;
    }

    /// <summary>
    /// Returns true if the current firmware supports short-range accuracy mode.
    /// Checks bit 4 of register BUILD_VERSION (0x03) as per §4.2.1.
    /// </summary>
    public bool SupportsShortRange(CancellationToken token = default)
    {
        byte buildVersion = I2C.ReadReg(REG_BUILD_VERSION, token);
        return (buildVersion & 0x10) != 0;
    }

    /// <summary>
    /// Returns the currently active range mode, or null if not supported (§4.2.2).
    /// </summary>
    public RangeMode? GetActiveRange(CancellationToken token = default)
    {
        byte activeRange = I2C.ReadReg(REG_ACTIVE_RANGE, token);
        return activeRange switch
        {
            CMD_SHORT_RANGE_ACCURACY => RangeMode.Short,
            CMD_LONG_RANGE_ACCURACY  => RangeMode.Long,
            _                        => null
        };
    }

    /// <summary>
    /// Switches the range accuracy mode (§4.2.3).
    /// Must only be called when no measurement is ongoing.
    /// </summary>
    public void SetRange(RangeMode mode, CancellationToken token = default)
    {
        I2C.WriteReg(REG_CMD_STAT, (byte)mode, token);
        WaitCmdDone(token: token);
    }

    public AppId GetAppId()
    {
        byte appId = I2C.ReadReg(REG_APPID);
        return appId switch
        {
            0x03 => AppId.Application,
            0x80 => AppId.Bootloader,
            _ => throw new Exception($"TMF882X: APPID=0x{appId:X2} (unexpected value).")
        };
    }

    public enum EnableFlags : byte
    {
        CpuReady = (1 << 6),
        Pon = (1 << 0)
    }

    public bool IsEnabled ()
    {
        byte en = I2C.ReadReg(REG_ENABLE);
        return (en & ((byte)EnableFlags.CpuReady | (byte)EnableFlags.Pon)) != 0;
    }

    public void DownloadImage(CancellationToken token = default)
    {           
        if (GetAppId() != AppId.Bootloader)
            return; // application already active, no need to re-flash

        // 1) DOWNLOAD_INIT
        SendBootloaderCommand(CMD_DOWNLOAD_INIT, [TMF882XFirmware.DownloadInitParam]);
        WaitBootloaderReady(token: token);

        // 2) SET_ADDR
        SetBootloaderAddress(TMF882XFirmware.StartAddress);
        WaitBootloaderReady(token: token);

        // 3) W_RAM chunks
        int offset = 0;
        while (offset < TMF882XFirmware.Image.Length)
        {
            token.ThrowIfCancellationRequested();
            int len = Math.Min(TMF882XFirmware.ChunkSize, TMF882XFirmware.Image.Length - offset);
            WriteBootloaderRamChunk(TMF882XFirmware.Image.AsSpan(offset, len));
            WaitBootloaderReady(token: token);
            offset += len;
        }

        // 4) RAMREMAP_RESET and wait for application
        SendBootloaderCommand(CMD_RAMREMAP_RESET, []);
        WaitForAppId(AppId.Application, token: token);
    }

    // Check that a command has been executed
    void WaitCmdDone(int? timeoutMs = null, CancellationToken token = default)
    {
        int effectiveTimeoutMs = timeoutMs ?? CommandTimeoutMs;
        int t0 = Environment.TickCount;

        while (Environment.TickCount - t0 < effectiveTimeoutMs)
        {
            token.ThrowIfCancellationRequested();
            byte v = I2C.ReadReg(CMD_STAT, token); 

            // Continue reading if value returned is 0x10..0xFF
            if (v <= 0x0F)
            {
                // 0x00 = STAT_OK, 0x01 = STAT_ACCEPTED, other values are errors
                if (v == 0x00 || v == 0x01)
                    return;

                throw new Exception($"TMF882X: CMD_STAT error status=0x{v:X2}");
            }

            SleepWithCancellation(2, token);
        }

        throw new TimeoutException("TMF882X: CMD_STAT timeout (no status returned).");
    }

    private static byte BootloaderChecksum(byte cmd, byte size, ReadOnlySpan<byte> data)
    {
        int sum = cmd + size;
        for (int i = 0; i < data.Length; i++) sum += data[i];
        return (byte)(0xFF ^ (sum & 0xFF));
    }

    private void SendBootloaderCommand(byte cmd, ReadOnlySpan<byte> data)
    {
        if (data.Length > 255)
            throw new ArgumentOutOfRangeException(nameof(data), "TMF882X: bootloader payload size > 255.");

        byte size = (byte)data.Length;
        var buffer = new byte[4 + data.Length];
        buffer[0] = REG_CMD_STAT;
        buffer[1] = cmd;
        buffer[2] = size;
        data.CopyTo(buffer.AsSpan(3));
        buffer[^1] = BootloaderChecksum(cmd, size, data);

        I2C.WriteBytes(buffer);
    }

    private void WaitBootloaderReady(int? timeoutMs = null, CancellationToken token = default)
    {
        int effectiveTimeoutMs = timeoutMs ?? CommandTimeoutMs;
        int start = Environment.TickCount;
        while (Environment.TickCount - start < effectiveTimeoutMs)
        {
            // The host should wait until it reads back the following 3 bytes: 0x00 0x00 0xFF
            token.ThrowIfCancellationRequested();
            byte[] status = I2C.ReadRegs(REG_CMD_STAT, 3, token);
            if (status[0] == 0x00 && status[1] == 0x00 && status[2] == 0xFF)
                return;

            if (status[0] >= 0x01 && status[0] <= 0x0F)
                throw new Exception($"TMF882X: bootloader CMD_STAT error=0x{status[0]:X2} (csum/size/command).");

            SleepWithCancellation(2, token);
        }

        throw new TimeoutException("TMF882X: bootloader not ready (CMD_STAT != 00 00 FF).");
    }

    private void SetBootloaderAddress(ushort address)
    {
        Span<byte> payload = stackalloc byte[2];
        payload[0] = (byte)(address & 0xFF);
        payload[1] = (byte)((address >> 8) & 0xFF);
        SendBootloaderCommand(CMD_SET_ADDR, payload);
    }

    private void WriteBootloaderRamChunk(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
            throw new ArgumentOutOfRangeException(nameof(data), "TMF882X: empty chunk.");
        if (data.Length > 255)
            throw new ArgumentOutOfRangeException(nameof(data), "TMF882X: chunk > 255.");

        SendBootloaderCommand(CMD_W_RAM, data);
    }

    private void WaitForAppId(AppId expected, int? timeoutMs = null, CancellationToken token = default)
    {
        int effectiveTimeoutMs = timeoutMs ?? AppIdTimeoutMs;
        int start = Environment.TickCount;
        while (Environment.TickCount - start < effectiveTimeoutMs)
        {
            token.ThrowIfCancellationRequested();
            var value = I2C.ReadReg(REG_APPID, token);
            if (value == (byte) expected)
                return;
            SleepWithCancellation(1, token);
        }
        throw new TimeoutException($"TMF882X: APPID 0x{expected:X2} not reached after download.");
    }

    public void Start(CancellationToken token = default)
    {
        // Start measure
        I2C.WriteReg(REG_CMD_STAT, CMD_MEASURE, token);
        WaitCmdDone(token: token);
    }

    void WaitForMeasurement(int timeoutMs, CancellationToken token = default)
    {
        const byte Interrupt2 = 0x02; // bit mask for interrupt 2 (measurement ready)
        int t0 = Environment.TickCount;
        while (Environment.TickCount - t0 < timeoutMs)
        {
            token.ThrowIfCancellationRequested();
            if ((I2C.ReadReg(REG_INT_STATUS, token) & Interrupt2) != 0)
            {
                I2C.WriteReg(REG_INT_STATUS, Interrupt2, token);
                return;
            }
            SleepWithCancellation(5, token);
        }
        throw new TimeoutException("TMF882X: timeout INT");
    }

    protected override List<(int distMM, float confidence)> ReadOnceInternal(int TimeoutMs = 1000, CancellationToken token = default)
    {
        // Start measure
        I2C.WriteReg(REG_CMD_STAT, CMD_MEASURE, token);
        WaitCmdDone(token: token);

        WaitForMeasurement(TimeoutMs, token);

        List<(int distMM, float confidence)> results = [];
        for (byte i = 0; i < 9; i++)
        {
            token.ThrowIfCancellationRequested();
            byte index = (byte)(i * 3);
            byte conf = I2C.ReadReg((byte)(REG_CONF0 + index), token);
            byte lo = I2C.ReadReg((byte)(REG_DIST0_LSB + index), token);
            byte hi = I2C.ReadReg((byte)(REG_DIST0_MSB + index), token);
            ushort dist = (ushort)(lo | ((ushort)hi << 8));
            results.Add(((int)dist, (float)conf/255.0f));
        }
        return results;
    }

    private static void SleepWithCancellation(int milliseconds, CancellationToken token)
    {
        if (!token.CanBeCanceled)
        {
            Thread.Sleep(milliseconds);
            return;
        }

        if (token.WaitHandle.WaitOne(milliseconds))
            throw new OperationCanceledException(token);
    }

}