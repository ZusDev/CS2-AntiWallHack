using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Convars;
using SwiftlyS2.Shared.Plugins;
using Microsoft.Extensions.Logging;

namespace AntiWallHack;

[PluginMetadata(Id = "AntiWallHack", Name = "AntiWallHack", Version = "1.0.3", Author = "M1K@c", Description = "Server-side visibility and smoke filtering for enemies.")]
public sealed partial class Plugin : BasePlugin
{
    private Config config = new();

    private PlayerVisibility? visibility;
    private TransmitHook? transmitHook;
    private IConVar? dontTransmit;

    private string? previousConvarValue;

    private bool faulted;
    private bool roundEnded;
    private VisibilityLoader? maps;
    private string mapName = "";
    private bool mapLoaded;
    private bool physicsReady;
    private bool mapUnloading;

    public Plugin(ISwiftlyCore core) : base(core){}

    public override void Load(bool hotReload)
    {
        LoadConfig();

        visibility = new PlayerVisibility(Core, config);
        string mapsDirectory = Path.Combine(Core.PluginPath, "resources", "maps");
        Directory.CreateDirectory(mapsDirectory);
        maps = new VisibilityLoader(Core.GameFilesDirectory, mapsDirectory, message => Core.Logger.LogInformation("{Message}", message), config.Debug);

        try
        {
            ConfigureTransmitConvar();
            transmitHook = new TransmitHook(Core, OnTransmit, OnFailure);
            RegisterEvents();
            
            if (hotReload)
            {
                SetMap(Core.Engine.GlobalVars.MapName.ToString(), Core.Engine.WorkshopId);
                physicsReady = mapLoaded && Core.EntitySystem.GetGameRules() != null;
            }
        }
        catch
        {
            Unload();
            throw;
        }
    }

    private void ConfigureTransmitConvar()
    {
        dontTransmit ??= Core.ConVar.FindAsString("sv_enable_donttransmit");

        if (dontTransmit == null)
        {
            Core.Logger.LogInformation("sv_enable_donttransmit is not exposed by this server. Skipping the optional convar workaround.");
            return;
        }

        previousConvarValue ??= dontTransmit.ValueAsString;
        dontTransmit.ValueAsString = "0";
    }

    private unsafe void OnTransmit(nint infos, int count)
    {
        if (faulted || !config.Enabled || visibility == null || maps?.Current is not { } map || infos == 0 || count is < 1 or > 64)
            return;

        if (!Config.CanFilterWithConvar(dontTransmit?.ValueAsString))
        {
            visibility.Reset();
            return;
        }

        var rules = Core.EntitySystem.GetGameRules();

        if (rules == null || rules.WarmupPeriod || rules.FreezePeriod || roundEnded)
        {
            visibility.Reset();
            return;
        }

        int tick = Core.Engine.GlobalVars.TickCount;

        visibility.BeginFrame(tick, map);

        // A fresh reverse-direction reveal can cancel an earlier hide in this batch.
        for (int offset = 0; offset < count; offset++)
        {
            int index = (offset + (tick & int.MaxValue) % count) % count;
            if (TransmitInfo.TryRead(((nint*)infos)[index], out int recipient, out _, out _))
            {
                visibility.EvaluateViewer(recipient, tick, map);
            }
        }

        for (int i = 0; i < count; i++)
        {
            if (!TransmitInfo.TryRead(((nint*)infos)[i], out int slot, out nint primary, out nint suppressed))
                continue;

            var entities = visibility.GetHidden(slot, tick);
            if (entities == null)
                continue;

            foreach (var entity in entities)
            {
                if (entity.IsValid)
                    TransmitMask.Withhold((uint*)primary, (uint*)suppressed, (int)entity.Index);
            }
        }
    }

    private void OnFailure(Exception error)
    {
        if (!faulted)
            Core.Logger.LogError(error, "AntiWallHack filtering stopped. Players remain visible until the plugin is reloaded.");

        faulted = true;

        visibility?.Reset();
    }

    public override void Unload()
    {
        transmitHook?.Dispose();
        transmitHook = null;

        UnregisterEvents();
        maps?.Dispose();
        maps = null;

        visibility?.Reset();

        if (previousConvarValue != null && dontTransmit != null && Config.CanFilterWithConvar(dontTransmit.ValueAsString))
        {
            dontTransmit.ValueAsString = previousConvarValue;
        }
    }
}



