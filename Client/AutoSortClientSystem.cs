using autoSortVintageStoryMod.Network;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace autoSortVintageStoryMod.Client;

public class AutoSortClientSystem : ModSystem
{
    private ICoreClientAPI? _capi;
    private ChestPreviewDialog? _dialog;
    private bool _enabled;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        _capi = api;
        _dialog = new ChestPreviewDialog(api);

        api.Network
            .RegisterChannel("autosort")
            .RegisterMessageType<OverlayPacket>()
            .SetMessageHandler<OverlayPacket>(OnOverlayPacket);

        api.Event.RegisterGameTickListener(OnTick, 150);
    }

    private void OnOverlayPacket(OverlayPacket packet)
    {
        _enabled = packet.Enabled;
        _capi?.Logger.Notification($"[AutoSort] Overlay packet received: enabled={_enabled}");
        if (!_enabled) _dialog?.TryClose();
    }

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
