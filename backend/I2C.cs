using System.Device.I2c;

namespace immensive;

public enum I2CDetectionState
{
    // Device detected and responding
    Present,
    // No device at this address
    Absent,
    // Error during detection
    Error
}

public class I2C : IDisposable
{
    protected I2cDevice Device {get;private set;}
    protected object LockI2C  {get;private set;}  = new();

    public I2C (int busId, int deviceAddress)
    {
        Device = I2cDevice.Create(new I2cConnectionSettings(busId, deviceAddress));
    }

    public I2C (I2cDevice device)
    {
        Device = device;
    }

    protected int DeviceAddress { get {
        return Device.ConnectionSettings.DeviceAddress;
    } }

    public static bool Verbose {get;set;} = false;

    public bool Ping ()
    {
        try
        {
            // A simple read to check if device responds
            ReadByte(1);
            if (Verbose)
                Console.WriteLine($"[I2C] Ping: Device 0x{DeviceAddress:X2} is present.");
            return true;
        }
        catch (Exception ex)
        {
            if (Verbose)
                Console.WriteLine($"[I2C] Ping: Device 0x{DeviceAddress:X2} not responding: {ex.Message}");
            return false;
        }
    }
    
    public bool WriteBytes(byte[] data, int retry = 3)
    {
        if (Verbose)
            Console.WriteLine($"[I2C] WriteBytes: Device 0x{DeviceAddress:X2}, Data={Convert.ToHexString(data)}, Retry={retry}");
        
        for (int i = 0; i < retry; i++)
        {
            try
            {
                lock (LockI2C)
                {
                    Device.Write(data);
                    if (Verbose)
                        Console.WriteLine($"[I2C] WriteBytes SUCCESS");
                }
                return true;
            }
            catch
            {
                if (i == retry - 1) throw;
                Thread.Sleep(5);
            }
        }
        return false;
    }
    
    public bool WriteByte(byte value, int retry = 3)
        => WriteBytes([value], retry);
    
    public byte[] ReadBytes(int count, int retry = 3)
    { 
        var buffer = new byte[count];
        if (Verbose)
            Console.WriteLine($"[I2C] ReadBytes: Device 0x{DeviceAddress:X2}, Count={count}, Retry={retry}");

        for (int i = 0; i < retry; i++)
        {
            try
            {
                lock (LockI2C)
                {
                    Device.Read(buffer);
                    if (Verbose)
                        Console.WriteLine($"[I2C] ReadBytes SUCCESS: {Convert.ToHexString(buffer)}");
                }
                return buffer;
            }
            catch (Exception ex)
            {
                if (Verbose)
                    Console.WriteLine($"[I2C] ReadBytes RETRY {i+1}/{retry}: {ex.Message}");
                if (i == retry - 1) throw;
                Thread.Sleep(5);
            }
        }
        return buffer;
    }

    public byte ReadByte(int retry = 3)
        => ReadBytes(1, retry)[0];

    // --- helpers for registers ---
    
    public void WriteReg(byte reg, byte value)
        => WriteBytes([reg, value], 1);

    public byte ReadReg(byte reg)
    {
        // set register pointer then read 1 byte
        WriteBytes([reg],1);
        return ReadByte(1);
    }

    public byte[] ReadRegs(byte startReg, int count)
    {
        WriteBytes([startReg]);
        return ReadBytes(count,1);
    }

    public void Dispose()
    {
        Device?.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Try to detect if an I2C device is present at the given address
    /// </summary>
    /// <param name="busId">I2C bus ID</param>
    /// <param name="addr">Device address to check</param>
    /// <param name="buffer">Reusable buffer for reading</param>
    /// <returns>Detection state: Present, Absent, or Error</returns>
    public static I2CDetectionState TryDetectDevice(int busId, int addr, byte[] buffer)
    {
        try
        {
            using var dev = I2cDevice.Create(new I2cConnectionSettings(busId, addr));
            dev.Read(buffer);
            return I2CDetectionState.Present;
        }
        catch (System.IO.IOException)
        {
            return I2CDetectionState.Absent;
        }
        catch (Exception)
        {
            return I2CDetectionState.Error;
        }
    }

    // Detect I2C devices on the specified bus
    public static void I2cDetect(int busId)
    {
        Console.WriteLine("     00 01 02 03 04 05 06 07 08 09 0A 0B 0C 0D 0E 0F");

        byte[] buffer = new byte[1];
        for (int row = 0; row < 8; row++)
        {
            Console.Write($"{row * 16:X2}: ");

            for (int col = 0; col < 16; col++)
            {
                int addr = row * 16 + col;

                if (addr < 0x03 || addr > 0x77)
                {
                    Console.Write(" ..");
                    continue;
                }

                var state = TryDetectDevice(busId, addr, buffer);
                Console.Write(state switch
                {
                    I2CDetectionState.Present => $" {addr:X2}",
                    I2CDetectionState.Absent => " --",
                    I2CDetectionState.Error => " ??",
                    _ => " ??"
                });
            }

            Console.WriteLine();
        }
    }
}
