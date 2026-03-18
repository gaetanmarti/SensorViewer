// Visitor detector for a ToF distance sensor mounted in front of an interactive screen.
//
// Use-case constraints
// ────────────────────
//  • Sensor : multi-zone ToF (e.g. TMF882X 3×3, 33°×32° FoV)
//  • Scene  : sensor faces visitors; static background (wall / screen) at ~1.5–2 m
//  • Background readings are unreliable: confidence typically < 10 %, often 0 mm / 0 %
//  • Goal   : detect a visitor, estimate distance (0.3–1.5 m), output a 3-D position
//
// Algorithm overview
// ──────────────────
//  1. Filter   — keep only cells with dist > 0 and confidence ≥ minConfDetect
//  2. Classify — a valid cell is foreground when its distance is at least
//                ΔForeground mm less than the learned per-cell background
//  3. Learn    — non-foreground cells update the per-cell background with a slow EMA
//                (only usable readings are used; unreliable / zero readings are skipped)
//  4. Presence — counter-based state machine: N frames on → presence ON,
//                M consecutive frames without candidate → presence OFF
//  5. Position — confidence-weighted centroid of foreground cells, projected to 3-D (mm),
//                then smoothed with an EMA to avoid output jitter

namespace immensive;

/// <summary>3-D vector maths for the distance tracker.</summary>
public readonly record struct Vec3(float X, float Y, float Z)
{
    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator *(float k, Vec3 v) => new(k * v.X, k * v.Y, k * v.Z);
    public static Vec3 operator /(Vec3 v, float k) => new(v.X / k, v.Y / k, v.Z / k);
    public float Norm() => MathF.Sqrt(X * X + Y * Y + Z * Z);
}

/// <summary>
/// Projects sensor zone (col, row) + distance to a 3-D point in mm.
/// Coordinate system: Z = forward (away from sensor), X = right, Y = up.
/// </summary>
public static class SensorGeometry
{
    public static Vec3 ProjectTo3D(
        int col, int row,
        int width, int height,
        float hFovDeg, float vFovDeg,
        float distanceMm)
    {
        // Cell-centre angular offsets from boresight
        float az = width  <= 1 ? 0f : ((col + 0.5f) / width  - 0.5f) * hFovDeg;
        float el = height <= 1 ? 0f : (0.5f - (row + 0.5f) / height) * vFovDeg; // Y up

        float azRad = az * MathF.PI / 180f;
        float elRad = el * MathF.PI / 180f;

        return new Vec3(
            distanceMm * MathF.Cos(elRad) * MathF.Sin(azRad),
            distanceMm * MathF.Sin(elRad),
            distanceMm * MathF.Cos(elRad) * MathF.Cos(azRad));
    }
}

/// <summary>
/// Result of one tracker update cycle.
/// <para><see cref="SmoothedPositionMm"/> is in millimetres; divide by 1000 to get metres.</para>
/// </summary>
public sealed record PersonState(
    bool    Presence,
    Vec3?   SmoothedPositionMm,
    float   Quality01);

/// <summary>
/// Detects and tracks the nearest visitor standing in front of an interactive screen.
/// </summary>
/// <remarks>
/// The tracker is stateful: it learns the background incrementally and maintains a
/// smoothed position across frames.  Call <see cref="Reset"/> to restart from scratch
/// (e.g. after a long idle period).
/// </remarks>
public sealed class ScreenVisitorTracker
{
    public sealed record Config
    {
        /// Minimum distance (mm) accepted as a valid reading — filters sensor floor noise.
        public int MinDistanceMm { get; init; } = 150;

        /// Maximum reachable distance for a visitor (mm).
        public int MaxDistanceMm { get; init; } = 5000;

        /// Minimum confidence [0–1] for a cell to enter the detection pipeline.
        public float MinConfDetect { get; init; } = 0.04f;

        /// Minimum confidence [0–1] for a cell to update the background model.
        /// Between MinConfDetect and MinConfTrack: readings in this band are neither
        /// foreground nor trusted enough to update the background (avoids corrupting
        /// the background model with flickering borderline readings).
        public float MinConfLearn { get; init; } = 0.06f;

        /// Minimum confidence [0–1] for a cell to be counted as foreground.
        /// Higher than MinConfLearn to reject borderline background readings.
        public float MinConfTrack { get; init; } = 0.08f;

        /// A cell is foreground when its distance is this many mm *closer* than background.
        /// Must be large enough to clear background noise (~100–150 mm typical).
        public int ForegroundDeltaMm { get; init; } = 250;

        /// Minimum foreground cell count to declare a candidate present.
        public int MinForegroundCells { get; init; } = 1;

        /// Frames of consecutive candidates required to turn presence ON.
        public int PresenceOnFrames { get; init; } = 3;

        /// Frames without a candidate required to turn presence OFF.
        public int PresenceOffFrames { get; init; } = 8;

        /// Background EMA learning rate (α).  Smaller = slower adaptation to scene changes.
        public float BackgroundLearnRate { get; init; } = 0.02f;

        /// Position EMA smoothing factor (α).  Higher = faster tracking, more jitter.
        public float PositionAlpha { get; init; } = 0.35f;
    }

    private readonly II2CDistanceSensor _sensor;
    private readonly Config _cfg;

    // Per-cell background estimate in mm (null = not yet learned)
    private readonly float?[] _background;

    // Presence state machine
    private int  _presenceCounter;
    private bool _presence;

    // Smoothed 3-D position output (mm)
    private Vec3? _smoothedPosition;

    public ScreenVisitorTracker(II2CDistanceSensor sensor, Config? config = null)
    {
        _sensor = sensor ?? throw new ArgumentNullException(nameof(sensor));
        _cfg    = config ?? new Config();

        var specs   = sensor.CurrentSpecifications();
        _background = new float?[specs.Width * specs.Height];
    }

    /// <summary>Resets the background model, presence state and smoothed position.</summary>
    public void Reset()
    {
        Array.Fill(_background, null);
        _presenceCounter  = 0;
        _presence         = false;
        _smoothedPosition = null;
    }

    /// <summary>Reads one frame from the sensor and updates the tracking state.</summary>
    public PersonState Update(int timeoutMs = 1000, CancellationToken token = default)
    {
        var specs = _sensor.CurrentSpecifications();
        var raw   = _sensor.ReadOnce(timeoutMs, token);

        int width  = specs.Width;
        int height = specs.Height;
        int count  = width * height;

        // ── 1. Classify cells ──────────────────────────────────────────────────

        var foreground = new List<(int index, float conf, Vec3 positionMm)>(count);

        for (int i = 0; i < count; i++)
        {
            var (distMM, conf) = raw[i];

            bool valid = distMM  >  0
                      && distMM  >= _cfg.MinDistanceMm
                      && distMM  <= _cfg.MaxDistanceMm
                      && conf    >= _cfg.MinConfDetect;

            if (!valid)
                continue;

            // A cell is foreground when it's noticeably closer than the background.
            // Unlearned cells fall back to MaxDistanceMm so visitors are detectable
            // even before the background model has converged.
            float bg = _background[i] ?? 0.0f; // ?? (float)_cfg.MaxDistanceMm;
            bool isForeground =
                conf >= _cfg.MinConfTrack
                && (bg - distMM) >= _cfg.ForegroundDeltaMm;

            if (!isForeground)
                continue;

            int col = i % width;
            int row = i / width;
            var pos = SensorGeometry.ProjectTo3D(
                col, row, width, height,
                specs.HorizontalFOVDeg, specs.VerticalFOVDeg, distMM);

            foreground.Add((i, conf, pos));
        }

        // ── 2. Background learning (non-foreground valid cells only) ───────────

        var occupiedIndices = new HashSet<int>(foreground.Select(c => c.index));

        for (int i = 0; i < count; i++)
        {
            if (occupiedIndices.Contains(i))
                continue;

            var (distMM, conf) = raw[i];

            if (distMM == 0)
            {
                distMM = _cfg.MaxDistanceMm;
                conf = _cfg.MinDistanceMm;
            }
               
            // MinConfLearn (> MinConfDetect) guards against flickering cells that
            // alternate between a borderline reading and 0/0%: those readings are
            // discarded here so they cannot corrupt the background model.
            bool learnable = distMM  >  0
                          && distMM  >= _cfg.MinDistanceMm
                          && distMM  <= _cfg.MaxDistanceMm
                          && conf    >= _cfg.MinConfLearn;

            if (!learnable)
                continue;

            _background[i] = _background[i].HasValue
                ? _cfg.BackgroundLearnRate * distMM + (1f - _cfg.BackgroundLearnRate) * _background[i]!.Value
                : distMM; // first valid reading seeds the background immediately
        }

        // ── 3. Candidate stats ─────────────────────────────────────────────────

        bool  hasCandidate = foreground.Count >= _cfg.MinForegroundCells;
        Vec3? rawPosition  = null;
        float quality      = 0f;

        if (hasCandidate)
        {
            // Confidence-weighted centroid
            float sumW  = 0f;
            Vec3  sumP  = new(0, 0, 0);
            foreach (var (_, conf, pos) in foreground)
            {
                sumP += conf * pos;
                sumW += conf;
            }
            rawPosition = sumW > 0f ? sumP / sumW : foreground[0].positionMm;

            // Quality: fraction of grid covered × average confidence
            float coverageScore = Math.Clamp(foreground.Count / (float)count, 0f, 1f);
            float confScore     = foreground.Average(c => c.conf);
            quality = Math.Clamp(0.4f * coverageScore + 0.6f * confScore, 0f, 1f);
        }

        // ── 4. Presence state machine ──────────────────────────────────────────

        if (hasCandidate)
        {
            _presenceCounter = Math.Min(_presenceCounter + 1, _cfg.PresenceOnFrames);
            if (_presenceCounter >= _cfg.PresenceOnFrames)
                _presence = true;
        }
        else if (_presence)
        {
            _presenceCounter--;
            if (_presenceCounter <= -_cfg.PresenceOffFrames)
            {
                _presence        = false;
                _presenceCounter = 0;
            }
        }
        else
        {
            _presenceCounter = 0;
        }

        // ── 5. Smooth position ─────────────────────────────────────────────────

        if (!_presence)
        {
            _smoothedPosition = null;
        }
        else if (rawPosition.HasValue)
        {
            _smoothedPosition = _smoothedPosition.HasValue
                ? Lerp(_smoothedPosition.Value, rawPosition.Value, _cfg.PositionAlpha)
                : rawPosition.Value;
        }
        // else: keep last smoothed position during brief drop-out frames

        return new PersonState(_presence, _smoothedPosition, _presence ? quality : 0f);
    }

    private static Vec3 Lerp(Vec3 a, Vec3 b, float α) =>
        new(a.X + α * (b.X - a.X), a.Y + α * (b.Y - a.Y), a.Z + α * (b.Z - a.Z));
}
