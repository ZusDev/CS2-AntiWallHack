using SwiftlyS2.Shared;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace AntiWallHack;

internal sealed class PlayerVisibility
{
    private readonly ISwiftlyCore core;
    private readonly Config settings;
    private readonly PlayerSnapshot?[] players = new PlayerSnapshot?[64];
    private readonly List<CEntityInstance>[] hidden = Enumerable.Range(0, 64) .Select(_ => new List<CEntityInstance>()).ToArray();

    private readonly bool[] evaluated = new bool[64];
    private readonly PairVisibility pairs = new();
    private bool warnedInput;
    private readonly (ulong Session, uint Pawn, byte Team)[] identities = new (ulong, uint, byte)[64];
    private readonly LineOfSight sight;

    private int frameTick = -1;

    public int Traces => sight.Traces;
    public int BudgetSkips => sight.BudgetSkips;
    public int HiddenPairs { get; private set; }
    public void OnSmokeExplosion(System.Numerics.Vector3 center, float time) => sight.OnSmokeExplosion(center, time);

    public PlayerVisibility(ISwiftlyCore core, Config settings)
    {
        this.core = core;
        this.settings = settings;
        sight = new LineOfSight(core, settings);
    }

    public void Reset()
    {
        frameTick = -1;

        Array.Clear(players);
        Array.Clear(identities);
        pairs.Reset();
        Array.Clear(evaluated);

        foreach (var row in hidden)
            row.Clear();

        sight.Reset();

        HiddenPairs = 0;
    }

    private static bool Finite(Vector v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    public void BeginFrame(int tick, VisibilityMap map)
    {
        if (frameTick == tick)
            return;

        if (tick < frameTick)
            Reset();

        frameTick = tick;

        Array.Clear(players);
        Array.Clear(evaluated);

        foreach (var row in hidden)
            row.Clear();

        pairs.BeginTick();
        sight.BeginTick();
        HiddenPairs = 0;

        foreach (var player in core.PlayerManager.GetAllValidPlayers())
        {
            int slot = player.PlayerID;
            var pawn = player.PlayerPawn;

            if (slot is < 0 or >= 64 || !Live(player, pawn) || pawn == null || pawn.TeamNum is not (2 or 3) || pawn.AbsOrigin is not Vector origin || pawn.EyePosition is not Vector eye || pawn.Collision is not { } bounds || pawn.Identity is not { } pawnIdentity)
                continue;

            if (!Finite(origin) || !Finite(eye) || !Finite(bounds.Mins) || !Finite(bounds.Maxs) || !Finite(pawn.AbsVelocity) || !float.IsFinite(pawn.EyeAngles.Pitch) || !float.IsFinite(pawn.EyeAngles.Yaw) || !float.IsFinite(pawn.EyeAngles.Roll) || bounds.Mins.X >= bounds.Maxs.X || bounds.Mins.Y >= bounds.Maxs.Y || bounds.Mins.Z >= bounds.Maxs.Z)
                continue;

            var identity = (player.SessionId, pawnIdentity.EntityHandle.Raw, pawn.TeamNum);

            if (identities[slot] != identity)
            {
                identities[slot] = identity;
                pairs.Invalidate(slot, tick, settings.VisibleGraceTicks);
            }

            var frame = new PlayerSnapshot
            {
                Player = player,
                Pawn = pawn,
                Origin = origin,
                Eye = eye,
                Mins = bounds.Mins,
                Maxs = bounds.Maxs,
                Team = pawn.TeamNum,
                Velocity = pawn.AbsVelocity,
                Angles = pawn.EyeAngles,
                Buttons = ReadButtons(pawn)
            };

            if (VisibilityMap.TrustHull(new(bounds.Mins.X, bounds.Mins.Y, bounds.Mins.Z), new(bounds.Maxs.X, bounds.Maxs.Y, bounds.Maxs.Z), eye.Z - origin.Z, settings.BoundsPadding, (pawn.Flags & 1) != 0))
            {
                frame.Cell = map.Cell(new(origin.X, origin.Y, origin.Z));
            }
            
            frame.Entities.Add(pawn);

            var seen = new HashSet<nint>();
            var scene = pawn.CBodyComponent?.SceneNode;

            frame.Complete = scene != null && CollectChildren(scene.Child, frame.Entities, seen, 0);

            if (pawn.WeaponServices is { } weapons)
            {
                foreach (var handle in weapons.MyWeapons)
                {
                    if (handle.Value is { IsValid: true } weapon)
                        frame.Entities.Add(weapon);
                }
            }
            else
            {
                frame.Complete = false;
            }

            players[slot] = frame;
        }

        CollectOwnedEquipment();

        for (int i = 0; i < 64; i++)
        {
            if (players[i] == null && identities[i] != default)
            {
                pairs.Invalidate(i, tick, settings.VisibleGraceTicks);
                identities[i] = default;
            }
        }
    }

    private void CollectOwnedEquipment()
    {
        // Include owned equipment that is not parented into the scene tree.
        var owners = new Dictionary<uint, PlayerSnapshot>();

        foreach (var frame in players)
        {
            if (frame != null)
                owners[frame.Pawn.Index] = frame;
        }

        foreach (var entity in core.EntitySystem.GetAllEntitiesByClass<CBaseEntity>())
        {
            // Ownership alone also matches thrown projectiles. Only equipment belongs
            // in a player's hidden bundle, world grenades must keep transmitting.
            if (entity is not CEconEntity || !entity.IsValid || !entity.OwnerEntity.IsValid)
                continue;

            if (owners.TryGetValue(entity.OwnerEntity.EntityIndex, out var owner) && entity != owner.Pawn) owner.Entities.Add(entity);
        }
    }

    private static bool CollectChildren(CGameSceneNode? node, List<CEntityInstance> entities, HashSet<nint> seen, int depth)
    {
        while (node != null)
        {
            if (depth > 16 || seen.Count >= 128 || !seen.Add(node.Address))
                return false;

            if (node.Owner is not { IsValid: true } entity)
                return false;

            if (entity is CCSPlayerPawn || entity is CCSPlayerController)
                return false;

            entities.Add(entity);

            if (!CollectChildren(node.Child, entities, seen, depth + 1))
                return false;

            node = node.NextSibling;
        }

        return true;
    }

    private static bool Live(SwiftlyS2.Shared.Players.IPlayer player, CCSPlayerPawn? pawn) => player.IsValid && pawn is { IsValid: true } && pawn.Health > 0 && pawn.LifeState == 0 && !pawn.IsSpawning && player.Controller is { IsValid: true } controller && pawn.Controller.Value?.Address == controller.Address && controller.PlayerPawn.Value?.Address == pawn.Address;

    private ulong ReadButtons(CCSPlayerPawn pawn)
    {
        if (!settings.CornerPrediction)
        {
            return 0;
        }

        try
        {
            if (pawn.MovementServices?.Buttons is { } buttons)
            {
                return buttons.ButtonStates[0];
            }
        }
        catch (Exception)
        {
            // Missing movement schema falls back to the captured velocity.
        }

        if (!warnedInput)
        {
            warnedInput = true;
            core.Logger.LogWarning("[AntiWallHack] Movement buttons unavailable; corner prediction uses velocity only.");
        }

        return 0;
    }

    private static bool Eligible(PlayerSnapshot? viewer) => viewer != null && Live(viewer.Player, viewer.Pawn) && !viewer.Player.IsFakeClient && !viewer.Player.ServerSideClient.IsHLTV && viewer.Player.ServerSideClient.DeltaTick >= 0;

    public void EvaluateViewer(int slot, int tick, VisibilityMap map)
    {
        var viewer = players[slot];

        if (!Eligible(viewer) || evaluated[slot])
        {
            return;
        }

        evaluated[slot] = true;

        for (int offset = 0; offset < 64; offset++)
        {
            int other = (offset + (tick & 63) + slot) & 63;
            var target = players[other];

            if (target == null || other == slot || target.Team == viewer!.Team)
            {
                continue;
            }

            if (pairs.NeedsQuery(slot, other, tick))
            {
                var value = target.Complete && Live(target.Player, target.Pawn) ? sight.Check(viewer, target, map) : DirectVisibility.Unknown;
                pairs.CompleteQuery(slot, other, tick, settings.VisibleGraceTicks, value);
            }
        }
    }

    // Build masks only after every recipient has had its ordinary evaluation.
    public List<CEntityInstance>? GetHidden(int slot, int tick)
    {
        var viewer = players[slot];

        if (!Eligible(viewer) || !evaluated[slot])
        {
            return null;
        }

        var result = hidden[slot];
        result.Clear();

        for (int other = 0; other < 64; other++)
        {
            var target = players[other];

            if (target == null || target.Team == viewer!.Team || !pairs.Hidden(slot, other, tick) ||
                !Live(target.Player, target.Pawn) || target.Entities.Any(entity => !entity.IsValid || entity.Index == 0 || entity.Index >= TransmitMask.EntityLimit))
            {
                continue;
            }

            result.AddRange(target.Entities);
            HiddenPairs++;
        }

        return result;
    }
}


