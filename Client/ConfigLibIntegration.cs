using System.Linq;
using ConfigLib;
using ImGuiNET;
using autoSortVintageStoryMod.Network;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace autoSortVintageStoryMod.Client;

/// <summary>
/// AutoSort settings screen rendered through the ConfigLib mod. A switch for the
/// per-player /sort overlay is shown to everyone; the sorting configuration is editable
/// by admins (controlserver privilege) and read-only for everyone else.
/// </summary>
public static class ConfigLibIntegration
{
    // Local editable state (mirrors the last server snapshot).
    private static bool _overlay, _enabled, _compact, _separateFloors, _restrictRoom, _sortBackpack;
    private static int _radius, _maxChests, _maxVSpan, _selectedDiscovered;
    private static float _threshold;
    private static System.Collections.Generic.List<string> _enabledKinds = new();
    private static string _customKind = "";
    private static string _lastHash = "";

    public static void InvalidateCache() => _lastHash = "";

    public static void Register(ICoreClientAPI api, AutoSortClientSystem client)
    {
        var modSys = api.ModLoader.GetModSystem<ConfigLibModSystem>();
        if (modSys == null) return;

        modSys.RegisterCustomConfig("autosort", (id, buttons) =>
        {
            try
            {
                var cfg = client.ServerConfig;
                if (cfg == null) { ImGui.Text(Lang.Get("autosort:cfg-waiting")); return; }
                Sync(cfg);
                Draw(id, buttons.Save, client, cfg);
            }
            catch (System.Exception ex)
            {
                api.Logger.Error($"[AutoSort] ConfigLib draw error: {ex.Message}");
            }
        });
    }

    private static void Sync(ConfigSyncPacket cfg)
    {
        string hash = $"{cfg.IsAdmin}|{cfg.OverlayEnabled}|{cfg.Enabled}|{cfg.CompactRoom}|{cfg.SeparateFloors}|" +
                      $"{cfg.RestrictToSameRoom}|{cfg.SortPlayerBackpack}|{cfg.SearchRadiusBlocks}|{cfg.MaxNetworkChests}|" +
                      $"{cfg.MaxVerticalSpan}|{cfg.SpecialisationThreshold}|{string.Join(',', cfg.EnabledKinds)}";
        if (hash == _lastHash) return;
        _lastHash = hash;

        _overlay = cfg.OverlayEnabled;
        _enabled = cfg.Enabled;
        _compact = cfg.CompactRoom;
        _separateFloors = cfg.SeparateFloors;
        _restrictRoom = cfg.RestrictToSameRoom;
        _sortBackpack = cfg.SortPlayerBackpack;
        _radius = cfg.SearchRadiusBlocks;
        _maxChests = cfg.MaxNetworkChests;
        _maxVSpan = cfg.MaxVerticalSpan;
        _threshold = (float)cfg.SpecialisationThreshold;
        _enabledKinds = cfg.EnabledKinds.ToList();
    }

    private static void Draw(string id, bool save, AutoSortClientSystem client, ConfigSyncPacket cfg)
    {
        // ── Player section: overlay switch (everyone) ──────────────────────────
        ImGui.TextDisabled(Lang.Get("autosort:cfg-player-section"));
        if (ImGui.Checkbox(Lang.Get("autosort:cfg-overlay") + $"##{id}ov", ref _overlay))
            client.SendChange(new ConfigChangePacket { OverlayEnabled = _overlay, ApplyConfig = false });
        ImGui.Separator();

        // ── Admin section ──────────────────────────────────────────────────────
        ImGui.TextDisabled(Lang.Get("autosort:cfg-admin-section"));
        if (!cfg.IsAdmin)
        {
            ImGui.TextDisabled(Lang.Get("autosort:cfg-admin-readonly"));
            ImGui.BeginDisabled();
        }

        ImGui.Checkbox(Lang.Get("autosort:cfg-enabled") + $"##{id}en", ref _enabled);
        ImGui.Checkbox(Lang.Get("autosort:cfg-compact") + $"##{id}co", ref _compact);
        ImGui.Checkbox(Lang.Get("autosort:cfg-separatefloors") + $"##{id}sf", ref _separateFloors);
        ImGui.Checkbox(Lang.Get("autosort:cfg-room") + $"##{id}rm", ref _restrictRoom);
        ImGui.Checkbox(Lang.Get("autosort:cfg-backpack") + $"##{id}bp", ref _sortBackpack);
        ImGui.Spacing();

        ImGui.SetNextItemWidth(120f);
        ImGui.InputInt(Lang.Get("autosort:cfg-radius") + $"##{id}ra", ref _radius);
        ImGui.SetNextItemWidth(120f);
        ImGui.InputInt(Lang.Get("autosort:cfg-maxchests") + $"##{id}mc", ref _maxChests);
        ImGui.SetNextItemWidth(120f);
        ImGui.InputInt(Lang.Get("autosort:cfg-vspan") + $"##{id}vs", ref _maxVSpan);
        ImGui.SetNextItemWidth(220f);
        ImGui.SliderFloat(Lang.Get("autosort:cfg-threshold") + $"##{id}th", ref _threshold, 0.1f, 1.0f);
        ImGui.Spacing();

        // ── Editable container list ────────────────────────────────────────────
        ImGui.Text(Lang.Get("autosort:cfg-containers"));
        for (int i = 0; i < _enabledKinds.Count; i++)
        {
            ImGui.BulletText(_enabledKinds[i]);
            ImGui.SameLine();
            if (ImGui.SmallButton($"x##{id}rm{i}")) { _enabledKinds.RemoveAt(i); i--; }
        }

        // Add from the kinds discovered on the server.
        var notYet = cfg.DiscoveredKinds.Where(k => !_enabledKinds.Contains(k)).ToArray();
        if (notYet.Length > 0)
        {
            if (_selectedDiscovered >= notYet.Length) _selectedDiscovered = 0;
            ImGui.SetNextItemWidth(220f);
            ImGui.Combo($"##{id}disc", ref _selectedDiscovered, notYet, notYet.Length);
            ImGui.SameLine();
            if (ImGui.Button(Lang.Get("autosort:cfg-add") + $"##{id}adddisc"))
                _enabledKinds.Add(notYet[_selectedDiscovered]);
        }

        // Add a custom kind by hand.
        ImGui.SetNextItemWidth(220f);
        ImGui.InputText($"##{id}custom", ref _customKind, 64);
        ImGui.SameLine();
        if (ImGui.Button(Lang.Get("autosort:cfg-add") + $"##{id}addcustom") &&
            !string.IsNullOrWhiteSpace(_customKind) && !_enabledKinds.Contains(_customKind.Trim()))
        {
            _enabledKinds.Add(_customKind.Trim());
            _customKind = "";
        }

        if (!cfg.IsAdmin) { ImGui.EndDisabled(); return; }

        // Admin save.
        if (save)
        {
            client.SendChange(new ConfigChangePacket
            {
                ApplyConfig = true,
                OverlayEnabled = _overlay,
                Enabled = _enabled,
                CompactRoom = _compact,
                SeparateFloors = _separateFloors,
                RestrictToSameRoom = _restrictRoom,
                SortPlayerBackpack = _sortBackpack,
                SearchRadiusBlocks = _radius,
                MaxNetworkChests = _maxChests,
                MaxVerticalSpan = _maxVSpan,
                SpecialisationThreshold = _threshold,
                EnabledKinds = _enabledKinds.ToArray(),
            });
            _lastHash = ""; // force re-sync on next server snapshot
        }
    }
}
