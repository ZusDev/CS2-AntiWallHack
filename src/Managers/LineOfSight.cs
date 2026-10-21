using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Trace;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace AntiWallHack;

internal sealed class LineOfSight
{
    private const int MaxTracesPerTick = 4096;
    private const float SmokeBoundsPadding = 28;
    private readonly ISwiftlyCore core;
    private readonly Config settings;
    private readonly SmokeManager smoke;
    private readonly Vector[][] samplePoints = Enumerable.Range(0, 64).Select(_ => new Vector[12]).ToArray();
    private readonly Vector[][] smokePoints = Enumerable.Range(0, 64).Select(_ => new Vector[12]).ToArray();
    private readonly bool[] samplesReady = new bool[64];
    // Encodes current/predicted pass and sample index, only ordering is cached.
    private readonly int[,] preferredSamples = new int[64, 64];

    public void Reset()
    {
        Array.Clear(preferredSamples);
        BeginTick();
    }

    private TraceParams trace = new()
    {
        // Visibility is blocked by solid geometry, not transparent windows,
        // player hitboxes, clip brushes, or carried equipment.
        InteractWith = MaskTrace.Solid,
        InteractExclude = MaskTrace.Player | MaskTrace.Hitbox | MaskTrace.Trigger | MaskTrace.PlayerClip | MaskTrace.NpcClip | MaskTrace.Window | MaskTrace.CarriedObject | MaskTrace.CarriedWeapon,
        ShouldHitEntity = IsOccluder
    };

    private static bool IsOccluder(CEntityInstance entity) => entity.IsValid && entity is not CCSPlayerPawn && entity is not CCSPlayerController && entity is not CEconEntity && entity.DesignerName != "player";

    public int Traces { get; private set; }
    public int BudgetSkips { get; private set; }

    public LineOfSight(ISwiftlyCore core, Config settings)
    {
        this.core = core;
        this.settings = settings;
        smoke = new SmokeManager(core, settings);
    }

    public void OnSmokeExplosion(System.Numerics.Vector3 center, float time) => smoke.OnExplosion(center, time);

    public void BeginTick()
    {
        Array.Clear(samplesReady);
        smoke.BeginTick();
        Traces = 0;
        BudgetSkips = 0;
    }

    public void PrepareViewer(PlayerSnapshot viewer, VisibilityMap map)
    {
        if (viewer.PredictionReady)
        {
            return;
        }

        viewer.PredictionReady = true;
        viewer.PeekCell = viewer.Cell;

        if (!settings.CornerPrediction)
        {
            return;
        }

        var velocity = viewer.Velocity;
        var requested = CornerPrediction.Lead(new(velocity.X, velocity.Y, velocity.Z), viewer.Angles.Yaw, viewer.Buttons, settings.CornerPredictionSeconds, settings.CornerMaxDistance);
        float length = requested.Length();

        if (length <= 0.01f || Traces >= MaxTracesPerTick)
        {
            return;
        }

        trace.EntitiesToIgnore.Clear();
        trace.EntitiesToIgnore.AddRange(viewer.Entities);
        trace.OwnersToIgnore.Clear();
        trace.OwnersToIgnore.Add(viewer.Pawn);
        var wanted = new Vector(requested.X, requested.Y, requested.Z);
        var endpoint = viewer.Eye + wanted;

        if (!CornerPrediction.Finite(new(endpoint.X, endpoint.Y, endpoint.Z)))
        {
            return;
        }

        Traces++;
        var hit = core.Trace.TraceShapeLine(viewer.Eye, endpoint, trace);
        viewer.EyeLead = wanted * CornerPrediction.ClipScale(length, hit.Fraction, hit.StartInSolid);

        if (viewer.Cell >= 0)
        {
            var predicted = viewer.Origin + viewer.EyeLead;
            viewer.PeekCell = map.Cell(new(predicted.X, predicted.Y, predicted.Z));
        }
    }

    public DirectVisibility Check(PlayerSnapshot viewer, PlayerSnapshot target, VisibilityMap map)
    {
        PrepareViewer(viewer, map);

        if (map.Blocked(viewer.Cell, viewer.PeekCell, target.Cell))
        {
            return DirectVisibility.Hidden;
        }

        trace.EntitiesToIgnore.Clear();

        trace.EntitiesToIgnore.AddRange(viewer.Entities);
        trace.EntitiesToIgnore.AddRange(target.Entities);

        trace.OwnersToIgnore.Clear();
        
        trace.OwnersToIgnore.Add(viewer.Pawn);
        trace.OwnersToIgnore.Add(target.Pawn);

        int viewerSlot = viewer.Player.PlayerID;
        int targetSlot = target.Player.PlayerID;

        if (!samplesReady[targetSlot])
        {
            BuildSamples(target);
        }

        var points = samplePoints[targetSlot];

        var eyeLead = viewer.EyeLead;

        int sampleCount = eyeLead.Length() < 1 ? 12 : 24;
        int preferred = preferredSamples[viewerSlot, targetSlot];

        if (preferred >= sampleCount)
        {
            preferred %= 12;
        }

        if (IsSmokeHidden(viewer, targetSlot, eyeLead, sampleCount, preferred))
        {
            return DirectVisibility.Hidden;
        }

        for (int order = 0; order < sampleCount; order++)
        {
            int sampleIndex = VisibilityRules.OrderedSample(order, preferred);
            bool predicted = sampleIndex >= 12;

            if (Traces >= MaxTracesPerTick)
            {
                BudgetSkips++;
                return DirectVisibility.Unknown;
            }

            Traces++;
            var start = viewer.Eye + (predicted ? eyeLead : new Vector());
            var end = points[sampleIndex % 12];

            if (!CornerPrediction.Finite(new(start.X, start.Y, start.Z)) || !CornerPrediction.Finite(new(end.X, end.Y, end.Z)))
            {
                return DirectVisibility.Unknown;
            }

            var hit = core.Trace.TraceShapeLine(start, end, trace);

            if (!VisibilityRules.IsOccluded(hit.Fraction, hit.StartInSolid))
            {
                bool clear = !hit.StartInSolid && float.IsFinite(hit.Fraction) && hit.Fraction >= 0.99f && hit.Fraction <= 1;

                if (!clear)
                {
                    return DirectVisibility.Unknown;
                }

                preferredSamples[viewerSlot, targetSlot] = sampleIndex;

                return DirectVisibility.Visible;
            }

            if (hit.Entity is { } entity && (!IsOccluder(entity) || trace.EntitiesToIgnore.Any(ignored => ignored.Address == entity.Address)))
            {
                return DirectVisibility.Unknown;
            }
        }

        return DirectVisibility.Hidden;
    }

    private bool IsSmokeHidden(PlayerSnapshot viewer, int targetSlot, Vector eyeLead, int sampleCount, int preferred)
    {
        for (int order = 0; order < sampleCount; order++)
        {
            int index = VisibilityRules.OrderedSample(order, preferred);
            bool predicted = index >= 12;
            var start = viewer.Eye + (predicted ? eyeLead : new Vector());
            var end = smokePoints[targetSlot][index % 12];

            if (!smoke.IsBlocked(new(start.X, start.Y, start.Z), new(end.X, end.Y, end.Z), predicted ? settings.CornerPredictionSeconds : 0))
            {
                return false;
            }
        }

        return true;
    }

    private void BuildSamples(PlayerSnapshot target)
    {
        target.Angles.ToDirectionVectors(out var forward, out _, out _);
        FillSamples(target, forward, settings.BoundsPadding, samplePoints[target.Player.PlayerID]);
        FillSamples(target, forward, SmokeBoundsPadding, smokePoints[target.Player.PlayerID]);
        samplesReady[target.Player.PlayerID] = true;
    }

    private static void FillSamples(PlayerSnapshot target, Vector forward, float padding, Vector[] points)
    {
        Vector lo = target.Mins - new Vector(padding, padding, 0);
        Vector hi = target.Maxs + new Vector(padding, padding, padding);
        points[0] = target.Eye;
        points[1] = target.Origin + (lo + hi) * 0.5f;

        for (int i = 0; i < 8; i++)
        {
            points[i + 2] = target.Origin + new Vector((i & 1) == 0 ? lo.X : hi.X, (i & 2) == 0 ? lo.Y : hi.Y, (i & 4) == 0 ? lo.Z + 4 : hi.Z);
        }
        
        points[10] = target.Eye + forward * 64;
        points[11] = target.Origin + new Vector(0, 0, 32) + forward * 40;
    }
}
