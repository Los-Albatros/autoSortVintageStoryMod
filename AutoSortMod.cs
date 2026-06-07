using autoSortVintageStoryMod.Config;
using autoSortVintageStoryMod.Network;
using autoSortVintageStoryMod.Sorting;
using Vintagestory.API.Common;
using Vintagestory.API.Common.CommandAbbr;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace autoSortVintageStoryMod;

public class AutoSortMod : ModSystem
{
    private ConfigManager? _cfg;
    private ICoreServerAPI? _api;
    // Inventory instances we've already attached a close-handler to. Keyed by the
    // inventory object (not its position) and held weakly: when a chest's chunk
    // unloads its inventory is GC'd and drops out, so on reload the fresh inventory
    // instance gets a new handler instead of being silently skipped forever.
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<InventoryBase, object> _hooked = new();
    private static readonly object Boxed = new();
    private IServerNetworkChannel? _channel;
    private readonly Dictionary<string, bool> _overlayByPlayer = new();

    public override bool ShouldLoad(EnumAppSide forSide)
        => forSide == EnumAppSide.Server;

    public override void StartServerSide(ICoreServerAPI api)
    {
        _api = api;
        _cfg = new ConfigManager(api);
        _cfg.Load();
        _cfg.Save();

        if (!_cfg.Data.Enabled)
        {
            Mod.Logger.Notification("[AutoSort] Disabled via config.");
            return;
        }

        // DidUseBlock fires server-side when a player right-clicks a block.
        // We intercept chest opens here and subscribe to OnInventoryClosed on the inventory,
        // capturing the BlockPos in a closure so the close-handler can sort and distribute.
        api.Event.DidUseBlock += OnDidUseBlock;

        _channel = api.Network
            .RegisterChannel("autosort")
            .RegisterMessageType<OverlayPacket>();

        api.ChatCommands
            .Create("autosort")
            .WithAlias("sort")
            .WithDescription("Toggle the read-only container overlay. Use 'show'/'hide' (or on/off) to set it explicitly.")
            .WithArgs(new WordArgParser("action", isMandatoryArg: false))
            .RequiresPrivilege(Privilege.chat)
            .HandleWith(OnSortCommand);

        // Re-send each player's saved overlay preference when they (re)connect, so
        // they don't have to type /sort again after every login.
        api.Event.PlayerNowPlaying += OnPlayerNowPlaying;

        Mod.Logger.Notification(
            $"[AutoSort] Loaded. Radius: {_cfg.Data.SearchRadiusBlocks} blocks, " +
            $"threshold: {_cfg.Data.SpecialisationThreshold * 100:F0}%.");
    }

    public override void Dispose()
    {
        if (_api != null && _cfg?.Data.Enabled == true)
        {
            _api.Event.DidUseBlock -= OnDidUseBlock;
            _api.Event.PlayerNowPlaying -= OnPlayerNowPlaying;
        }
        _hooked.Clear();
    }

    private const string OverlayPrefKey = "autosort_overlay";

    private TextCommandResult OnSortCommand(TextCommandCallingArgs args)
    {
        if (args.Caller.Player is not IServerPlayer player)
            return TextCommandResult.Error("Must be called by a player.");

        var action = (args.Parsers[0].GetValue() as string ?? "").ToLowerInvariant();
        bool enable = action switch
        {
            "show" or "on" or "enable" => true,
            "hide" or "off" or "disable" => false,
            // No (or unrecognised) argument → toggle the current state.
            _ => !_overlayByPlayer.GetValueOrDefault(player.PlayerUID),
        };

        _overlayByPlayer[player.PlayerUID] = enable;
        // Persist per-player so the choice survives reconnects and server restarts.
        player.WorldData.SetModdata(OverlayPrefKey, new[] { (byte)(enable ? 1 : 0) });
        _channel?.SendPacket(new OverlayPacket { Enabled = enable }, player);

        return TextCommandResult.Success(enable
            ? "Container overlay enabled. Use /sort hide to turn it off."
            : "Container overlay hidden.");
    }

    private void OnPlayerNowPlaying(IServerPlayer player)
    {
        var data = player.WorldData.GetModdata(OverlayPrefKey);
        bool enabled = data is { Length: > 0 } && data[0] == 1;
        if (!enabled) return;

        _overlayByPlayer[player.PlayerUID] = true;
        // Small delay so the client mod has finished registering its network channel.
        _api?.Event.RegisterCallback(_ =>
            _channel?.SendPacket(new OverlayPacket { Enabled = true }, player), 500);
    }

    private void OnDidUseBlock(IServerPlayer byPlayer, BlockSelection blockSel)
    {
        if (_api == null || _cfg == null) return;

        try
        {
            var pos = blockSel.Position;
            var be = _api.World.BlockAccessor.GetBlockEntity(pos);
            if (be is not IBlockEntityContainer container) return;

            var inv = container.Inventory;
            if (inv == null) return;

            // Filter to supported inventory classes
            if (!_cfg.Data.SupportedInventoryClasses.Any(cls =>
                inv.ClassName.Contains(cls, StringComparison.OrdinalIgnoreCase)))
                return;

            // All block-entity inventories in VS extend InventoryBase, which exposes
            // the OnInventoryClosed event and the Pos field.
            if (inv is InventoryBase invBase)
                SubscribeToInventory(invBase, pos);
        }
        catch (Exception ex)
        {
            _api.Logger.Warning(
                $"[AutoSort] Error hooking inventory at {blockSel.Position}: {ex.Message}");
        }
    }

    /// <summary>
    /// Attaches ONE persistent <see cref="OnInventoryClosedDelegate"/> to a chest the
    /// first time it is opened. It fires whenever any player closes the chest and runs
    /// the (idempotent) sort+distribute, so re-opening an already-sorted chest moves
    /// nothing. One handler per chest avoids the multiplayer clobbering that made the
    /// previous one-shot approach fire only intermittently.
    /// </summary>
    private void SubscribeToInventory(InventoryBase inv, BlockPos pos)
    {
        if (_hooked.TryGetValue(inv, out _)) return; // this inventory instance already hooked
        _hooked.Add(inv, Boxed);

        inv.OnInventoryClosed += _ =>
        {
            if (_api == null || _cfg == null) return;
            // Defer the heavy sort+distribute to the next server tick so closing the
            // chest returns immediately (no perceived lag). World access stays on the
            // main thread via RegisterCallback.
            _api.Event.RegisterCallback(_ =>
            {
                if (_api == null || _cfg == null) return;
                try
                {
                    NetworkDistributor.DistributeCascade(pos, _api, _cfg.Data);
                }
                catch (Exception ex)
                {
                    Mod.Logger.Warning($"[AutoSort] Error processing chest at {pos}: {ex.Message}");
                }
            }, 1);
        };
    }
}
