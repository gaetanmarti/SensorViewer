namespace immensive;

/// <summary>
/// Base class for I2C devices.
/// </summary>
/// <remarks>
/// Implementors should set <see cref="Name"/>, provide detection logic in
/// <see cref="TryDetect"/>, and initialize device registers in
/// <see cref="Initialize"/>. The <see cref="I2C"/> instance is created on demand
/// when a bus id is provided, or can be injected by derived classes.
/// </remarks>
public abstract class II2CDevice (int address)
{
    /// <summary>
    /// I2C device address.
    /// </summary>
    public int Address { get; } = address;
    
    /// <summary>
    /// Human-readable device name.
    /// </summary>
    public string Name { get; protected set; } = "";

    public enum DeviceType {
        Unknown = 0,
        Distance = 1,
        Thermal = 2,
        HumanPresence = 3,
    }

    /// <summary>
    /// Device type.
    /// </summary>
    public DeviceType Type { get; protected set; } = DeviceType.Unknown;

    protected I2C? _i2c = null;
    protected virtual I2C.TransferMode PreferredTransferMode => I2C.TransferMode.Auto;

    /// <summary>
    /// Associated I2C instance for the device. Throws if not initialized.
    /// </summary>
    public I2C I2C { 
        get {
            if (_i2c == null)
                throw new InvalidOperationException($"I2C instance not set for device {Name} at address 0x{Address:X2}");
            return _i2c;
        } 
        protected set => _i2c = value;
    }

    /// <summary>
    /// Reset the I2C instance (e.g. when device is not responding).
    /// </summary>
    protected void Reset ()
    {
        _i2c?.Dispose();
        _i2c = null;
    }

    /// <summary>
    /// Try to detect the device on the specified bus.
    /// </summary>
    /// <param name="busId">I2C bus ID.</param>
    /// <param name="token">Optional cancellation token.</param>
    /// <returns>True if detected and responding; otherwise false.</returns>
    public virtual bool TryDetect(int busId, CancellationToken token = default) => false;

    /// <summary>
    /// Initialize the device using the provided configuration.
    /// </summary>
    /// <param name="config">Device-specific configuration.</param>
    /// <param name="busId">Optional I2C bus ID used to create the I2C instance.</param>
    /// <param name="token">Optional cancellation token.</param>
    public virtual void Initialize(Dictionary<string, string> config, int busId = -1, CancellationToken token = default)
    {
        Initialized = false;
        token.ThrowIfCancellationRequested();
        if (busId >= 0)
        {
            var mode = ResolveTransferMode(config);
            if (_i2c == null)
            {
                I2C = new I2C(busId, Address, mode);
            }
            else if (_i2c.Mode != mode)
            {
                _i2c.Dispose();
                I2C = new I2C(busId, Address, mode);
            }
            Initialized = true;
            return;
        }
        if (_i2c == null)
            throw new InvalidOperationException($"Cannot initialize device {Name} at address 0x{Address:X2}: invalid busId or I2C instance already set.");
    }

    protected I2C.TransferMode ResolveTransferMode(Dictionary<string, string> config)
    {
        if (config.TryGetValue("i2cTransferMode", out string? value) && !string.IsNullOrWhiteSpace(value))
        {
            if (value.Equals("writeread", StringComparison.OrdinalIgnoreCase))
                return I2C.TransferMode.WriteRead;
            if (value.Equals("writethenread", StringComparison.OrdinalIgnoreCase))
                return I2C.TransferMode.WriteThenRead;
            if (value.Equals("auto", StringComparison.OrdinalIgnoreCase))
                return I2C.TransferMode.Auto;
        }

        return PreferredTransferMode;
    }

    protected bool Initialized = false;

    public enum DeviceStatus {
        Unknown = 0,
        Detected = 1,
        Initialized = 2,
    }

    // Device status based on detection and initialization state
    public DeviceStatus Status {
        get {
            if (Initialized)
                return DeviceStatus.Initialized;
            return _i2c == null ? DeviceStatus.Unknown : DeviceStatus.Detected;
        }
    }

    // -- Logical operators ---

    // Equality operator based on Address and Name
    public static bool operator ==(II2CDevice? left, II2CDevice? right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null)
            return false;
        return left.Address == right.Address && left.Name == right.Name;
    }

    // Inequality operator
    public static bool operator !=(II2CDevice? left, II2CDevice? right)
    {
        return !(left == right);
    }

    // Override Equals for consistency
    public override bool Equals(object? obj)
    {
        return obj is II2CDevice device && Address == device.Address && Name == device.Name;
    }

    // Override GetHashCode for consistency
    public override int GetHashCode()
    {
        return HashCode.Combine(Address, Name);
    }
}

/// <summary>
/// Base class for I2C distance sensors.
/// </summary> <remarks>
/// Implementors should provide sensor specifications in <see cref="CurrentSpecifications"/> and implement measurement logic in <see cref="ReadOnce"/>.
/// </remarks>  
public abstract class II2CDistanceSensor : II2CDevice
{
    public II2CDistanceSensor(int address) : base(address)
    {
         Type = DeviceType.Distance;
    }

    /// <summary>
    /// Sensor specifications configuration.
    /// </summary>
    public record Specifications(int Width, int Height, float UpdateRateHz, float VerticalFOVDeg, float HorizontalFOVDeg);
    // Get the current sensor specifications (e.g. for point cloud projection)
    public abstract Specifications CurrentSpecifications();
    /// <summary> Read a single measurement from the sensor, returning a list of (distance in mm, confidence) tuples. </summary>
    /// <param name="TimeoutMs">Maximum time to wait for a measurement in milliseconds (default: 1000ms).</param>
    /// <param name="token">Optional cancellation token.</param>
    /// <returns>A list of (distance in mm, confidence) tuples representing the sensor measurement.</returns>
    public abstract List<(int distMM, float confidence)> ReadOnce(int TimeoutMs = 1000, CancellationToken token = default);
}

/// <summary>
/// Base class for I2C thermal sensors.
/// </summary>
/// <remarks>
/// Implementors should provide sensor specifications in <see cref="CurrentSpecifications"/> and implement measurement logic in <see cref="ReadOnce"/>.
/// </remarks>
public abstract class II2CThermalSensor : II2CDevice
{
    public II2CThermalSensor(int address) : base(address)
    {
        Type = DeviceType.Thermal;
    }

    /// <summary>
    /// Thermal sensor specifications configuration.
    /// </summary>
    /// <param name="Width">Width of the thermal array (number of columns).</param>
    /// <param name="Height">Height of the thermal array (number of rows).</param>
    /// <param name="UpdateRateHz">Update rate in Hz.</param>
    /// <param name="VerticalFOVDeg">Vertical field of view in degrees.</param>
    /// <param name="HorizontalFOVDeg">Horizontal field of view in degrees.</param>
    /// <param name="MinTempCelsius">Minimum measurable temperature in Celsius.</param>
    /// <param name="MaxTempCelsius">Maximum measurable temperature in Celsius.</param>
    /// <param name="ResolutionCelsius">Temperature resolution in Celsius.</param>
    public record Specifications(int Width, int Height, float UpdateRateHz, float VerticalFOVDeg, float HorizontalFOVDeg, float MinTempCelsius, float MaxTempCelsius, float ResolutionCelsius);
    
    /// <summary>
    /// Get the current sensor specifications.
    /// </summary>
    public abstract Specifications CurrentSpecifications();
    
    /// <summary>
    /// Read a single thermal measurement from the sensor, returning a 2D array of temperatures in Celsius.
    /// </summary>
    /// <param name="TimeoutMs">Maximum time to wait for a measurement in milliseconds (default: 1000ms).</param>
    /// <param name="token">Optional cancellation token.</param>
    /// <returns>A 2D array of temperatures in Celsius. The array dimensions match the sensor specifications (Height x Width).</returns>
    public abstract float[,] ReadOnce(int TimeoutMs = 1000, CancellationToken token = default);
}

/// <summary>
/// Base class for I2C human presence sensors.
/// </summary>
/// <remarks>
/// Implementors should provide sensor specifications in <see cref="CurrentSpecifications"/> and implement measurement logic in <see cref="ReadOnce"/>.
/// </remarks>
public abstract class II2CHumanPresenceSensor : II2CDevice
{
    public II2CHumanPresenceSensor(int address) : base(address)
    {
        Type = DeviceType.HumanPresence;
    }

    /// <summary>
    /// Human presence sensor measurement data.
    /// </summary>
    /// <param name="PresenceDetected">True if human presence is detected.</param>
    /// <param name="MotionDetected">True if motion is detected.</param>
    /// <param name="AmbientShockDetected">True if ambient temperature shock is detected.</param>
    /// <param name="AmbientTemperatureCelsius">Ambient temperature in Celsius.</param>
    /// <param name="ObjectTemperatureCelsius">Absolute object (human) temperature in Celsius.</param>
    /// <param name="PresenceValue">Raw presence value (sensor-specific).</param>
    /// <param name="MotionValue">Raw motion value (sensor-specific).</param>
    /// <param name="AmbientShockValue">Raw ambient shock value (sensor-specific).</param>
    public record PresenceMeasurement(
        bool PresenceDetected,
        bool MotionDetected,
        bool AmbientShockDetected,
        float AmbientTemperatureCelsius,
        float ObjectTemperatureCelsius,
        int PresenceValue,
        int MotionValue,
        int AmbientShockValue
    );

    /// <summary>
    /// Human presence sensor specifications configuration.
    /// </summary>
    /// <param name="UpdateRateHz">Update rate in Hz.</param>
    /// <param name="VerticalFOVDeg">Vertical field of view in degrees.</param>
    /// <param name="HorizontalFOVDeg">Horizontal field of view in degrees.</param>
    /// <param name="MinTempCelsius">Minimum measurable temperature in Celsius.</param>
    /// <param name="MaxTempCelsius">Maximum measurable temperature in Celsius.</param>
    /// <param name="ResolutionCelsius">Temperature resolution in Celsius.</param>
    /// <param name="DetectionRangeMeters">Maximum detection range in meters.</param>
    public record Specifications(
        float UpdateRateHz,
        float VerticalFOVDeg,
        float HorizontalFOVDeg,
        float MinTempCelsius,
        float MaxTempCelsius,
        float ResolutionCelsius,
        float DetectionRangeMeters
    );

    /// <summary>
    /// Get the current sensor specifications.
    /// </summary>
    public abstract Specifications CurrentSpecifications();

    /// <summary>
    /// Read a single presence measurement from the sensor.
    /// </summary>
    /// <param name="TimeoutMs">Maximum time to wait for a measurement in milliseconds (default: 1000ms).</param>
    /// <param name="token">Optional cancellation token.</param>
    /// <returns>A PresenceMeasurement record containing presence, motion, and temperature data.</returns>
    public abstract PresenceMeasurement ReadOnce(int TimeoutMs = 1000, CancellationToken token = default);
}

// Fallback class for unknown devices that respond on the bus but do not match any known device signature
public class UnknownII2CDevice : II2CDevice
{
    public UnknownII2CDevice(int address): base(address)
    {
        Name = "<Unknown Device>";
    }
}
