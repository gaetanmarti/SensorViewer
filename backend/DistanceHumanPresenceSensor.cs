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

    // --- Tracker ---
    private ScreenVisitorTracker? _tracker;

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

            var specs = DistanceSensor.CurrentSpecifications();
            _ = specs; // ScreenVisitorTracker reads specs internally
            _tracker = new ScreenVisitorTracker(DistanceSensor);

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
                    var state = _tracker!.Update(delayMs * 2, token);
                    var detection = ToHumanDetection(state);

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
    // PersonState → HumanDetection conversion
    // -------------------------------------------------------------------------

    private static HumanDetection ToHumanDetection(PersonState state)
    {
        if (!state.Presence || state.SmoothedPositionMm is null)
            return new HumanDetection(false, null, state.Quality01);

        var p = state.SmoothedPositionMm.Value;
        // HumanTracker works in mm; Position is in metres
        return new HumanDetection(
            true,
            new Position(p.X / 1000f, p.Y / 1000f, p.Z / 1000f),
            state.Quality01);
    }
}
