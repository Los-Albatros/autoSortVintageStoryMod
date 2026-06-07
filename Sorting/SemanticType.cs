namespace autoSortVintageStoryMod.Sorting;

/// <summary>
/// Semantic category for chest sorting. Enum ordinal defines display/sort order,
/// so related categories are kept adjacent (e.g. Food next to Drink/Ingredient).
/// </summary>
public enum SemanticType
{
    Weapon     = 0,
    Tool       = 1,
    Armor      = 2,
    Clothing   = 3,
    Food       = 4,
    Drink      = 5,
    Ingredient = 6,
    Seed       = 7,
    Material   = 8,
    Pottery    = 9,
    Building   = 10,
    Fuel       = 11,
    Lighting   = 12,
    Mechanical = 13,
    Medicine   = 14,
    Valuable   = 15,
    Other      = 16
}
