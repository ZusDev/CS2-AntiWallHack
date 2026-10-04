using SwiftlyS2.Shared;
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
    private readonly int[,] visibleUntil = new int[64, 64];
    private readonly (ulong Session, uint Pawn, byte Team)[] identities = new (ulong, uint, byte)[64];
    private readonly LineOfSight sight;

    private int frameTick = -1;

    public int Traces => sight.Traces;
    public int BudgetSkips => sight.BudgetSkips;
    public int HiddenPairs { get; private set; }

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
        Array.Clear(visibleUntil);
        Array.Clear(evaluated);

        foreach (var row in hidden)
            row.Clear();

        sight.Reset();

        HiddenPairs = 0;
    }

    private static bool Finite(Vector v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    public void BeginFrame(int tick)
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

        sight.BeginTick();
        HiddenPairs = 0;

        foreach (var player in core.PlayerManager.GetAllValidPlayers())
        {
            int slot = player.PlayerID;
            var pawn = player.PlayerPawn;

            if (slot is < 0 or >= 64 || !player.IsAlive || pawn == null || !pawn.IsValid || pawn.TeamNum is not (2 or 3) || pawn.AbsOrigin is not Vector origin || pawn.EyePosition is not Vector eye || pawn.Collision is not { } bounds || pawn.Identity is not { } pawnIdentity)
                continue;

            if (!Finite(origin) || !Finite(eye) || !Finite(bounds.Mins) || !Finite(bounds.Maxs) || !Finite(pawn.AbsVelocity) || !float.IsFinite(pawn.EyeAngles.Pitch) || !float.IsFinite(pawn.EyeAngles.Yaw) || !float.IsFinite(pawn.EyeAngles.Roll) || bounds.Mins.X >= bounds.Maxs.X || bounds.Mins.Y >= bounds.Maxs.Y || bounds.Mins.Z >= bounds.Maxs.Z)
                continue;

            var identity = (player.SessionId, pawnIdentity.EntityHandle.Raw, pawn.TeamNum);

            if (identities[slot] != identity)
            {
                identities[slot] = identity;
                for (int other = 0; other < 64; other++) visibleUntil[slot, other] = visibleUntil[other, slot] = tick + settings.VisibleGraceTicks;
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
                Velocity = pawn.AbsVelocity
            };

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
            if (players[i] == null)
                identities[i] = default;
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

    public List<CEntityInstance>? GetHidden(int slot, int tick)
    {
        var viewer = players[slot];

        if (viewer == null || viewer.Player.IsFakeClient || !viewer.Player.IsAlive || !viewer.Pawn.IsValid || viewer.Player.ServerSideClient.DeltaTick < 0)
            return null;

        if (evaluated[slot])
            return hidden[slot];

        evaluated[slot] = true;

        Span<int> nearest = stackalloc int[settings.NearestEnemies];
        Span<float> distances = stackalloc float[settings.NearestEnemies];

        int count = 0;

        for (int i = 0; i < 64; i++)
        {
            var target = players[i];

            if (target == null || target.Team == viewer.Team || slot == i)
                continue;

            var delta = target.Origin - viewer.Origin;

            VisibilityRules.InsertNearest(nearest, distances, ref count, i, delta.X * delta.X + delta.Y * delta.Y + delta.Z * delta.Z);
        }

        for (int i = 0; i < count; i++)
        {
            int targetSlot = nearest[i];
            var target = players[targetSlot]!;

            // Keep transmitting throughout the existing hold without spending rays.
            if (!VisibilityRules.CanHide(tick, visibleUntil[slot, targetSlot]))
                continue;

            bool occluded = target.Complete && target.Player.IsAlive && target.Pawn.IsValid && distances[i] > settings.AlwaysVisibleDistance * settings.AlwaysVisibleDistance && sight.IsHidden(viewer, target);

            if (!occluded)
            {
                visibleUntil[slot, targetSlot] = tick + settings.VisibleGraceTicks;
            }

            else
            {
                // Never partially suppress a bundle because an entity became invalid.
                if (target.Entities.Any(entity => !entity.IsValid || entity.Index == 0 || entity.Index >= TransmitMask.EntityLimit))
                    continue;
                    
                hidden[slot].AddRange(target.Entities);
                HiddenPairs++;
            }
        }

        return hidden[slot];
    }
}

