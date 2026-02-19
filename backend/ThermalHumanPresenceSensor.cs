namespace immensive;

/// <summary>
/// Virtual human presence sensor that simulates presence detection based on a thermal sensor.
/// This class wraps a thermal sensor (like MLX90640 or AMG88XX) and derives human presence
/// information from thermal data.
/// </summary>
/// <remarks>
/// This is a proof-of-concept implementation. The actual presence detection algorithm
/// will be implemented to analyze thermal patterns and detect human presence/motion.
/// </remarks>
public class ThermalHumanPresenceSensor : II2CHumanPresenceSensor
{
    public II2CThermalSensor ThermalSensor { get; private set; }

    // Processing thread management
    private Thread? _processingThread = null;
    private CancellationTokenSource? _cancellationTokenSource = null;
    private readonly Lock _dataLock = new();
    
    // Cached measurement result
    private PresenceMeasurement _lastMeasurement;
    private DateTime _lastUpdateTime = DateTime.MinValue;

    /// <summary>
    /// Create a virtual human presence sensor based on a thermal sensor.
    /// </summary>
    /// <param name="thermalSensor">The underlying thermal sensor to use for data acquisition.</param>
    /// <param name="virtualAddress">The virtual I2C address for this human presence sensor.</param>
    public ThermalHumanPresenceSensor(II2CThermalSensor thermalSensor, int virtualAddress) : base(virtualAddress)
    {
        ThermalSensor = thermalSensor ?? throw new ArgumentNullException(nameof(thermalSensor));
        Name = $"Human Presence Sensor (Virtual from {thermalSensor.Name})";
        Type = DeviceType.HumanPresence;
        
        // Initialize with default placeholder values
        _lastMeasurement = new PresenceMeasurement(
            PresenceDetected: false,
            MotionDetected: false,
            AmbientShockDetected: false,
            AmbientTemperatureCelsius: 20.0f,
            ObjectTemperatureCelsius: 20.0f,
            PresenceValue: 0,
            MotionValue: 0,
            AmbientShockValue: 0
        );
    }

    public override bool TryDetect(int busId, CancellationToken token = default)
    {
        // Detection is delegated to the underlying thermal sensor
        return ThermalSensor.TryDetect(busId, token);
    }

    public override void Initialize(Dictionary<string, string> config, int busId = -1, CancellationToken token = default)
    {
        // Stop any existing processing thread
        StopProcessingThread();
        
        // Initialize the underlying thermal sensor
        ThermalSensor.Initialize(config, busId, token);
        
        // Set our own initialization state based on the thermal sensor
        if (ThermalSensor.Status == II2CDevice.DeviceStatus.Initialized)
        {
            _i2c = ThermalSensor.I2C;
            Initialized = true;
            
            // Start the processing thread
            StartProcessingThread();
        }
        else
        {
            Initialized = false;
        }
    }

    public override Specifications CurrentSpecifications()
    {
        // Get thermal sensor specifications and map them to presence sensor specs
        var thermalSpecs = ThermalSensor.CurrentSpecifications();
        
        return new Specifications(
            UpdateRateHz: thermalSpecs.UpdateRateHz,
            VerticalFOVDeg: thermalSpecs.VerticalFOVDeg,
            HorizontalFOVDeg: thermalSpecs.HorizontalFOVDeg,
            MinTempCelsius: thermalSpecs.MinTempCelsius,
            MaxTempCelsius: thermalSpecs.MaxTempCelsius,
            ResolutionCelsius: thermalSpecs.ResolutionCelsius,
            DetectionRangeMeters: 4.0f // Default detection range
        );
    }

    protected override PresenceMeasurement ReadOnceInternal(int TimeoutMs = 1000, CancellationToken token = default)
    {
        if (!Initialized)
            throw new InvalidOperationException("Sensor not initialized. Call Initialize() first.");
        
        lock (_dataLock)
        {
            // Return the last calculated measurement from the processing thread
            return _lastMeasurement;
        }
    }

    /// <summary>
    /// Start the background processing thread that continuously reads thermal data
    /// and updates presence detection.
    /// </summary>
    private void StartProcessingThread()
    {
        if (_processingThread != null && _processingThread.IsAlive)
            return;

        _cancellationTokenSource = new CancellationTokenSource();
        _processingThread = new Thread(() => ProcessingThreadLoop(_cancellationTokenSource.Token))
        {
            IsBackground = true,
            Name = $"ThermalHumanPresenceSensor-{Address:X2}"
        };
        _processingThread.Start();
        
        CustomLogger.Log(this, CustomLogger.LogLevel.Info, 
            $"Started processing thread for {Name}");
    }

    /// <summary>
    /// Stop the background processing thread.
    /// </summary>
    private void StopProcessingThread()
    {
        _cancellationTokenSource?.Cancel();

        if (_processingThread != null && _processingThread.IsAlive)
        {
            if (!_processingThread.Join(TimeSpan.FromSeconds(2)))
            {
                CustomLogger.Log(this, CustomLogger.LogLevel.Warning, 
                    $"Processing thread did not stop gracefully for {Name}");
            }
        }

        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
        _processingThread = null;
    }

    /// <summary>
    /// Main loop for the processing thread. Continuously reads thermal data
    /// at the sensor's nominal frequency and updates presence detection.
    /// </summary>
    private void ProcessingThreadLoop(CancellationToken token)
    {
        try
        {
            var specs = ThermalSensor.CurrentSpecifications();
            int delayMs = (int)(1000.0 / specs.UpdateRateHz);

            CustomLogger.Log(this, CustomLogger.LogLevel.Info, 
                $"Processing thread running at {specs.UpdateRateHz} Hz (delay: {delayMs} ms)");

            while (!token.IsCancellationRequested)
            {
                try
                {
                    // Read thermal data from the underlying sensor
                    var thermalData = ThermalSensor.ReadOnce(delayMs * 2, token);
                    
                    // Process the thermal data and update presence detection
                    var measurement = Update(thermalData, token);
                    
                    // Store the result
                    lock (_dataLock)
                    {
                        _lastMeasurement = measurement;
                        _lastUpdateTime = DateTime.UtcNow;
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    CustomLogger.Log(this, CustomLogger.LogLevel.Error, 
                        $"Error in processing thread for {Name}: {ex.Message}");
                    
                    // Wait before retrying
                    Thread.Sleep(delayMs);
                }

                // Wait for the next update cycle
                if (!token.IsCancellationRequested)
                {
                    Thread.Sleep(delayMs);
                }
            }
        }
        catch (Exception ex)
        {
            CustomLogger.Log(this, CustomLogger.LogLevel.Error, 
                $"Fatal error in processing thread for {Name}: {ex.Message}");
        }
        finally
        {
            CustomLogger.Log(this, CustomLogger.LogLevel.Info, 
                $"Processing thread stopped for {Name}");
        }
    }

    /// <summary>
    /// Update presence detection based on thermal data.
    /// This is where the actual presence detection algorithm will be implemented.
    /// </summary>
    /// <param name="thermalData">2D array of thermal data in Celsius.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>Updated presence measurement.</returns>
    private PresenceMeasurement Update(float[,] thermalData, CancellationToken token)
    {
        // TODO: Implement actual presence detection algorithm
        // For now, return placeholder values based on thermal data
        
        token.ThrowIfCancellationRequested();
        
        // Placeholder: Calculate some basic statistics from thermal data
        float minTemp = float.MaxValue;
        float maxTemp = float.MinValue;
        float sumTemp = 0;
        int count = 0;
        
        for (int y = 0; y < thermalData.GetLength(0); y++)
        {
            for (int x = 0; x < thermalData.GetLength(1); x++)
            {
                float temp = thermalData[y, x];
                minTemp = Math.Min(minTemp, temp);
                maxTemp = Math.Max(maxTemp, temp);
                sumTemp += temp;
                count++;
            }
        }
        
        float avgTemp = count > 0 ? sumTemp / count : 20.0f;
        
        // TODO: Real algorithm to detect:
        // - Human presence (check for warm areas in human temperature range)
        // - Motion (compare with previous frames)
        // - Ambient shock (detect sudden temperature changes)
        
        return new PresenceMeasurement(
            PresenceDetected: false,           // TODO: Implement detection
            MotionDetected: false,              // TODO: Implement motion detection
            AmbientShockDetected: false,        // TODO: Implement shock detection
            AmbientTemperatureCelsius: minTemp, // Using min temp as ambient proxy
            ObjectTemperatureCelsius: maxTemp,  // Using max temp as object proxy
            PresenceValue: 0,                   // TODO: Confidence score
            MotionValue: 0,                     // TODO: Motion intensity
            AmbientShockValue: 0                // TODO: Shock intensity
        );
    }

    /// <summary>
    /// Clean up resources including the processing thread.
    /// </summary>
    ~ThermalHumanPresenceSensor()
    {
        StopProcessingThread();
    }
}
