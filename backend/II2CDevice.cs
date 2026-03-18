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
        Environmental = 4,
        HumanDistance = 5,
    }

    /// <summary>
    /// Device type.
    /// </summary>
    public DeviceType Type { get; protected set; } = DeviceType.Unknown;

    protected I2C? _i2c = null;
    protected virtual I2C.TransferMode PreferredTransferMode => I2C.TransferMode.Auto;

    /// <summary>
    /// Frame rate in Hz for caching purposes.
    /// If set to 0 or less, caching is disabled.
    /// Derived classes should override this with their actual frame rate.
    /// </summary>
    public virtual float FrameRateHz { get; protected set; } = 0.0f;

    /// <summary>
    /// Cached measurement result to avoid duplicate reads within the same frame period.
    /// </summary>
    protected object? _cachedResult = null;
    
    /// <summary>
    /// Timestamp of the last cached result.
    /// </summary>
    protected DateTime _cacheTimestamp = DateTime.MinValue;
    
    /// <summary>
    /// Lock for thread-safe cache access.
    /// </summary>
    protected readonly Lock _cacheLock = new();

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

    /// <summary>
    /// Helper method to check if cached result is still valid based on frame rate.
    /// </summary>
    protected bool IsCacheValid()
    {
        lock (_cacheLock)
        {
            double cacheValidityMs = 1000.0 / FrameRateHz;
            return _cachedResult != null && (DateTime.UtcNow - _cacheTimestamp).TotalMilliseconds < cacheValidityMs;
        }
    }

    /// <summary>
    /// Helper method to update the cached result and timestamp.
    /// </summary>
    protected void UpdateCache(object? result)
    {
        lock (_cacheLock)
        {
            _cachedResult = result;
            _cacheTimestamp = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Helper method to get the current cached result.
    /// </summary>
    protected object? GetCachedResult()
    {
        lock (_cacheLock)
        {
            return _cachedResult;
        }
    }

    /// <summary>
    /// Generic method to handle caching logic for any measurement type.
    /// Used by derived classes to implement their ReadOnce() methods.
    /// If FrameRateHz is set to 0 or less, caching is disabled.
    /// </summary>
    protected T GetCachedOrCompute<T>(Func<T> compute) where T : notnull
    {
        // If caching is disabled, always compute fresh data
        if (FrameRateHz <= 0)
            return compute();

        lock (_cacheLock)
        {
            var now = DateTime.UtcNow;
            double cacheValidityMs = 1000.0 / FrameRateHz;
            
            if (_cachedResult is T cachedData && (now - _cacheTimestamp).TotalMilliseconds < cacheValidityMs)
            {
                return cachedData;
            }
            
            var newData = compute();
            _cachedResult = newData;
            _cacheTimestamp = DateTime.UtcNow;
            return newData;
        }
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
    
    /// <summary>
    /// Get the current sensor specifications (e.g. for point cloud projection)
    /// </summary>
    public abstract Specifications CurrentSpecifications();

    /// <summary>
    /// Override FrameRateHz to use the distance sensor's UpdateRateHz from specifications.
    /// </summary>
    public override float FrameRateHz
    {
        get => CurrentSpecifications().UpdateRateHz;
        protected set { } // Ignore sets, always use specs
    }

    /// <summary> 
    /// Read a single measurement from the sensor, returning a list of (distance in mm, confidence) tuples.
    /// Uses caching to avoid multiple I2C reads within the same frame period.
    /// </summary>
    /// <param name="TimeoutMs">Maximum time to wait for a measurement in milliseconds (default: 1000ms).</param>
    /// <param name="token">Optional cancellation token.</param>
    /// <returns>A list of (distance in mm, confidence) tuples representing the sensor measurement.</returns>
    public List<(int distMM, float confidence)> ReadOnce(int TimeoutMs = 1000, CancellationToken token = default)
    {
        if (!Initialized)
            throw new InvalidOperationException("Sensor not initialized. Call Initialize() first.");

        var cached = GetCachedOrCompute(() => ReadOnceInternal(TimeoutMs, token));
        // Return a copy to prevent external modifications
        return new List<(int distMM, float confidence)>(cached);
    }

    /// <summary>
    /// Internal method to read distance data directly from the sensor.
    /// Implementors should provide the actual sensor reading logic here.
    /// </summary>
    /// <param name="TimeoutMs">Maximum time to wait for a measurement in milliseconds.</param>
    /// <param name="token">Optional cancellation token.</param>
    /// <returns>A list of (distance in mm, confidence) tuples.</returns>
    protected abstract List<(int distMM, float confidence)> ReadOnceInternal(int TimeoutMs = 1000, CancellationToken token = default);
}

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
    /// Override FrameRateHz to use the thermal sensor's UpdateRateHz from specifications.
    /// </summary>
    public override float FrameRateHz
    {
        get => CurrentSpecifications().UpdateRateHz;
        protected set { } // Ignore sets, always use specs
    }
    
    /// <summary>
    /// Read a single thermal measurement from the sensor, returning a 2D array of temperatures in Celsius.
    /// Uses caching to avoid multiple I2C reads within the same frame period.
    /// </summary>
    /// <param name="TimeoutMs">Maximum time to wait for a measurement in milliseconds (default: 1000ms).</param>
    /// <param name="token">Optional cancellation token.</param>
    /// <returns>A 2D array of temperatures in Celsius. The array dimensions match the sensor specifications (Height x Width).</returns>
    public float[,] ReadOnce(int TimeoutMs = 1000, CancellationToken token = default)
    {
        if (!Initialized)
            throw new InvalidOperationException("Sensor not initialized. Call Initialize() first.");

        var specs = CurrentSpecifications();
        var cached = GetCachedOrCompute(() => ReadOnceInternal(TimeoutMs, token));
        
        // Return a copy to prevent external modifications
        var result = new float[specs.Height, specs.Width];
        Array.Copy(cached, result, cached.Length);
        return result;
    }

    /// <summary>
    /// Internal method to read thermal data directly from the sensor.
    /// Implementors should provide the actual sensor reading logic here.
    /// </summary>
    /// <param name="TimeoutMs">Maximum time to wait for a measurement in milliseconds.</param>
    /// <param name="token">Optional cancellation token.</param>
    /// <returns>A 2D array of temperatures in Celsius.</returns>
    protected abstract float[,] ReadOnceInternal(int TimeoutMs = 1000, CancellationToken token = default);
}

/// <summary>
/// Base class for I2C s.
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
    /// Override FrameRateHz to use the human presence sensor's UpdateRateHz from specifications.
    /// </summary>
    public override float FrameRateHz
    {
        get => CurrentSpecifications().UpdateRateHz;
        protected set { } // Ignore sets, always use specs
    }

    /// <summary>
    /// Read a single presence measurement from the sensor.
    /// Uses caching to avoid multiple readings within the same frame period.
    /// </summary>
    /// <param name="TimeoutMs">Maximum time to wait for a measurement in milliseconds (default: 1000ms).</param>
    /// <param name="token">Optional cancellation token.</param>
    /// <returns>A PresenceMeasurement record containing presence, motion, and temperature data.</returns>
    public PresenceMeasurement ReadOnce(int TimeoutMs = 1000, CancellationToken token = default)
    {
        if (!Initialized)
            throw new InvalidOperationException("Sensor not initialized. Call Initialize() first.");

        return GetCachedOrCompute(() => ReadOnceInternal(TimeoutMs, token));
    }

    /// <summary>
    /// Internal method to read presence data directly from the sensor.
    /// Implementors should provide the actual sensor reading logic here.
    /// </summary>
    /// <param name="TimeoutMs">Maximum time to wait for a measurement in milliseconds.</param>
    /// <param name="token">Optional cancellation token.</param>
    /// <returns>A PresenceMeasurement record.</returns>
    protected abstract PresenceMeasurement ReadOnceInternal(int TimeoutMs = 1000, CancellationToken token = default);
}

/// <summary>
/// Base class for I2C environmental sensors (e.g. gas, temperature, humidity, pressure).
/// </summary>
/// </summary> <remarks>
/// Implementors should provide sensor specifications in <see cref="CurrentSpecifications"/> and implement measurement logic in <see cref="ReadOnce"/>.
/// </remarks> 

public abstract class II2CEnvironmentalSensor : II2CDevice
{
    public II2CEnvironmentalSensor(int address) : base(address)
    {
        Type = DeviceType.Environmental; // Environmental sensors can be of various types, so we keep it as Unknown
    }

    /// <summary>
    /// Environmental sensor specifications configuration.
    /// </summary>
    /// <param name="HasTemperature">Indicates if the sensor can measure temperature.</param>
    /// <param name="HasHumidity">Indicates if the sensor can measure humidity.</param>
    /// <param name="HasPressure">Indicates if the sensor can measure pressure.</param>
    /// <param name="HasGas">Indicates if the sensor can measure gas.</param>
    public record Specifications(bool HasTemperature, bool HasHumidity, bool HasPressure, bool HasGas);
    
    /// <summary>
    /// Get the current sensor specifications.
    /// </summary>
    public abstract Specifications CurrentSpecifications();

    /// <summary>
    ///  Read a single environmental measurement from the sensor, returning a dictionary of measurement type to value.
    /// </summary>
    public enum MeasurementType {
        Temperature = 1,
        Humidity = 2,
        Pressure = 3,
        Gas = 4,
        IAQ = 10, // Indoor Air Quality index
    }

    /// <summary>
    /// Read a single environmental measurement from the sensor, returning a dictionary of measurement type to value.
    /// </summary>
    /// <param name="TimeoutMs">Maximum time to wait for a measurement in milliseconds (default: 1000ms).</param>
    /// <param name="token">Optional cancellation token.</param>
    /// <returns>A dictionary mapping measurement types (e.g. "Temperature") to their corresponding values.</returns>
    public abstract Dictionary<MeasurementType, float> ReadOnce(int TimeoutMs = 1000, CancellationToken token = default);

    /// <summary>
    /// Compute an indoor air quality (IAQ) index based on the sensor's gas resistance and humidity measurements.
    /// This is a simple heuristic based on the algorithm by David Bird (G6EJD).
    /// Source: https://github.com/G6EJD/BME680-Example/blob/master/ESP32_bme680_CC_demo_02.ino
    /// <br/><br/>
    /// The returned value follows the US AQI scale (0–500, lower = better):
    /// <list type="table">
    ///   <listheader><term>Range</term><description>Category</description></listheader>
    ///   <item><term>0 – 50</term>    <description>Good</description></item>
    ///   <item><term>51 – 150</term>  <description>Moderate</description></item>
    ///   <item><term>151 – 175</term> <description>Unhealthy for Sensitive Groups</description></item>
    ///   <item><term>176 – 200</term> <description>Unhealthy</description></item>
    ///   <item><term>201 – 300</term> <description>Very Unhealthy</description></item>
    ///   <item><term>301 – 500</term> <description>Hazardous</description></item>
    /// </list>
    /// </summary>
    /// <param name="gasResistanceOhms">Gas resistance measurement in Ohms. Clamped to [5 000, 50 000] Ω.</param>
    /// <param name="humidityPercent">Relative humidity in percent. Optimum is 38–42 %RH.</param>
    /// <returns>AQI-scale index (0–500) where 0 is excellent and 500 is hazardous.</returns>
    public static float CalculateAirQualityIndex(float gasResistanceOhms, float humidityPercent)
    {
        // Humidity contribution (25% weight) — optimum range is 38–42 %RH
        const float humReference = 40f;
        float humScore;
        if (humidityPercent >= 38f && humidityPercent <= 42f)
            humScore = 0.25f * 100f;
        else if (humidityPercent < 38f)
            humScore = 0.25f / humReference * humidityPercent * 100f;
        else
            humScore = ((-0.25f / (100f - humReference)) * humidityPercent + 0.416666f) * 100f;

        // Gas resistance contribution (75% weight) — clamp to [5 000 Ω, 50 000 Ω]
        const float gasLower = 5_000f;
        const float gasUpper = 50_000f;
        float gas = Math.Clamp(gasResistanceOhms, gasLower, gasUpper);
        float gasScore = (0.75f / (gasUpper - gasLower) * gas
                         - gasLower * (0.75f / (gasUpper - gasLower))) * 100f;

        // Combined percentage (0–100, 100 = excellent), then convert to AQI scale (0–500, 0 = excellent)
        float airQualityPct = Math.Clamp(humScore + gasScore, 0f, 100f);
        return (100f - airQualityPct) * 5f;
    }
}

/// <summary>
/// Base class for virtual human-presence sensors derived from a distance sensor array.
/// </summary>
/// <remarks>
/// Implementors wrap a <see cref="II2CDistanceSensor"/> and implement the detection
/// algorithm in <see cref="ReadOnceInternal"/>.
/// </remarks>
public abstract class II2CHumanDistanceSensor : II2CDevice
{
    protected II2CHumanDistanceSensor(int address) : base(address)
    {
        Type = DeviceType.HumanDistance;
    }

    /// <summary>3-D position in metres relative to the sensor origin (Z = forward).</summary>
    public record Position(float X, float Y, float Z)
    {
        /// <summary>Euclidean distance from the sensor in metres.</summary>
        public float Distance() => MathF.Sqrt(X * X + Y * Y + Z * Z);
    }

    /// <summary>Result of one human-detection frame.</summary>
    /// <param name="Presence">True if a human is detected.</param>
    /// <param name="Position">3-D position of the detected human, or null when absent.</param>
    /// <param name="Quality01">Detection confidence in [0, 1].</param>
    public record HumanDetection(bool Presence, Position? Position, float Quality01);

    /// <summary>Sensor specifications used for frame-rate caching and projection.</summary>
    public record Specifications(
        float UpdateRateHz,
        float VerticalFOVDeg,
        float HorizontalFOVDeg,
        float MaxRangeMeters);

    public abstract Specifications CurrentSpecifications();

    public override float FrameRateHz
    {
        get => CurrentSpecifications().UpdateRateHz;
        protected set { }
    }

    /// <summary>
    /// Returns the latest human-detection result, using the frame-rate cache.
    /// </summary>
    public HumanDetection ReadOnce(int TimeoutMs = 1000, CancellationToken token = default)
    {
        if (!Initialized)
            throw new InvalidOperationException("Sensor not initialized. Call Initialize() first.");

        return GetCachedOrCompute(() => ReadOnceInternal(TimeoutMs, token));
    }

    /// <summary>
    /// Internal detection logic, called by <see cref="ReadOnce"/> through the cache.
    /// </summary>
    protected abstract HumanDetection ReadOnceInternal(int TimeoutMs = 1000, CancellationToken token = default);
}

// Fallback class for unknown devices that respond on the bus but do not match any known device signature
public class UnknownII2CDevice : II2CDevice
{
    public UnknownII2CDevice(int address): base(address)
    {
        Name = "<Unknown Device>";
    }
}
