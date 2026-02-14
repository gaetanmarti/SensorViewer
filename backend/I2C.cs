using System.Device.I2c;

namespace immensive;

/// <summary>
/// I2C device detection state.
/// </summary>
public enum I2CDetectionState
{
    /// <summary>Device detected and responding.</summary>
    Present,
    /// <summary>No device at this address.</summary>
    Absent,
    /// <summary>Error occurred during detection.</summary>
    Error
}

/// <summary>
/// Wrapper class for I2C communication with automatic retry logic and flexible transfer modes.
/// </summary>
/// <remarks>
/// This class provides a high-level interface for I2C operations with built-in retry mechanisms,
/// thread-safe locking, and support for different communication modes (WriteRead vs WriteThenRead).
/// All operations support cancellation tokens for graceful shutdown.
/// </remarks>
public class I2C : IDisposable
{
    /// <summary>
    /// I2C transfer mode determines how read operations with register pointers are performed.
    /// </summary>
    public enum TransferMode
    {
        /// <summary>Automatically tries WriteRead first, falls back to WriteThenRead if it fails.</summary>
        Auto,
        /// <summary>Uses a single WriteRead transaction (most devices).</summary>
        WriteRead,
        /// <summary>Uses separate Write and Read transactions (some devices require this).</summary>
        WriteThenRead
    }

    /// <summary>
    /// Byte order (endianness) for multi-byte reads.
    /// </summary>
    public enum ByteOrder
    {
        /// <summary>Little-endian: least significant byte first (most common, e.g., x86).</summary>
        LittleEndian,
        /// <summary>Big-endian: most significant byte first (network byte order).</summary>
        BigEndian
    }

    private const int DefaultRetryCount = 3;
    private const int RetryDelayMs = 5;

    private readonly I2cDevice _device;
    private readonly object _lock = new();

    /// <summary>
    /// Gets the transfer mode used by this I2C instance.
    /// </summary>
    public TransferMode Mode { get; }

    /// <summary>
    /// Gets or sets whether verbose logging is enabled for debugging I2C operations.
    /// </summary>
    public static bool Verbose { get; set; } = false;

    /// <summary>
    /// Gets the I2C device address.
    /// </summary>
    protected int DeviceAddress => _device.ConnectionSettings.DeviceAddress;

    /// <summary>
    /// Initializes a new instance of the I2C class.
    /// </summary>
    /// <param name="busId">I2C bus ID (typically 0 or 1).</param>
    /// <param name="deviceAddress">7-bit I2C device address.</param>
    /// <param name="mode">Transfer mode to use for read operations.</param>
    public I2C(int busId, int deviceAddress, TransferMode mode = TransferMode.Auto)
    {
        _device = I2cDevice.Create(new I2cConnectionSettings(busId, deviceAddress));
        Mode = mode;
    }

    /// <summary>
    /// Initializes a new instance of the I2C class from an existing I2cDevice.
    /// </summary>
    /// <param name="device">Existing I2cDevice instance.</param>
    /// <param name="mode">Transfer mode to use for read operations.</param>
    public I2C(I2cDevice device, TransferMode mode = TransferMode.Auto)
    {
        _device = device;
        Mode = mode;
    }

    /// <summary>
    /// Get the underlying I2cDevice for use with external drivers.
    /// </summary>
    /// <returns>The underlying I2cDevice instance.</returns>
    public I2cDevice GetI2cDevice() => _device;

    /// <summary>
    /// Checks if the I2C device is responding.
    /// </summary>
    /// <param name="token">Cancellation token.</param>
    /// <returns>True if the device responds; otherwise false.</returns>
    public bool Ping(CancellationToken token = default)
    {
        try
        {
            ReadByte(token: token);
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

    /// <summary>
    /// Writes a byte array to the I2C device with automatic retry logic.
    /// </summary>
    /// <param name="data">Data bytes to write.</param>
    /// <param name="retry">Number of retry attempts (default: 3).</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>True if write succeeded; false otherwise.</returns>
    /// <exception cref="OperationCanceledException">Thrown if operation is cancelled.</exception>
    public bool WriteBytes(byte[] data, int retry = DefaultRetryCount, CancellationToken token = default)
    {
        if (Verbose)
            Console.WriteLine($"[I2C] WriteBytes: Device 0x{DeviceAddress:X2}, Data={Convert.ToHexString(data)}, Retry={retry}");
        
        return RetryOperation(() =>
        {
            lock (_lock)
            {
                _device.Write(data);
                if (Verbose)
                    Console.WriteLine($"[I2C] WriteBytes SUCCESS");
            }
        }, retry, token);
    }
    
    /// <summary>
    /// Writes a single byte to the I2C device.
    /// </summary>
    /// <param name="value">Byte value to write.</param>
    /// <param name="retry">Number of retry attempts (default: 3).</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>True if write succeeded; false otherwise.</returns>
    public bool WriteByte(byte value, int retry = DefaultRetryCount, CancellationToken token = default)
        => WriteBytes([value], retry, token);
    
    /// <summary>
    /// Reads a specified number of bytes from the I2C device with automatic retry logic.
    /// </summary>
    /// <param name="count">Number of bytes to read.</param>
    /// <param name="retry">Number of retry attempts (default: 3).</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>Array of bytes read from the device.</returns>
    /// <exception cref="OperationCanceledException">Thrown if operation is cancelled.</exception>
    public byte[] ReadBytes(int count, int retry = DefaultRetryCount, CancellationToken token = default)
    { 
        var buffer = new byte[count];
        if (Verbose)
            Console.WriteLine($"[I2C] ReadBytes: Device 0x{DeviceAddress:X2}, Count={count}, Retry={retry}");

        RetryOperation(() =>
        {
            lock (_lock)
            {
                _device.Read(buffer);
                if (Verbose)
                    Console.WriteLine($"[I2C] ReadBytes SUCCESS: {Convert.ToHexString(buffer)}");
            }
        }, retry, token, logRetries: true);

        return buffer;
    }

    /// <summary>
    /// Reads a single byte from the I2C device.
    /// </summary>
    /// <param name="retry">Number of retry attempts (default: 3).</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>Byte read from the device.</returns>
    public byte ReadByte(int retry = DefaultRetryCount, CancellationToken token = default)
        => ReadBytes(1, retry, token)[0];

    /// <summary>
    /// Reads a signed 16-bit integer (short) from the I2C device.
    /// </summary>
    /// <param name="byteOrder">Byte order for the 16-bit value (Little-endian or Big-endian).</param>
    /// <param name="retry">Number of retry attempts (default: 3).</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>Signed 16-bit integer read from the device.</returns>
    public short ReadShort(ByteOrder byteOrder = ByteOrder.LittleEndian, int retry = DefaultRetryCount, CancellationToken token = default)
    {
        var bytes = ReadBytes(2, retry, token);
        return byteOrder == ByteOrder.LittleEndian
            ? (short)(bytes[0] | (bytes[1] << 8))
            : (short)((bytes[0] << 8) | bytes[1]);
    }

    /// <summary>
    /// Reads an unsigned 16-bit integer (ushort) from the I2C device.
    /// </summary>
    /// <param name="byteOrder">Byte order for the 16-bit value (Little-endian or Big-endian).</param>
    /// <param name="retry">Number of retry attempts (default: 3).</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>Unsigned 16-bit integer read from the device.</returns>
    public ushort ReadUShort(ByteOrder byteOrder = ByteOrder.LittleEndian, int retry = DefaultRetryCount, CancellationToken token = default)
    {
        var bytes = ReadBytes(2, retry, token);
        return byteOrder == ByteOrder.LittleEndian
            ? (ushort)(bytes[0] | (bytes[1] << 8))
            : (ushort)((bytes[0] << 8) | bytes[1]);
    }

    /// <summary>
    /// Performs a combined write-read operation in a single I2C transaction.
    /// </summary>
    /// <param name="writeBuffer">Data to write before reading.</param>
    /// <param name="readCount">Number of bytes to read.</param>
    /// <param name="retry">Number of retry attempts (default: 3).</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>Array of bytes read from the device.</returns>
    /// <exception cref="OperationCanceledException">Thrown if operation is cancelled.</exception>
    public byte[] WriteRead(byte[] writeBuffer, int readCount, int retry = DefaultRetryCount, CancellationToken token = default)
    {
        if (Verbose)
            Console.WriteLine($"[I2C] WriteRead: Device 0x{DeviceAddress:X2}, Write={Convert.ToHexString(writeBuffer)}, ReadCount={readCount}, Retry={retry}");

        var readBuffer = new byte[readCount];
        RetryOperation(() =>
        {
            lock (_lock)
            {
                _device.WriteRead(writeBuffer, readBuffer);
                if (Verbose)
                    Console.WriteLine($"[I2C] WriteRead SUCCESS: {Convert.ToHexString(readBuffer)}");
            }
        }, retry, token, logRetries: true);

        return readBuffer;
    }

    /// <summary>
    /// Reads data from a specific register or memory address using the configured transfer mode.
    /// </summary>
    /// <param name="addrBuffer">Address/register bytes to write before reading.</param>
    /// <param name="readCount">Number of bytes to read.</param>
    /// <param name="retry">Number of retry attempts (default: 3).</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>Array of bytes read from the device.</returns>
    /// <remarks>
    /// This method respects the TransferMode setting:
    /// - WriteRead: Uses a single I2C transaction (most common)
    /// - WriteThenRead: Uses separate write and read transactions (some devices require this)
    /// - Auto: Tries WriteRead first, falls back to WriteThenRead on failure
    /// </remarks>
    public byte[] ReadWithPointer(byte[] addrBuffer, int readCount, int retry = DefaultRetryCount, CancellationToken token = default)
    {
        switch (Mode)
        {
            case TransferMode.WriteThenRead:
                WriteBytes(addrBuffer, retry, token);
                return ReadBytes(readCount, retry, token);

            case TransferMode.WriteRead:
                return WriteRead(addrBuffer, readCount, retry, token);

            case TransferMode.Auto:
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

    // ==========================================
    // Register-based convenience methods
    // ==========================================
    
    /// <summary>
    /// Writes a byte value to a specific register.
    /// </summary>
    /// <param name="reg">Register address.</param>
    /// <param name="value">Value to write.</param>
    /// <param name="token">Cancellation token.</param>
    public void WriteReg(byte reg, byte value, CancellationToken token = default)
        => WriteBytes([reg, value], DefaultRetryCount, token);

    /// <summary>
    /// Reads a single byte from a specific register.
    /// </summary>
    /// <param name="reg">Register address.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>Byte value read from the register.</returns>
    public byte ReadReg(byte reg, CancellationToken token = default)
        => ReadWithPointer([reg], 1, DefaultRetryCount, token)[0];

    /// <summary>
    /// Reads multiple consecutive bytes starting from a specific register.
    /// </summary>
    /// <param name="startReg">Starting register address.</param>
    /// <param name="count">Number of bytes to read.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>Array of bytes read from consecutive registers.</returns>
    public byte[] ReadRegs(byte startReg, int count, CancellationToken token = default)
        => ReadWithPointer([startReg], count, DefaultRetryCount, token);

    /// <summary>
    /// Reads a signed 16-bit integer (short) from a specific register.
    /// </summary>
    /// <param name="startReg">Starting register address (will read 2 consecutive bytes).</param>
    /// <param name="byteOrder">Byte order for the 16-bit value (Little-endian or Big-endian).</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>Signed 16-bit integer read from the register.</returns>
    public short ReadRegShort(byte startReg, ByteOrder byteOrder = ByteOrder.LittleEndian, CancellationToken token = default)
    {
        var bytes = ReadWithPointer([startReg], 2, DefaultRetryCount, token);
        return byteOrder == ByteOrder.LittleEndian
            ? (short)(bytes[0] | (bytes[1] << 8))
            : (short)((bytes[0] << 8) | bytes[1]);
    }

    /// <summary>
    /// Reads an unsigned 16-bit integer (ushort) from a specific register.
    /// </summary>
    /// <param name="startReg">Starting register address (will read 2 consecutive bytes).</param>
    /// <param name="byteOrder">Byte order for the 16-bit value (Little-endian or Big-endian).</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>Unsigned 16-bit integer read from the register.</returns>
    public ushort ReadRegUShort(byte startReg, ByteOrder byteOrder = ByteOrder.LittleEndian, CancellationToken token = default)
    {
        var bytes = ReadWithPointer([startReg], 2, DefaultRetryCount, token);
        return byteOrder == ByteOrder.LittleEndian
            ? (ushort)(bytes[0] | (bytes[1] << 8))
            : (ushort)((bytes[0] << 8) | bytes[1]);
    }

    // ==========================================
    // Private helper methods
    // ==========================================

    /// <summary>
    /// Executes an operation with automatic retry logic.
    /// </summary>
    private static bool RetryOperation(Action operation, int retry, CancellationToken token, bool logRetries = false)
    {
        for (int attempt = 0; attempt < retry; attempt++)
        {
            try
            {
                token.ThrowIfCancellationRequested();
                operation();
                return true;
            }
            catch
            {
                if (logRetries && Verbose)
                    Console.WriteLine($"[I2C] RETRY {attempt + 1}/{retry}");
                    
                if (attempt == retry - 1)
                    throw;
                    
                SleepWithCancellation(RetryDelayMs, token);
            }
        }
        return false;
    }

    /// <summary>
    /// Sleeps for the specified duration while respecting cancellation tokens.
    /// </summary>
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

    // ==========================================
    // Resource management
    // ==========================================

    /// <summary>
    /// Releases the I2C device resources.
    /// </summary>
    public void Dispose()
    {
        _device?.Dispose();
        GC.SuppressFinalize(this);
    }

    // ==========================================
    // Static utility methods
    // ==========================================

    /// <summary>
    /// Tries to detect if an I2C device is present at the specified address.
    /// </summary>
    /// <param name="busId">I2C bus ID.</param>
    /// <param name="addr">Device address to check (7-bit).</param>
    /// <param name="buffer">Reusable buffer for reading (minimum 1 byte).</param>
    /// <returns>Detection state: Present, Absent, or Error.</returns>
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

    /// <summary>
    /// Scans the I2C bus and prints a table of detected devices (similar to i2cdetect on Linux).
    /// </summary>
    /// <param name="busId">I2C bus ID to scan.</param>
    /// <remarks>
    /// Valid I2C addresses range from 0x03 to 0x77.
    /// Reserved addresses (0x00-0x02 and 0x78-0x7F) are not scanned.
    /// </remarks>
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

                // Skip reserved addresses
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
