using Vintagestory.API.Server;

namespace autoSortVintageStoryMod.Config;

public class ConfigManager
{
    private const string FileName = "autosort.json";
    private readonly ICoreServerAPI _api;
    private SortConfig _data = new();

    public ConfigManager(ICoreServerAPI api) { _api = api; }

    public SortConfig Data => _data;

    public void Load()
    {
        try
        {
            _data = _api.LoadModConfig<SortConfig>(FileName) ?? new SortConfig();
        }
        catch (Exception ex)
        {
            _api.Logger.Warning("[AutoSort] Failed to load config, using defaults: " + ex.Message);
            _data = new SortConfig();
        }
    }

    public void Save()
    {
        try
        {
            _api.StoreModConfig(_data, FileName);
        }
        catch (Exception ex)
        {
            _api.Logger.Warning("[AutoSort] Failed to save config: " + ex.Message);
        }
    }
}
