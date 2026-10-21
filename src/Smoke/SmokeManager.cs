using System.Numerics;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace AntiWallHack;

internal sealed class SmokeManager
{
    private readonly ISwiftlyCore core;
    private readonly Config settings;
    private readonly SmokeVolume?[] volumes = new SmokeVolume?[32];
    private readonly byte[] frameBuffer = new byte[SmokeSnapshotReader.FrameBytes];
    private readonly List<(Vector3 Center, float Time)> explosions = [];
    private bool available;
    private bool captured;
    private int count;
    private int cellBudget;
    private float now;
    private float disabledUntil;
    private float previousTime;

    public SmokeManager(ISwiftlyCore core, Config settings)
    {
        this.core = core;
        this.settings = settings;

        if (!settings.SmokeOcclusion) 
            return;

        string reason;

        try
        {
            nint address = core.Memory.GetVTableAddress("server", "CSource2GameEntities") ?? 0;
            available = SmokeCompatibility.Verify(address, out reason);
        }
        catch (Exception e) { available = false; reason = e.Message; }
        
        if (!available)
            core.Logger.LogInformation("Smoke filtering disabled: {Reason}.", reason);
    }

    public void BeginTick()
    {
        captured = false;
        count = 0;
        // Separate fixed ceiling for voxel work, uncertainty always reveals.
        cellBudget = 262144;
    }

    public void OnExplosion(Vector3 center, float time)
    {
        if (!available || !SmokeOcclusion.Finite(center) || !float.IsFinite(time)) 
            return;

        if (explosions.Count >= 64) { explosions.Clear(); disabledUntil = time + 3; }
        explosions.Add((center, time));
        captured = false;
    }

    private void Capture()
    {
        captured = true;
        count = 0;
        now = core.Engine.GlobalVars.CurrentTime;

        if (!float.IsFinite(now)) 
            return;

        if (now < previousTime) { explosions.Clear(); disabledUntil = 0; }
        previousTime = now;

        if (now < disabledUntil) 
            return;

        explosions.RemoveAll(e => now - e.Time > 3 || now < e.Time);
        try
        {
            foreach (var entity in core.EntitySystem.GetAllEntitiesByClass<CSmokeGrenadeProjectile>())
            {
                if (!entity.IsValid || !entity.DidSmokeEffect) 
                    continue;

                if (count == volumes.Length) 
                { 
                    count = 0; 
                    return; 
                }

                var volume = volumes[count] ??= new SmokeVolume();

                if (!SmokeSnapshotReader.Read(entity.Address, OperatingSystem.IsWindows() ? SmokeCompatibility.WindowsVolumeOffset : SmokeCompatibility.LinuxVolumeOffset, now, volume, frameBuffer))
                {
                    Disable("invalid or unreadable smoke snapshot"); 
                    return;
                }
                // Preserve HE openings across player death/spawn resets.
                if (!explosions.Any(e => SmokeOcclusion.ClearedByExplosion(volume, e.Center, e.Time, now)))
                    count++;
            }
        }
        catch (Exception e) { Disable(e.Message); }
    }

    private void Disable(string reason)
    {
        count = 0; available = false;
        core.Logger.LogWarning("Smoke filtering disabled until plugin reload: {Reason}. Wall filtering continues.", reason);
    }

    public bool IsBlocked(Vector3 start, Vector3 end, float prediction)
    {
        if (!available || !settings.SmokeOcclusion || !float.IsFinite(prediction)) return false;
        if (!captured) Capture();

        float total = 0;

        for (int i = 0; i < count; i++)
        {
            var volume = volumes[i]!;

            if (!SmokeOcclusion.Clip(volume.Center, start, end - start, out _, out _)) 
                continue;

            if (cellBudget < 128) 
                return false;

            cellBudget -= 128;

            total += SmokeOcclusion.Contribution(volume, start, end, prediction);

            if (total >= 0.2f) 
                return true;
        }
        
        return false;
    }
}

