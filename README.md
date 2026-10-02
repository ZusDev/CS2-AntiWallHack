<div align="center">

<img width="96" height="auto" alt="332" src="https://github.com/user-attachments/assets/87ce35ec-7e90-4a0e-a61b-09e184a05bca" />

  
# CS2-AntiWallHack

### A Counter-Strike 2 AntiWallHack plugin built on SwiftlyS2.

**It does not detect cheats, issue bans or replace a complete anti-cheat system.**

[![Protection](https://img.shields.io/badge/protection-WallHack-6f42c1?style=for-the-badge)](#features)
[![Framework](https://img.shields.io/badge/framework-SwiftlyS2-2ea44f?style=for-the-badge)](https://github.com/swiftly-solution/swiftlys2)
[![License](https://img.shields.io/badge/license-GNU%20GPL%20v3-2ea44f?style=for-the-badge)](LICENSE)

This AC feature is taken from [CS2 AntiCheat Defense](https://github.com/ZusDev/CS2-Anticheat-Defense)
</div>

## Showcase

<table>
<tr>
<td width="50%" align="center">
<img src="img/AntiWallHack_showcase_de_mirage.gif" width="100%" alt="CS2 AntiWallHack blocking wallhack on Mirage"><br>
<strong>Showcase on Mirage</strong>
</td>
<td width="50%" align="center">
<img src="img/AntiWallHack_showcase_de_dust2.gif" width="100%" alt="CS2 AntiWallHack blocking wallhack on Dust2"><br>
<strong>Showcase on Dust2</strong>
</td>
</tr>
</table>

### A wallhack cannot render player data that has not been transmitted by the server.

AntiWallHack aims to reduce the enemy information available to wallhacks. When an enemy is completely hidden behind solid cover, the plugin can stop sending that enemy and their equipment to a player's game client. Once a visibility check finds a clear path, normal transmission is allowed again.

<img src="img/awh.png" width="1200" alt="ACD (AntiCheat Defense)">

</div>

---

> [!NOTE]
> Teammates, dead or spectator viewers, warmup, freeze time and ended rounds are not filtered. Bots can be enemy targets, but the plugin does not filter what bot viewers receive. Enemies outside the selected nearest group are left untouched.

## Requirements

- [SwiftlyS2](https://github.com/swiftly-solution/swiftlys2)

## Features

- **Checks solid cover** using the game's line-tracing API.
- **Anticipates movement** to help reduce sudden appearances around corners.
- **Keeps recently visible enemies available briefly** to reduce flickering.

## How it works

For each player, the plugin selects the nearest enemies, up to the configured count. It then follows this process:

1. **Nearby enemies stay available.** Enemies within `AlwaysVisibleDistance` bypass wall checks.
2. **Check for a clear path.** Rays run from the viewer's eyes to several points around each selected enemy.
3. **Check ahead when moving.** Additional rays use estimated future positions based on current movement speed and direction.
4. **Keep visible enemies available.** One clear or uncertain result is enough to allow normal transmission.
5. **Hide fully blocked enemies.** If every tested path is blocked and the grace period has expired, the plugin withholds that enemy and their collected equipment from that viewer.

<img src="img/wh.png" width="1200" alt="ACD (AntiCheat Defense)">

## Installation

1. Get the packaged `AntiWallHack.zip`.
2. Extract to your server's swiftlys2/plugins/ directory.
3. Restart the server.

The server files should look like this:

```text
game/csgo/addons/swiftlys2/
├── plugins/
│   └── AntiWallHack/
│       └── AntiWallHack.dll
└── config/
    └── plugins/
        └── AntiWallHack/
            └── config.jsonc
```

## Configuration

```text
addons/swiftlys2/config/plugins/AntiWallHack/config.jsonc
```

The included config.jsonc contains:

```jsonc
{
  "Enabled": true,
  "NearestEnemies": 5,
  "VisibleGraceTicks": 20,
  "BoundsPadding": 48,
  "PredictionSeconds": 0.2,
  "AlwaysVisibleDistance": 120,
  "SetDontTransmitToZero": true
}
```

| Setting | Value | What it does |
| :--- | :---: | :--- |
| `Enabled` | `true` | Turns visibility filtering on or off. |
| `NearestEnemies` | `5` | Number of nearest enemies checked for each viewer. |
| `VisibleGraceTicks` | `20` | How long an enemy stays available after a clear check. |
| `BoundsPadding` | `48` | Helping reveal corner peeks earlier. Measured in game units. |
| `PredictionSeconds` | `0.2` | How far ahead to estimate moving players' positions. `0.2` means 200 ms. |
| `AlwaysVisibleDistance` | `120` | Nearby enemies within this radius bypass wall checks, including through walls. Measured in game units. |
| `SetDontTransmitToZero` | `true` | Automatically requests the optional transmission compatibility setting when the server exposes it. |

### Adjusting corner pop-in

Larger `BoundsPadding` and `PredictionSeconds` values can make enemies available earlier. A longer `VisibleGraceTicks` period delays hiding them again. These settings trade stricter hiding for smoother appearances; prediction is an estimate, not a guarantee.

Increasing `AlwaysVisibleDistance` also reveals nearby enemies earlier, but allows them through walls within that radius.

## Performance

Ray tracing is the main workload. Open paths can finish after one ray; fully blocked enemies can need all 12 sample rays, plus another 12 for predicted positions when players move.

The plugin reuses visibility decisions within the same tick. If the shared trace budget runs out, it leaves affected enemies available instead of hiding them without a completed check. A smaller budget can reduce trace work but also reduce filtering coverage.

## FAQ

### Does this stop every wallhack?

No. It limits transmission of selected hidden enemies and their equipment. It does not filter sound, radar messages or all other information a cheat might use.

### Does smoke hide enemies from transmission?

No. Smoke is not part of the plugin's visibility checks. Window collision layers are also excluded from the ray filter.

### Why can I still see a nearby enemy through a wall with a cheat?

Enemies inside `AlwaysVisibleDistance` bypass ray checks. Other reasons include the grace period, an exhausted trace budget, or the enemy being outside the nearest group selected for checking.
