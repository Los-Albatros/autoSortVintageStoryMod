using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace autoSortVintageStoryMod.Client;

/// <summary>
/// Read-only chest preview HUD. Shows when the player aims at a chest while
/// the /sort overlay is enabled. Extends HudElement (the HUD render layer) and
/// declares itself non-grabbing so the player keeps free-look and movement.
/// Refreshes automatically when inventory changes.
/// </summary>
public class ChestPreviewDialog : HudElement
{
    private BlockPos? _lastPos;
    private string _lastHash = "";

    public ChestPreviewDialog(ICoreClientAPI api) : base(api) { }

    public override string ToggleKeyCombinationCode => null!;

    // Passive overlay: HUD draw layer, never grabs input.
    public override double DrawOrder => 0.2;
    public override bool Focusable => false;
    public override bool PrefersUngrabbedMouse => false;

    private int _missTicks;
    private const int GraceTicks = 3; // don't close on brief deselection (raycast flicker)

    public bool Refresh()
    {
        // While a real GUI is open (e.g. the chest the player just opened), the mouse
        // is ungrabbed. Hide the passive preview then; it reappears once the dialog is
        // closed and the player returns to free-look. Our own HUD does not grab the
        // mouse (PrefersUngrabbedMouse=false), so it never hides itself.
        if (!capi.Input.MouseGrabbed)
        {
            if (IsOpened()) { TryClose(); _lastPos = null; _lastHash = ""; }
            return false;
        }

        var inv = ResolveAimedInventory(out var anchorPos);

        if (inv == null)
        {
            // Grace period: a few empty ticks are tolerated before hiding, so a
            // momentary loss of block selection doesn't make the overlay flicker.
            if (IsOpened() && ++_missTicks >= GraceTicks)
            {
                TryClose(); _lastPos = null; _lastHash = ""; _missTicks = 0;
            }
            return false;
        }
        _missTicks = 0;

        bool posChanged = _lastPos == null || !_lastPos.Equals(anchorPos);
        var hash = ComputeHash(inv);
        bool contentChanged = hash != _lastHash;

        if (posChanged || contentChanged || !IsOpened())
        {
            _lastPos = anchorPos!.Copy();
            _lastHash = hash;
            LogContent(anchorPos, inv);
            Recompose(anchorPos, inv);
            if (!IsOpened()) TryOpen();
        }

        return posChanged || contentChanged;
    }

    /// <summary>
    /// Returns the inventory of the aimed block (following multiblock stubs to the
    /// control block), or null if the player isn't aiming at a container.
    /// </summary>
    private IInventory? ResolveAimedInventory(out BlockPos? anchorPos)
    {
        anchorPos = null;
        var sel = capi.World.Player.CurrentBlockSelection;
        if (sel == null) return null;

        var pos = sel.Position;
        var inv = InventoryAt(pos);
        if (inv == null)
        {
            var block = capi.World.BlockAccessor.GetBlock(pos);
            if (block is IMultiblockOffset mb)
            {
                var ctrl = mb.GetControlBlockPos(pos);
                if (ctrl != null && !ctrl.Equals(pos))
                {
                    inv = InventoryAt(ctrl);
                    if (inv != null) pos = ctrl;
                }
            }
        }
        if (inv == null) return null;

        anchorPos = pos;
        return inv;
    }

    /// <summary>
    /// Prints a compact, type-grouped summary of the chest content to the client
    /// log. Doubles as a reliable readout when the in-world panel is occluded.
    /// </summary>
    private void LogContent(BlockPos pos, IInventory inv)
    {
        var byType = new Dictionary<Sorting.SemanticType, int>();
        int stacks = 0;
        foreach (var slot in inv)
        {
            if (slot.Itemstack == null) continue;
            stacks++;
            var t = Sorting.ItemClassifier.Classify(slot.Itemstack.Collectible.Code.Path);
            byType[t] = byType.GetValueOrDefault(t) + slot.Itemstack.StackSize;
        }
        var tldr = string.Join(", ", byType.OrderBy(kv => (int)kv.Key).Select(kv => $"{kv.Key}:{kv.Value}"));
        capi.Logger.Notification($"[AutoSort] Chest {pos} — {stacks} stack(s) | {tldr}");
    }

    private IInventory? InventoryAt(BlockPos pos)
    {
        var be = capi.World.BlockAccessor.GetBlockEntity(pos);
        if (be == null) return null;

        if (be is IBlockEntityContainer container)
            return container.Inventory;

        var prop = be.GetType().GetProperty("Inventory");
        return prop?.GetValue(be) as IInventory;
    }

    private static string ComputeHash(IInventory inv)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var slot in inv)
            sb.Append(slot.Itemstack != null
                ? $"{slot.Itemstack.Collectible.Code.Path}:{slot.Itemstack.StackSize}|"
                : "_|");
        return sb.ToString();
    }

    private void Recompose(BlockPos pos, IInventory inv)
    {
        var allSlots = inv.ToList();
        int count = allSlots.Count;
        if (count == 0) return;

        int cols = ResolveColumns(pos, inv, count);
        int rows = (int)Math.Ceiling((double)count / cols);

        double pad      = GuiStyle.ElementToDialogPadding;
        double titleBar = GuiStyle.TitleBarHeight;
        double slotSize = GuiElementPassiveItemSlot.unscaledSlotSize + GuiElementItemSlotGrid.unscaledSlotPadding;

        double gridW = cols * slotSize;
        double gridH = rows * slotSize;

        // Use the exact same layout as the real container GUI (standard pad below the
        // title bar and on every side) so the panel has the same size and therefore
        // lands at the same right-middle screen position.
        double topGap = pad;
        double totalW = gridW + 2 * pad;
        double totalH = gridH + titleBar + topGap + pad;

        var slotBounds = ElementBounds.Fixed(pad, titleBar + topGap, gridW, gridH);

        var bgBounds = ElementBounds.Fixed(0, 0, totalW, totalH);
        bgBounds.WithChildren(slotBounds);

        // Explicit on-screen placement: anchored to the right-middle of the screen.
        var dialogBounds = ElementBounds
            .Fixed(0, 0, totalW, totalH)
            .WithAlignment(EnumDialogArea.RightMiddle)
            .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0);

        // Recessed darker panel behind the slots, like the real chest GUI, for depth.
        var insetBounds = ElementBounds.Fixed(pad - 2, titleBar + topGap - 2, gridW + 4, gridH + 4);

        SingleComposer = capi.Gui
            .CreateCompo("autosort-preview", dialogBounds)
            .AddShadedDialogBG(bgBounds, withTitleBar: true)
            .AddDialogTitleBar(ResolveTitle(pos), () => TryClose())
            .BeginChildElements(bgBounds)
                .AddInset(insetBounds, 3, 0.85f)
                .AddItemSlotGrid(inv, OnSendPacket, cols, slotBounds, "slots")
            .EndChildElements()
            .Compose();
    }

    /// <summary>
    /// Column count matching the real container GUI. Vintage Story containers default
    /// to 4 columns (normal chest 16→4×4, storage vessel 12→4×3) unless the block sets
    /// a "quantityColumns" attribute (e.g. trunk / double chest → 9). The attribute is
    /// a per-type tree; we take the largest value as the effective width.
    /// </summary>
    private int ResolveColumns(BlockPos pos, IInventory inv, int count)
    {
        int cols = 4; // VS default for GenericTypedContainer
        try
        {
            var block = capi.World.BlockAccessor.GetBlock(pos);
            var qc = block?.Attributes?["quantityColumns"];
            if (qc != null && qc.Exists)
            {
                var dict = qc.AsObject<Dictionary<string, int>>(null!);
                if (dict is { Count: > 0 })
                    cols = dict.Values.Max();
                else
                {
                    int single = qc.AsInt(0);
                    if (single > 0) cols = single;
                }
            }
        }
        catch { /* fall back to default */ }

        // An inventory that exposes its own column count wins outright.
        if (inv.GetType().GetProperty("Cols")?.GetValue(inv) is int cv && cv > 0)
            cols = cv;

        return Math.Clamp(cols, 1, count);
    }

    /// <summary>
    /// Localized container title (e.g. "Chest Contents" / "Contenu de la malle"),
    /// matching the real GUI. Uses the block's dialogTitleLangCode, then the placed
    /// block name, then a generic fallback.
    /// </summary>
    private string ResolveTitle(BlockPos pos)
    {
        try
        {
            var block = capi.World.BlockAccessor.GetBlock(pos);
            var dt = block?.Attributes?["dialogTitleLangCode"];
            if (dt != null && dt.Exists)
            {
                string? code = null;
                var dict = dt.AsObject<Dictionary<string, string>>(null!);
                if (dict is { Count: > 0 }) code = dict.Values.First();
                else code = dt.AsString(null);
                if (!string.IsNullOrEmpty(code)) return Lang.Get(code);
            }

            var name = block?.GetPlacedBlockName(capi.World, pos);
            if (!string.IsNullOrEmpty(name)) return name;
        }
        catch { /* fall back */ }

        return Lang.Get("Contents");
    }

    private void OnSendPacket(object packet) { }
}
