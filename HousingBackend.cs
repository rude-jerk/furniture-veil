using System.Numerics;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FurnitureVeil.Core;
using FurnitureSheet = Lumina.Excel.Sheets.HousingFurniture;
using YardSheet = Lumina.Excel.Sheets.HousingYardObject;

namespace FurnitureVeil;

internal sealed unsafe class HousingBackend(IClientState client, IObjectTable objects, IDataManager data) : IVisibilityWriter
{
    private readonly Dictionary<(bool Outdoor, uint Id), string> names = new();

    // All calls run on Framework.Update, including the final unload restoration.
    private HousingFurnitureManager* CurrentManager(out SceneKey scene, out bool outdoor)
    {
        scene = default;
        outdoor = false;
        if (!client.IsLoggedIn) return null;
        var housing = HousingManager.Instance();
        if (housing == null || housing->CurrentTerritory == null || !housing->CurrentTerritory->IsLoaded())
            return null;
        HousingFurnitureManager* manager;
        if (housing->IndoorTerritory != null && housing->CurrentTerritory == (HousingTerritory*)housing->IndoorTerritory)
            manager = &housing->IndoorTerritory->FurnitureManager;
        else if (housing->OutdoorTerritory != null && housing->CurrentTerritory == (HousingTerritory*)housing->OutdoorTerritory)
        {
            manager = &housing->OutdoorTerritory->FurnitureManager;
            outdoor = true;
        }
        else return null;
        scene = new(client.TerritoryType, (nint)housing->CurrentTerritory, (nint)manager);
        return manager;
    }

    public HousingFrame? Capture()
    {
        var manager = CurrentManager(out var scene, out var outdoor);
        if (manager == null) return null;
        var entries = new List<FurnitureEntry>();
        var playerPosition = objects.LocalPlayer?.Position ?? Vector3.Zero;
        // Last FurnitureMemory entry is the placement preview, not a placed item.
        for (var slot = 0; slot < manager->FurnitureMemory.Length - 1; slot++)
        {
            var obj = ResolveSlot(manager, slot, out var key);
            if (obj == null) continue;
            var position = new Vector3(obj->Position.X, obj->Position.Y, obj->Position.Z);
            entries.Add(new(key, GetName(outdoor, key.FurnitureId), position, obj->Rotation,
                obj->Visibility, Vector3.Distance(playerPosition, position)));
        }
        return new(scene, entries.OrderBy(e => e.Distance).ToArray());
    }

    private static GameObject* ResolveSlot(HousingFurnitureManager* manager, int slot, out FurnitureKey key)
    {
        key = default;
        if (slot < 0 || slot >= manager->FurnitureMemory.Length - 1) return null;
        ref var furniture = ref manager->FurnitureMemory[slot];
        ref var array = ref manager->ObjectManager.ObjectArray;
        if (furniture.Id == 0 || furniture.Index < 0 || furniture.Index >= array.ObjectCount ||
            furniture.Index >= array.Objects.Length) return null;
        var obj = array.Objects[furniture.Index].Value;
        if (obj == null || obj == array.TempObject) return null;
        key = new(slot, furniture.Index, (nint)obj, furniture.Id, obj->BaseId, obj->LayoutId, obj->EntityId);
        return obj;
    }

    public bool TrySetVisibility(SceneKey scene, FurnitureKey key, byte visibility)
    {
        var manager = CurrentManager(out var currentScene, out _);
        if (manager == null || currentScene != scene) return false;
        var obj = ResolveSlot(manager, key.Slot, out var currentKey);
        if (obj == null || currentKey != key) return false;
        // Documented local GameObject visibility: 0 = visible, 1 = hidden.
        // No placement, inventory, housing permission, or network operations are used.
        obj->SetVisibility(visibility);
        return true;
    }

    private string GetName(bool outdoor, uint id)
    {
        if (names.TryGetValue((outdoor, id), out var cached)) return cached;
        var rowId = id | (outdoor ? 0x30000u : 0x20000u);
        var sheet = data.GetExcelSheet<FurnitureSheet>();
        string? name = null;
        if (sheet.TryGetRow(rowId, out var row)) name = row.Item.Value.Name.ToString();
        // Older sheets use separate indoor/outdoor row numbering.
        if (string.IsNullOrWhiteSpace(name) && outdoor && data.GetExcelSheet<YardSheet>().TryGetRow(id, out var yard))
            name = yard.Item.Value.Name.ToString();
        if (string.IsNullOrWhiteSpace(name) && !outdoor && sheet.TryGetRow(id, out row))
            name = row.Item.Value.Name.ToString();
        return names[(outdoor, id)] = string.IsNullOrWhiteSpace(name) ? $"Furnishing {id}" : name;
    }
}
