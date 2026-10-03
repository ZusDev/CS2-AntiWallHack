using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Convars;
using SwiftlyS2.Shared.Plugins;
using Microsoft.Extensions.Logging;

namespace AntiWallHack;

[PluginMetadata(Id = "AntiWallHack", Name = "AntiWallHack", Version = "1.0.0", Author = "M1K@c", Description = "Server-side line-of-sight filtering for nearby enemies.")]
public sealed partial class Plugin : BasePlugin
{
    private Config config = new();

    private PlayerVisibility? visibility;
    private TransmitHook? transmitHook;
    private IConVar? dontTransmit;

    private string? previousConvarValue;

    private bool faulted;
    private bool roundEnded;

    public Plugin(ISwiftlyCore core) : base(core){}

    public override void Load(bool hotReload)
    {
        LoadConfig();

        visibility = new PlayerVisibility(Core, config);

        try
        {
            ConfigureTransmitConvar();
            transmitHook = new TransmitHook(Core, OnTransmit, OnFailure);
            RegisterEvents();
            Core.Logger.LogInformation("AntiWallHack loaded. Checking the nearest {Count} enemies per viewer.", config.NearestEnemies);
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

        if (config.SetDontTransmitToZero)
        {
            previousConvarValue ??= dontTransmit.ValueAsString;
            dontTransmit.ValueAsString = "0";
        }
    }

    private unsafe void OnTransmit(nint infos, int count)
    {
        if (faulted || !config.Enabled || visibility == null || infos == 0 || count is < 1 or > 64)
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

        visibility.BeginFrame(tick);

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

        visibility?.Reset();

        if (previousConvarValue != null && dontTransmit != null && Config.CanFilterWithConvar(dontTransmit.ValueAsString))
        {
            dontTransmit.ValueAsString = previousConvarValue;
        }
    }
}


