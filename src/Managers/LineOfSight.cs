using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Trace;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace AntiWallHack;

internal sealed class LineOfSight
{
    private const int MaxTracesPerTick = 4096;
    private readonly ISwiftlyCore core;
    private readonly Config settings;

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
    }

    public void BeginTick()
    {
        Traces = 0;
        BudgetSkips = 0;
    }

    public bool IsHidden(PlayerSnapshot viewer, PlayerSnapshot target)
    {
        trace.EntitiesToIgnore.Clear();

        trace.EntitiesToIgnore.AddRange(viewer.Entities);
        trace.EntitiesToIgnore.AddRange(target.Entities);

        trace.OwnersToIgnore.Clear();
        
        trace.OwnersToIgnore.Add(viewer.Pawn);
        trace.OwnersToIgnore.Add(target.Pawn);

        float padding = settings.BoundsPadding;

        Vector lo = target.Mins - new Vector(padding, padding, 0);
        Vector hi = target.Maxs + new Vector(padding, padding, padding);

        Span<Vector> points = stackalloc Vector[12];

        points[0] = target.Eye;
        points[1] = target.Origin + (lo + hi) * 0.5f;

        for (int i = 0; i < 8; i++)
            points[i + 2] = target.Origin + new Vector((i & 1) == 0 ? lo.X : hi.X, (i & 2) == 0 ? lo.Y : hi.Y, (i & 4) == 0 ? lo.Z + 4 : hi.Z);

        target.Pawn.EyeAngles.ToDirectionVectors(out var forward, out _, out _);

        points[10] = target.Eye + forward * 64;
        points[11] = target.Origin + new Vector(0, 0, 32) + forward * 40;

        var lead = target.Velocity * settings.PredictionSeconds;
        var eyeLead = viewer.Velocity * settings.PredictionSeconds;

        for (int pass = 0; pass < 2; pass++)
        {
            if (pass == 1 && lead.Length() < 1 && eyeLead.Length() < 1)
                break;

            foreach (Vector sample in points)
            {
                if (Traces >= MaxTracesPerTick)
                {
                    BudgetSkips++;
                    return false;
                }

                Traces++;

                var start = viewer.Eye + (pass == 0 ? new Vector() : eyeLead);
                var end = sample + (pass == 0 ? new Vector() : lead);

                var hit = core.Trace.TraceShapeLine(start, end, trace);

                if (!VisibilityRules.IsOccluded(hit.Fraction, hit.StartInSolid))
                    return false;

                // Fail open if the engine reports an entity the filter rejected.
                if (hit.Entity is { } entity && (!IsOccluder(entity) || trace.EntitiesToIgnore.Any(ignored => ignored.Address == entity.Address)))
                    return false;
            }
        }

        return true;
    }
}


