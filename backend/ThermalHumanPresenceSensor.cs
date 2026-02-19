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
            int delayMs = (int)(1000.0 / ThermalSensor.FrameRateHz);

            CustomLogger.Log(this, CustomLogger.LogLevel.Info, 
                $"Processing thread running at {ThermalSensor.FrameRateHz} Hz (delay: {delayMs} ms)");

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

    private class Pixel
    {
        public const float ErrorTemperature = -1000.0f;
        public const float AlphaInactive = 0.01f;
        public const float AlphaActive = AlphaInactive / 100;
        public const float HumanPresenceTemperatureDiff = 4.0f; // Minimum temperature difference to consider human presence
        public const float PresenceThreshold = 100f; // Minimum activity value to consider presence (tuned based on sensor characteristics)
        public const float MotionThreshold = 100f; // Minimum motion value to consider motion (tuned based on sensor characteristics)    
        public const float AmbientShockThreshold = 500f; // Minimum ambient shock value to consider shock (tuned based on sensor characteristics)    


        public float BackgroundTemperature { get; private set; } = ErrorTemperature;

        public float Temperature { get; private set; } = ErrorTemperature;

        public float PrevTemperature { get; private set; } = ErrorTemperature;
        public long Counter { get; private set; } = 0;
    
        public bool IsActive { get; private set; } = false;

        public float DiffBackground { get {
            return Temperature == ErrorTemperature || BackgroundTemperature == ErrorTemperature ? 0.0f : 
                Temperature - BackgroundTemperature;
        } }
        public float DiffPrev { get {
            return Temperature == ErrorTemperature || PrevTemperature == ErrorTemperature ? 0.0f :
                Temperature - PrevTemperature;
        } }

        // --- Noise estimation algo ---
        /*
        public const float SensorTypicalNoise = 0.50f;

        private const int noiseSamples = 64;
        private readonly float[] _noiseBuffer = new float[noiseSamples];
        private int _noiseIndex = 0;
        private int _noiseCount = 0;

        public float Noise { get; private set; } = SensorTypicalNoise;

        private void ComputePixelNoise(float diff)
        {
            _noiseBuffer[_noiseIndex] = diff;
            _noiseIndex = (_noiseIndex + 1) % noiseSamples;
            _noiseCount = Math.Min(_noiseCount + 1, noiseSamples);

            if (_noiseCount < noiseSamples) {
                Noise = SensorTypicalNoise;
                return;
            }

            // Median
            var sorted = _noiseBuffer.OrderBy(x => x).ToArray();
            var median = sorted[noiseSamples / 2];

            // MAD
            var absDev = _noiseBuffer.Select(x => Math.Abs(x - median)).OrderBy(x => x).ToArray();
            var mad = absDev[noiseSamples / 2];

            Noise = 1.4826f * mad;
        }
        */
        
        public void Reset()
        {
            Counter = 0;
            //_noiseIndex = _noiseCount = 0;
            //Noise = SensorTypicalNoise;
        }

        // From AMG88 datasheet:
        // "Temperature accuracy is typically ±2.5 °C"
        // "To have more than 4 °C of temperature difference from background"
        public void Update(float newTemperature, float threshold = HumanPresenceTemperatureDiff)
        {
            
            PrevTemperature = Temperature;
            Temperature = newTemperature;

            if (Counter++ == 0)
            {
                BackgroundTemperature = newTemperature;    
                IsActive = false;
                return;
            }
            
            IsActive = DiffBackground > threshold;

            // This code use the MAD method to estimate pixel noise and define if pixel is active or not.
            //if (!IsActive)
            //    ComputePixelNoise(diff);

            var alpha = IsActive ? AlphaActive : AlphaInactive;
            BackgroundTemperature = BackgroundTemperature * (1 - alpha) + Temperature * alpha;
        }
    }

    Pixel[,]? _pixels = null;

    /// <summary>
    /// Update presence detection based on thermal data.
    /// This is where the actual presence detection algorithm will be implemented.
    /// </summary>
    /// <param name="thermalData">2D array of thermal data in Celsius.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>Updated presence measurement.</returns>
    private PresenceMeasurement Update(float[,] thermalData, CancellationToken token)
    {
        int height = thermalData.GetLength(0);
        int width  = thermalData.GetLength(1);

        if (_pixels == null) {
            _pixels = new Pixel[height, width];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    _pixels[y, x] = new Pixel();
        }
        else if (_pixels.GetLength(0) != height || _pixels.GetLength(1) != width)
            throw new InvalidOperationException($"Thermal data dimensions ({height}x{width}) do not match pixel array dimensions ({_pixels.GetLength(0)}x{_pixels.GetLength(1)})");
    
        token.ThrowIfCancellationRequested();
        
        // Placeholder: Calculate some basic statistics from thermal data
        float sumBackgroundTemp = 0.0f;
        float sumObjectTemp = 0.0f;
        float sumSqDiffBackground = 0.0f;
        float sumSqDiffPrev = 0.0f;
        float sumDiffPrev = 0.0f;
        float sumAbsDiffPrev = 0.0f;
        int   countBackground = 0;
        int   countObject = 0;
        
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float temp = thermalData[y, x];
                var pixel = _pixels[y, x];
                pixel.Update(temp);
                sumSqDiffBackground += pixel.DiffBackground * pixel.DiffBackground;
                var diffPrev = pixel.DiffPrev;
                sumSqDiffPrev += diffPrev * diffPrev;
                sumDiffPrev += diffPrev;
                sumAbsDiffPrev += Math.Abs(diffPrev);
                if (pixel.IsActive)
                {
                    sumObjectTemp += temp;
                    countObject++;
                }
                else
                {
                    sumBackgroundTemp += temp;
                    countBackground++;
                }
            }

        float presenceRMS = (float)Math.Sqrt((double) sumSqDiffBackground);
        float motionRMS   = (float)Math.Sqrt((double) sumSqDiffPrev);

        int count = countBackground + countObject;
        const float scale = 1000.0f; // Scale activity to a more human-friendly range

        float A = sumAbsDiffPrev / count;
        float C = MathF.Abs(sumDiffPrev / count) / (A + float.Epsilon); // cohérence 0..1
        int ambientShock = (int)(scale * A * C);

        var tempBackground = countBackground > 0 ? sumBackgroundTemp / countBackground : 0.0f;
        var tempObject = countObject > 0 ? sumObjectTemp / countObject : tempBackground;
        
        //CustomLogger.Log(this, CustomLogger.LogLevel.Info, 
        //    $"Presence update: presence={presenceRMS:F2}, motion={motionRMS:F2}, ambientShock={ambientShock:F2}");

        // - Human presence (check for warm areas in human temperature range)
        int presence = (int)(scale * presenceRMS / (float) count);
        int motion = (int)(scale * motionRMS / (float) count);

        return new PresenceMeasurement(
            PresenceDetected: presence > Pixel.PresenceThreshold,     
            MotionDetected: motion > Pixel.MotionThreshold,
            AmbientShockDetected: ambientShock > Pixel.AmbientShockThreshold,
            AmbientTemperatureCelsius: tempBackground,
            ObjectTemperatureCelsius: tempObject,  
            PresenceValue: presence,  
            MotionValue: motion,                   
            AmbientShockValue: ambientShock                
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
