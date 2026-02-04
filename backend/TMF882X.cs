// Time-of-Flight distance sensors TMF882X
// Hookup guide: https://learn.sparkfun.com/tutorials/qwiic-dtof-imager-tmf882x-hookup-guide/all
// Datasheet: https://cdn.sparkfun.com/assets/learn_tutorials/2/2/8/9/TMF882X_DataSheet.pdf
// Firmware download: https://github.com/sparkfun/SparkFun_Qwiic_TMF882X_Arduino_Library/blob/main/examples/Example-07_Firmware/Example-07_Firmware.ino

namespace immensive;

// Interrupt status register (0xE1) bit field
public readonly struct IntStatus(byte value)
{
    public byte RawValue { get; } = value;

    // Bit 6: Status register has been set to non-zero value
    public bool Int7_StatusRegister => (RawValue & Int7()) != 0;

    // Bit 5: Received command has been handled
    public bool Int6_CommandHandled => (RawValue & Int6()) != 0;

    // Bit 3: Raw histogram is ready for readout
    public bool Int4_HistogramReady => (RawValue & Int4()) != 0;

    // Bit 1: Measurement result is ready for readout
    public bool Int2_MeasurementReady => (RawValue & Int2()) != 0;

    // Clear specific interrupt bit by writing 1 to it
    public static byte Int7() => 0x40;
    public static byte Int6() => 0x20;
    public static byte Int4() => 0x08;
    public static byte Int2() => 0x02;
}

public class TMF882X: II2CDevice
{
    private I2C? _i2c;
    
    // TMF882X Register addresses
    private const byte REG_APPID = 0x00;
    private const byte REG_CMD_STAT = 0x08;
    private const byte REG_INT_STATUS = 0xE1;
    private const byte REG_ENABLE = 0xE0;
    private const byte REG_CONFIG_RESULT = 0x20;

    // When page "common config" (cid_rid=0x16) is loaded :
    private const byte REG_PERIOD_LSB = 0x24;
    private const byte REG_PERIOD_MSB = 0x25;
    private const byte REG_KILO_ITER_LSB = 0x26;
    private const byte REG_KILO_ITER_MSB = 0x27;

    // When the "results" page (cid_rid=0x10) is active :
    private const byte REG_CONF0 = 0x38;
    private const byte REG_DIST0_LSB = 0x39;
    private const byte REG_DIST0_MSB = 0x3A;
    
    // Commands
    
    // Measure: start a cyclic measurement according to the configuration
    private const byte CMD_MEASURE = 0x10;
    // Configuration page (whatever page has been loaded to registers 0x20 andfollowing will be written to the device)
    private const byte CMD_WRITE_CONFIG_PAGE = 0x15;
    // Load Configuration Page 0 - common configuration
    private const byte CMD_LOAD_CONFIG_PAGE_COMMON = 0x16;
    // Stop: Abort any ongoing measurement
    private const byte CMD_STOP = 0xFF;
    
    public TMF882X(int address = 0x41) : base(address)
    {
        Name = "TMF882X Time-of-Flight Sensor";
    }

    public override bool TryDetect(int busId)
    {
        try
        {
            _i2c = new I2C(busId, Address);
            
            // According to TMF882X datasheet:
            // Register 0x00: APPID - Application ID
            // Expected values: 0x03 (ROM), 0x80 (RAM), 0xC0 (APP)
            // We check for any valid TMF882X app ID
            
            // Write register address 0x00
            _i2c.WriteByte(REG_APPID);
            
            // Read the APPID register
            byte appId = _i2c.ReadByte();
            
            // Valid TMF882X APPID values according to datasheet
            // 0x03 = ROM bootloader mode
            // 0x80 = RAM bootloader mode  
            // 0xC0 = Application mode
            bool isValidAppId = (appId == 0x03 || appId == 0x80 || appId == 0xC0);
            return isValidAppId;
        }
        catch
        {
            _i2c?.Dispose();
            _i2c = null;
            return false;
        }
    }

    private void ClearIntStatus(byte mask)
    {
        // R_PUSH1: writing '1' clears the corresponding interrupt bit :contentReference[oaicite:11]{index=11}
        _i2c!.WriteReg(REG_INT_STATUS, mask);
    }

    // CMD_STAT: device writes back 0x00..0x0F when done :contentReference[oaicite:2]{index=2}
    void WaitCmdDone(int timeoutMs = 1000)
    {
        int t0 = Environment.TickCount;

        while (Environment.TickCount - t0 < timeoutMs)
        {
            byte v = _i2c!.ReadReg(0x08); // CMD_STAT

            // Command range is 0x10..0xFF, status range is 0x00..0x0F :contentReference[oaicite:3]{index=3}
            if (v <= 0x0F)
            {
                // 0x00 = STAT_OK, 0x01 = STAT_ACCEPTED, other values are errors :contentReference[oaicite:4]{index=4}
                if (v == 0x00 || v == 0x01) return;

                throw new Exception($"TMF882X: CMD_STAT error status=0x{v:X2}");
            }

            Thread.Sleep(2);
        }

        throw new TimeoutException("TMF882X: CMD_STAT timeout (no status returned).");
    }

    private void WaitCommandHandled(int timeoutMs = 200)
    {
        int start = Environment.TickCount;
        while (Environment.TickCount - start < timeoutMs)
        {
            byte st = _i2c!.ReadReg(REG_INT_STATUS);
            if ((st & IntStatus.Int6()) != 0)
            {
                ClearIntStatus(IntStatus.Int6());
                return;
            }
            Thread.Sleep(2);
        }
        // si tu veux, log CMD_STAT ici (0x00..0x0F = status; >0x0F = command range) :contentReference[oaicite:12]{index=12}
        throw new TimeoutException("TMF882X: commande non acquittée (INT6).");
    }

    private void WaitForResultsPage(int timeoutMs = 500)
    {
        int start = Environment.TickCount;
        while (Environment.TickCount - start < timeoutMs)
        {
            byte cid = _i2c!.ReadReg(REG_CONFIG_RESULT);
            if (cid == 0x10) return; // MEASUREMENT_RESULT :contentReference[oaicite:13]{index=13}
            Thread.Sleep(5);
        }
        throw new TimeoutException("TMF882X: la page résultats (cid_rid=0x10) n'est jamais devenue active.");
    }

    public void Start()
    {
        // (Optionnel mais utile) vérifier que tu es bien en app mesure (0x03), sinon tu es en bootloader. :contentReference[oaicite:5]{index=5}
        byte appid = _i2c!.ReadReg(0x00);
        if (appid != 0x03)
            throw new Exception($"TMF882X: APPID=0x{appid:X2} (pas l'app mesure 0x03).");

        // Charge page common
        _i2c!.WriteReg(0x08, 0x16);     // CMD_LOAD_CONFIG_PAGE_COMMON :contentReference[oaicite:6]{index=6}
        WaitCmdDone(1000);

        // Configure period/iterations
        _i2c!.WriteReg(0x24, 50); _i2c!.WriteReg(0x25, 0);
        _i2c!.WriteReg(0x26, 25); _i2c!.WriteReg(0x27, 0);

        // Commit config
        _i2c!.WriteReg(0x08, 0x15);     // CMD_WRITE_CONFIG_PAGE :contentReference[oaicite:7]{index=7}
        WaitCmdDone(1000);

        // Start measure
        _i2c!.WriteReg(0x08, 0x10);     // CMD_MEASURE :contentReference[oaicite:8]{index=8}
        WaitCmdDone(1000);
    }

    public void Start2(int periodMs = 50, int kiloIterations = 25)
    {
        // (Optionnel mais utile) vérifier que tu es bien en app mesure (0x03), sinon tu es en bootloader. :contentReference[oaicite:5]{index=5}
        byte appid = _i2c!.ReadReg(0x00);
        if (appid != 0x03)
            throw new Exception($"TMF882X: APPID=0x{appid:X2} (pas l'app mesure 0x03).");

        // Optionnel mais utile: vérifier que cpu_ready=1 avant de toucher aux pages :contentReference[oaicite:14]{index=14}
        byte en = _i2c!.ReadReg(REG_ENABLE);
        bool cpuReady = (en & (1 << 6)) != 0;
        if (!cpuReady)
            throw new InvalidOperationException("TMF882X: ENABLE.cpu_ready=0 (firmware pas prêt / mode standby/bootloader).");

        // Load common config page
        _i2c!.WriteReg(REG_CMD_STAT, CMD_LOAD_CONFIG_PAGE_COMMON);
        WaitCommandHandled();

        // Configure
        _i2c!.WriteReg(REG_PERIOD_LSB, (byte)(periodMs & 0xFF));
        _i2c!.WriteReg(REG_PERIOD_MSB, (byte)((periodMs >> 8) & 0xFF));
        _i2c!.WriteReg(REG_KILO_ITER_LSB, (byte)(kiloIterations & 0xFF));
        _i2c!.WriteReg(REG_KILO_ITER_MSB, (byte)((kiloIterations >> 8) & 0xFF));

        // Commit config
        _i2c!.WriteReg(REG_CMD_STAT, CMD_WRITE_CONFIG_PAGE);
        WaitCommandHandled();

        // Start measurement
        _i2c!.WriteReg(REG_CMD_STAT, CMD_MEASURE);
        WaitCommandHandled();

        // Attendre que la page résultats soit active (sinon tes lectures 0x24.. ne sont pas des résultats)
        WaitForResultsPage();
    }

    void ClearInt(byte mask) => _i2c!.WriteReg(REG_INT_STATUS, mask);

    void WaitInt(byte mask, int timeoutMs)
    {
        int t0 = Environment.TickCount;
        while (Environment.TickCount - t0 < timeoutMs)
        {
            if ((_i2c!.ReadReg(REG_INT_STATUS) & mask) != 0)
            {
                ClearInt(mask);
                return;
            }
            Thread.Sleep(5);
        }
        throw new TimeoutException("TMF882X: timeout INT");
    }

    public (ushort dist, byte conf) ReadOnce()
    {
        WaitInt(IntStatus.Int2(), 5000);

        byte conf = _i2c!.ReadReg(REG_CONF0);
        byte lo = _i2c!.ReadReg(REG_DIST0_LSB);
        byte hi = _i2c!.ReadReg(REG_DIST0_MSB);
        return ((ushort)(lo | (hi << 8)), conf);
    }

    public void Stop() => _i2c!.WriteReg(REG_CMD_STAT, CMD_STOP);

    public (ushort distanceMm, byte confidence) ReadZone1()
    {
        // Ici, tu peux garder ton block read depuis 0x20 si tu veux.
        // Mais au minimum, assure-toi que CONFIG_RESULT==0x10 avant.
        if (_i2c!.ReadReg(REG_CONFIG_RESULT) != 0x10)
            throw new InvalidOperationException("TMF882X: pas sur la page résultats (cid_rid != 0x10).");

        // zone0 distance/confidence (cid_rid=0x10) :contentReference[oaicite:15]{index=15}
        byte conf = _i2c!.ReadReg(0x38);
        byte dL = _i2c!.ReadReg(0x39);
        byte dH = _i2c!.ReadReg(0x3A);
        return ((ushort)(dL | (dH << 8)), conf);
    }

    public void WaitForNextSample_Int2(int timeoutMs = 5000, int pollMs = 5)
    {
        int start = Environment.TickCount;

        while (Environment.TickCount - start < timeoutMs)
        {
            byte st = _i2c!.ReadReg(REG_INT_STATUS);
            if ((st & IntStatus.Int2()) != 0)
            {
                // résultat prêt
                ClearIntStatus(IntStatus.Int2());
                return;
            }
            Thread.Sleep(pollMs);
        }

        throw new TimeoutException($"TMF882X: pas de résultat (INT2) dans {timeoutMs} ms.");
    }
}