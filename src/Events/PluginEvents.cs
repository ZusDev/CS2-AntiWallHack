using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Misc;

namespace AntiWallHack;

public sealed partial class Plugin
{
    private readonly List<Guid> eventHooks = [];

    private void RegisterEvents()
    {
        Core.Event.OnMapLoad += OnMapLoad;
        Core.Event.OnMapUnload += OnMapUnload;
        Core.Event.OnWorldUpdate += OnWorldUpdate;
        Core.Event.OnTick += OnGameTick;
        
        eventHooks.Add(Core.GameEvent.HookPost<EventPlayerSpawn>(OnPlayerSpawn));
        eventHooks.Add(Core.GameEvent.HookPost<EventPlayerDeath>(OnPlayerDeath));
        eventHooks.Add(Core.GameEvent.HookPost<EventPlayerDisconnect>(OnPlayerDisconnect));
        eventHooks.Add(Core.GameEvent.HookPost<EventRoundStart>(OnRoundStart));
        eventHooks.Add(Core.GameEvent.HookPost<EventRoundEnd>(OnRoundEnd));
        eventHooks.Add(Core.GameEvent.HookPost<EventHegrenadeDetonate>(OnHeDetonate));
        eventHooks.Add(Core.GameEvent.HookPost<EventServerSpawn>(OnServerSpawn));
    }

    private void UnregisterEvents()
    {
        Core.Event.OnMapLoad -= OnMapLoad;
        Core.Event.OnMapUnload -= OnMapUnload;
        Core.Event.OnWorldUpdate -= OnWorldUpdate;
        Core.Event.OnTick -= OnGameTick;

        foreach (var eventId in eventHooks)
            Core.GameEvent.Unhook(eventId);

        eventHooks.Clear();
    }

    private HookResult OnPlayerSpawn(EventPlayerSpawn _) => ResetVisibility();
    private HookResult OnHeDetonate(EventHegrenadeDetonate e)
    {
        visibility?.OnSmokeExplosion(new(e.X, e.Y, e.Z), Core.Engine.GlobalVars.CurrentTime);
        return HookResult.Continue;
    }
    private HookResult OnPlayerDeath(EventPlayerDeath _) => ResetVisibility();
    private HookResult OnPlayerDisconnect(EventPlayerDisconnect _) => ResetVisibility();

    private HookResult OnRoundStart(EventRoundStart _)
    {
        roundEnded = false;
        return ResetVisibility();
    }

    private HookResult OnRoundEnd(EventRoundEnd _)
    {
        roundEnded = true;
        return ResetVisibility();
    }

    private HookResult ResetVisibility()
    {
        visibility?.Reset();
        return HookResult.Continue;
    }

    private void OnMapLoad(IOnMapLoadEvent e)
    {
        roundEnded = false;
        visibility?.Reset();
        ConfigureTransmitConvar();
        SetMap(e.MapName, Core.Engine.WorkshopId);
    }

    private void OnMapUnload(IOnMapUnloadEvent _)
    {
        visibility?.Reset();
        mapLoaded = false;
        mapUnloading = true;
        physicsReady = false;
        mapName = "";
        maps?.ChangeMap("");
    }

    private HookResult OnServerSpawn(EventServerSpawn e)
    {
        SetMap(e.MapName, e.AddonName);
        physicsReady = mapLoaded;
        return HookResult.Continue;
    }

    private void SetMap(string name, string addon)
    {
        string identity = MapIdentity.Resolve(name, addon == "0" ? "" : addon);
        mapUnloading = false;
        mapLoaded = identity.Length != 0;

        if (mapName == identity)
        {
            return;
        }
        mapName = identity;
        physicsReady = false;
        maps?.ChangeMap(identity);
        visibility?.Reset();

        if (config.Enabled)
        {
            maps?.Tick(false, Environment.TickCount64);
        }
    }

    private void OnWorldUpdate()
    {
        if (!config.Enabled || maps == null || !mapLoaded)
        {
            return;
        }

        // Server spawn or a processed game tick establishes readiness. World
        // components are not a reliable readiness signal in the managed entity cache.
        // Poll and publish here even when an empty server is hibernating.
        var previous = maps.Current;
        maps.Tick(physicsReady, Environment.TickCount64);

        if (!ReferenceEquals(previous, maps.Current))
        {
            visibility?.Reset();
        }
    }

    private void OnGameTick()
    {
        if (!config.Enabled || mapUnloading || physicsReady || Core.EntitySystem.GetGameRules() == null)
        {
            return;
        }

        if (!mapLoaded)
        {
            SetMap(Core.Engine.GlobalVars.MapName.ToString(), Core.Engine.WorkshopId);
        }
        
        physicsReady = mapLoaded;
    }

}

