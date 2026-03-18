// Virtual human-presence sensor built on top of any II2CDistanceSensor.
// Follows the same pattern as ThermalHumanPresenceSensor:
//   a background thread reads distance frames and runs the detection algorithm,
//   ReadOnceInternal() simply returns the last cached HumanDetection.

namespace immensive;

/// <summary>
/// Virtual human-presence sensor that derives detection from a distance sensor array
/// (e.g. TMF882X).  Detection runs in a background thread at the sensor's native frame rate.
/// </summary>
public class DistanceHumanPresenceSensor : II2CHumanDistanceSensor
{
    public II2CDistanceSensor DistanceSensor { get; private set; }

    // --- Background thread ---
    private Thread? _processingThread;
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly Lock _dataLock = new();

    // --- State ---
    private HumanDetection _lastDetection;
    private DateTime _lastUpdateTime = DateTime.MinValue;

    /// <param name="distanceSensor">Underlying distance sensor (must be initialised before use).</param>
    /// <param name="virtualAddress">Virtual I2C address assigned to this sensor instance.</param>
    public DistanceHumanPresenceSensor(II2CDistanceSensor distanceSensor, int virtualAddress)
        : base(virtualAddress)
    {
        DistanceSensor = distanceSensor ?? throw new ArgumentNullException(nameof(distanceSensor));
        Name = $"Human Distance Sensor (Virtual from {distanceSensor.Name})";
        _lastDetection = new HumanDetection(false, null, 0f);
    }

    public override bool TryDetect(int busId, CancellationToken token = default)
        => DistanceSensor.TryDetect(busId, token);

    public override void Initialize(Dictionary<string, string> config, int busId = -1, CancellationToken token = default)
    {
        StopProcessingThread();
        DistanceSensor.Initialize(config, busId, token);

        if (DistanceSensor.Status == DeviceStatus.Initialized)
        {
            _i2c = DistanceSensor.I2C;
            Initialized = true;
            StartProcessingThread();
        }
        else
        {
            Initialized = false;
        }
    }

    public override Specifications CurrentSpecifications()
    {
        var s = DistanceSensor.CurrentSpecifications();
        return new Specifications(
            UpdateRateHz:      s.UpdateRateHz,
            VerticalFOVDeg:    s.VerticalFOVDeg,
            HorizontalFOVDeg:  s.HorizontalFOVDeg,
            MaxRangeMeters:    5.0f
        );
    }

    protected override HumanDetection ReadOnceInternal(int TimeoutMs = 1000, CancellationToken token = default)
    {
        lock (_dataLock)
            return _lastDetection;
    }

    // -------------------------------------------------------------------------
    // Background processing thread
    // -------------------------------------------------------------------------

    private void StartProcessingThread()
    {
        if (_processingThread?.IsAlive == true)
            return;

        _cancellationTokenSource = new CancellationTokenSource();
        _processingThread = new Thread(() => ProcessingThreadLoop(_cancellationTokenSource.Token))
        {
            IsBackground = true,
            Name = $"DistanceHumanPresenceSensor-{Address:X2}"
        };
        _processingThread.Start();
        CustomLogger.Log(this, CustomLogger.LogLevel.Info, $"Started processing thread for {Name}");
    }

    private void StopProcessingThread()
    {
        _cancellationTokenSource?.Cancel();
        if (_processingThread?.IsAlive == true)
        {
            if (!_processingThread.Join(TimeSpan.FromSeconds(2)))
                CustomLogger.Log(this, CustomLogger.LogLevel.Warning,
                    $"Processing thread did not stop gracefully for {Name}");
        }
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
        _processingThread = null;
    }

    private void ProcessingThreadLoop(CancellationToken token)
    {
        try
        {
            int delayMs = (int)(1000.0 / DistanceSensor.FrameRateHz);
            CustomLogger.Log(this, CustomLogger.LogLevel.Info,
                $"Processing thread running at {DistanceSensor.FrameRateHz} Hz (delay: {delayMs} ms)");

            while (!token.IsCancellationRequested)
            {
                try
                {
                    var pixels = DistanceSensor.ReadOnce(delayMs * 2, token);
                    var specs  = DistanceSensor.CurrentSpecifications();
                    var detection = Update(pixels, specs);

                    lock (_dataLock)
                    {
                        _lastDetection  = detection;
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
                    Thread.Sleep(delayMs);
                }

                if (!token.IsCancellationRequested)
                    Thread.Sleep(delayMs);
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

    // -------------------------------------------------------------------------
    // Detection algorithm (placeholder)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Processes one frame of distance pixels and returns a human detection result.
    /// </summary>
    /// <remarks>
    /// Current implementation is a placeholder:
    ///   • filters pixels below a confidence threshold
    ///   • picks the closest valid pixel as the candidate
    ///   • projects it to a 3-D position using the sensor FoV
    ///
    /// TODO: replace with a proper algorithm, e.g.:
    ///   1. Pixel clustering by depth proximity
    ///   2. Silhouette classification (size / shape heuristics)
    ///   3. Multi-frame temporal tracking for presence stability
    /// </remarks>
    private static HumanDetection Update(
        List<(int distMM, float confidence)> pixels,
        II2CDistanceSensor.Specifications specs)
    {
        const float MinConfidence = 0.3f;
        const float MaxRangeMm   = 5_000f; // 5 m

        int   bestIndex = -1;
        int   bestDist  = int.MaxValue;
        float bestConf  = 0f;

        for (int i = 0; i < pixels.Count; i++)
        {
            var (distMM, conf) = pixels[i];
            if (conf < MinConfidence || distMM <= 0 || distMM > MaxRangeMm)
                continue;

            if (distMM < bestDist)
            {
                bestDist  = distMM;
                bestConf  = conf;
                bestIndex = i;
            }
        }

        if (bestIndex < 0)
            return new HumanDetection(false, null, 0f);

        var position = ProjectPixel(bestIndex, bestDist, specs);
        return new HumanDetection(true, position, bestConf);
    }

    /// <summary>
    /// Projects a pixel index and distance to a 3-D position (Z = forward axis).
    /// </summary>
    private static Position ProjectPixel(int index, int distMM, II2CDistanceSensor.Specifications specs)
    {
        float distM = distMM / 1000f;
        float hFovRad = specs.HorizontalFOVDeg * MathF.PI / 180f;
        float vFovRad = specs.VerticalFOVDeg    * MathF.PI / 180f;

        int cols = specs.Width;
        int rows = specs.Height;

        int col = index % cols;
        int row = index / cols;

        // Angular offset from sensor boresight
        float angleH = cols > 1 ? (col - (cols - 1) / 2f) / (cols - 1) * hFovRad : 0f;
        float angleV = rows > 1 ? (row - (rows - 1) / 2f) / (rows - 1) * vFovRad : 0f;

        // Spherical → Cartesian  (Z forward, X right, Y up)
        float z = distM * MathF.Cos(angleH) * MathF.Cos(angleV);
        float x = distM * MathF.Sin(angleH);
        float y = distM * MathF.Sin(angleV);

        return new Position(x, y, z);
    }
}
