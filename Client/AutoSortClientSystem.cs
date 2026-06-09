using autoSortVintageStoryMod.Network;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace autoSortVintageStoryMod.Client;

public class AutoSortClientSystem : ModSystem
{
    private ICoreClientAPI? _capi;
    private ChestPreviewDialog? _dialog;
    private IClientNetworkChannel? _channel;
    private bool _enabled;

    /// <summary>Latest config snapshot received from the server (for the ConfigLib screen).</summary>
    public ConfigSyncPacket? ServerConfig { get; private set; }

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        _capi = api;
        _dialog = new ChestPreviewDialog(api);

        _channel = api.Network
            .RegisterChannel("autosort")
            .RegisterMessageType<OverlayPacket>()
            .RegisterMessageType<ConfigSyncPacket>()
            .RegisterMessageType<ConfigChangePacket>()
            .SetMessageHandler<OverlayPacket>(OnOverlayPacket)
            .SetMessageHandler<ConfigSyncPacket>(OnConfigSync);

        api.Event.RegisterGameTickListener(OnTick, 150);

        // Settings screen via ConfigLib, only if that mod is present.
        if (api.ModLoader.IsModEnabled("configlib"))
        {
            try { ConfigLibIntegration.Register(api, this); }
            catch (System.Exception ex) { Mod.Logger.Warning("AutoSort: ConfigLib integration failed: " + ex.Message); }
        }
    }

    private void OnOverlayPacket(OverlayPacket packet)
    {
        _enabled = packet.Enabled;
        if (!_enabled) _dialog?.TryClose();
    }

    private void OnConfigSync(ConfigSyncPacket packet)
    {
        ServerConfig = packet;
        _enabled = packet.OverlayEnabled;
        ConfigLibIntegration.InvalidateCache();
    }

    /// <summary>Sends a change request to the server (overlay toggle and/or admin config).</summary>
    public void SendChange(ConfigChangePacket packet) => _channel?.SendPacket(packet);

    private void OnTick(float dt)
    {
        if (_enabled) _dialog?.Refresh();
    }

    public override void Dispose()
    {
        _dialog?.TryClose();
        _dialog?.Dispose();
    }
}
