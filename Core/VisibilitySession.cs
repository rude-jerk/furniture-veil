using System.Numerics;

namespace FurnitureVeil.Core;

// Addresses are opaque identity tokens here. Only HousingBackend dereferences live objects.
public readonly record struct SceneKey(uint TerritoryId, nint Territory, nint Manager);
public readonly record struct FurnitureKey(int Slot, int ObjectSlot, nint Address, uint FurnitureId,
    uint BaseId, uint LayoutId, uint EntityId);
public sealed record FurnitureEntry(FurnitureKey Key, string Name, Vector3 Position,
    float Rotation, byte Visibility, float Distance);
public sealed record HousingFrame(SceneKey Scene, IReadOnlyList<FurnitureEntry> Entries);

public interface IVisibilityWriter
{
    // Must resolve the key against the current housing manager again before writing.
    bool TrySetVisibility(SceneKey scene, FurnitureKey key, byte visibility);
}

public sealed class VisibilitySession
{
    private readonly Dictionary<FurnitureKey, byte> originals = new();
    private SceneKey scene;
    private bool active;
    public long Generation { get; private set; }
    public int HiddenCount => originals.Count;
    public bool IsHidden(FurnitureKey key) => originals.ContainsKey(key);

    public void Update(bool gpose, HousingFrame? frame, IVisibilityWriter writer)
    {
        if ((frame == null && active) || (frame != null && active && frame.Scene != scene))
            Forget(); // The old scene is gone: never dereference its saved addresses.

        if (!gpose || frame == null)
        {
            if (frame != null) RestoreAll(frame, writer);
            if (active) { active = false; Generation++; }
            return;
        }

        if (!active)
        {
            scene = frame.Scene;
            active = true;
            Generation++;
        }

        var live = frame.Entries.ToDictionary(e => e.Key);
        foreach (var key in originals.Keys.ToArray())
        {
            if (!live.TryGetValue(key, out var entry))
                originals.Remove(key);
            else if (entry.Visibility != 1)
                writer.TrySetVisibility(scene, key, 1);
        }
    }

    public bool Hide(long generation, HousingFrame frame, FurnitureKey key, IVisibilityWriter writer)
    {
        if (!active || generation != Generation || frame.Scene != scene || originals.ContainsKey(key))
            return false;
        var entry = frame.Entries.FirstOrDefault(e => e.Key == key);
        if (entry == null || entry.Visibility != 0) return false;
        // Save first so an unexpected writer exception cannot lose restoration state.
        originals.Add(key, entry.Visibility);
        if (writer.TrySetVisibility(scene, key, 1)) return true;
        originals.Remove(key);
        return false;
    }

    public void Restore(HousingFrame frame, FurnitureKey key, IVisibilityWriter writer)
    {
        if (frame.Scene != scene || !originals.TryGetValue(key, out var original)) return;
        var entry = frame.Entries.FirstOrDefault(e => e.Key == key);
        if (entry != null && entry.Visibility == 1)
            writer.TrySetVisibility(scene, key, original);
        originals.Remove(key);
    }

    public void RestoreAll(HousingFrame frame, IVisibilityWriter writer)
    {
        foreach (var key in originals.Keys.ToArray()) Restore(frame, key, writer);
    }

    public void Forget()
    {
        originals.Clear();
        active = false;
        scene = default;
        Generation++;
    }
}
