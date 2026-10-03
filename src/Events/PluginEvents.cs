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
        
        eventHooks.Add(Core.GameEvent.HookPost<EventPlayerSpawn>(OnPlayerSpawn));
        eventHooks.Add(Core.GameEvent.HookPost<EventPlayerDeath>(OnPlayerDeath));
        eventHooks.Add(Core.GameEvent.HookPost<EventPlayerDisconnect>(OnPlayerDisconnect));
        eventHooks.Add(Core.GameEvent.HookPost<EventRoundStart>(OnRoundStart));
        eventHooks.Add(Core.GameEvent.HookPost<EventRoundEnd>(OnRoundEnd));
    }

    private void UnregisterEvents()
    {
        Core.Event.OnMapLoad -= OnMapLoad;
        Core.Event.OnMapUnload -= OnMapUnload;

        foreach (var eventId in eventHooks)
            Core.GameEvent.Unhook(eventId);

        eventHooks.Clear();
    }

    private HookResult OnPlayerSpawn(EventPlayerSpawn _) => ResetVisibility();
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

    private void OnMapLoad(IOnMapLoadEvent _)
    {
        roundEnded = false;
        visibility?.Reset();
        ConfigureTransmitConvar();
    }

    private void OnMapUnload(IOnMapUnloadEvent _)
    {
        visibility?.Reset();
    }

}

