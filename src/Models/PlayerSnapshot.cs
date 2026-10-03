using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace AntiWallHack;

internal sealed class PlayerSnapshot
{
    public required IPlayer Player { get; init; }
    public required CCSPlayerPawn Pawn { get; init; }
    public required Vector Origin { get; init; }
    public required Vector Eye { get; init; }
    public required Vector Mins { get; init; }
    public required Vector Maxs { get; init; }
    public required Vector Velocity { get; init; }
    public required byte Team { get; init; }
    public List<CEntityInstance> Entities { get; } = [];
    public bool Complete { get; set; } = true;
}
