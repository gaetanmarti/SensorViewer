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
    public enum TransferMode
    {
        Auto,
        WriteRead,
        WriteThenRead
    }

    protected I2cDevice Device {get;private set;}
    protected object LockI2C  {get;private set;}  = new();
    public TransferMode Mode { get; }

    public I2C (int busId, int deviceAddress, TransferMode mode = TransferMode.Auto)
    {
        Device = I2cDevice.Create(new I2cConnectionSettings(busId, deviceAddress));
        Mode = mode;
    }

    public I2C (I2cDevice device, TransferMode mode = TransferMode.Auto)
    {
        Device = device;
        Mode = mode;
    }

    protected int DeviceAddress { get {
        return Device.ConnectionSettings.DeviceAddress;
    } }

    /// <summary>
    /// Get the underlying I2cDevice for use with external drivers.
    /// </summary>
    public I2cDevice GetI2cDevice() => Device;

    public static bool Verbose {get;set;} = false;

    public bool Ping (CancellationToken token = default)
    {
        try
        {
            // A simple read to check if device responds
            ReadByte(1, token);
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
    
    public bool WriteBytes(byte[] data, int retry = 3, CancellationToken token = default)
    {
        if (Verbose)
            Console.WriteLine($"[I2C] WriteBytes: Device 0x{DeviceAddress:X2}, Data={Convert.ToHexString(data)}, Retry={retry}");
        
        for (int i = 0; i < retry; i++)
        {
            try
            {
                token.ThrowIfCancellationRequested();
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
                SleepWithCancellation(5, token);
            }
        }
        return false;
    }
    
    public bool WriteByte(byte value, int retry = 3, CancellationToken token = default)
        => WriteBytes([value], retry, token);
    
    public byte[] ReadBytes(int count, int retry = 3, CancellationToken token = default)
    { 
        var buffer = new byte[count];
        if (Verbose)
            Console.WriteLine($"[I2C] ReadBytes: Device 0x{DeviceAddress:X2}, Count={count}, Retry={retry}");

        for (int i = 0; i < retry; i++)
        {
            try
            {
                token.ThrowIfCancellationRequested();
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
                SleepWithCancellation(5, token);
            }
        }
        return buffer;
    }

    public byte ReadByte(int retry = 3, CancellationToken token = default)
        => ReadBytes(1, retry, token)[0];

    public byte[] WriteRead(byte[] writeBuffer, int readCount, int retry = 3, CancellationToken token = default)
    {
        if (Verbose)
            Console.WriteLine($"[I2C] WriteRead: Device 0x{DeviceAddress:X2}, Write={Convert.ToHexString(writeBuffer)}, ReadCount={readCount}, Retry={retry}");

        var readBuffer = new byte[readCount];
        for (int i = 0; i < retry; i++)
        {
            try
            {
                token.ThrowIfCancellationRequested();
                lock (LockI2C)
                {
                    Device.WriteRead(writeBuffer, readBuffer);
                    if (Verbose)
                        Console.WriteLine($"[I2C] WriteRead SUCCESS: {Convert.ToHexString(readBuffer)}");
                }
                return readBuffer;
            }
            catch (Exception ex)
            {
                if (Verbose)
                    Console.WriteLine($"[I2C] WriteRead RETRY {i + 1}/{retry}: {ex.Message}");
                if (i == retry - 1) throw;
                SleepWithCancellation(5, token);
            }
        }

        return readBuffer;
    }

    public byte[] ReadWithPointer(byte[] addrBuffer, int readCount, int retry = 3, CancellationToken token = default)
    {
        switch (Mode)
        {
            case TransferMode.WriteThenRead:
                WriteBytes(addrBuffer, retry, token);
                return ReadBytes(readCount, retry, token);
            case TransferMode.WriteRead:
                return WriteRead(addrBuffer, readCount, retry, token);
            default:
                try
                {
                    return WriteRead(addrBuffer, readCount, retry, token);
                }
                catch
                {
                    WriteBytes(addrBuffer, retry, token);
                    return ReadBytes(readCount, retry, token);
                }
        }
    }

    // --- helpers for registers ---
    
    public void WriteReg(byte reg, byte value, CancellationToken token = default)
        => WriteBytes([reg, value], 1, token);

    public byte ReadReg(byte reg, CancellationToken token = default)
    {
        return ReadWithPointer([reg], 1, 3, token)[0];
    }

    public byte[] ReadRegs(byte startReg, int count, CancellationToken token = default)
    {
        return ReadWithPointer([startReg], count, 3, token);
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
