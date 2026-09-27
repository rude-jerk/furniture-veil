using System.Numerics;
using FurnitureVeil.Core;

var tests = new (string Name, Action Run)[]
{
    ("Hiding one instance leaves identical furniture alone", () =>
    {
        var (s, b, a) = Start();
        var second = a with { Slot = 1, Address = 200 };
        b.Entries.Add(Entry(second));
        Check(s.Hide(s.Generation, b.Frame(), a, b));
        Check(b.Entries.Single(e => e.Key == a).Visibility == 1);
        Check(b.Entries.Single(e => e.Key == second).Visibility == 0);
        Check(s.HiddenCount == 1);
    }),
    ("Leaving GPose restores and clears the session", () =>
    {
        var (s, b, a) = Start();
        s.Hide(s.Generation, b.Frame(), a, b);
        s.Update(false, b.Frame(), b);
        Check(b.Entries[0].Visibility == 0 && s.HiddenCount == 0);
        Check(!s.Hide(s.Generation, b.Frame(), a, b));
    }),
    ("Manual restore supports repeated hide/restore cycles", () =>
    {
        var (s, b, a) = Start();
        for (var i = 0; i < 3; i++)
        {
            Check(s.Hide(s.Generation, b.Frame(), a, b));
            s.Restore(b.Frame(), a, b);
            Check(b.Entries[0].Visibility == 0 && s.HiddenCount == 0);
        }
    }),
    ("Restore all affects every owned instance", () =>
    {
        var (s, b, a) = Start();
        var second = a with { Slot = 1, Address = 200 };
        b.Entries.Add(Entry(second));
        s.Hide(s.Generation, b.Frame(), a, b);
        s.Hide(s.Generation, b.Frame(), second, b);
        s.RestoreAll(b.Frame(), b);
        Check(b.Entries.All(e => e.Visibility == 0) && s.HiddenCount == 0);
    }),
    ("Changing rooms never writes to objects in the old room", () =>
    {
        var (s, b, a) = Start();
        s.Hide(s.Generation, b.Frame(), a, b);
        b.Writes.Clear();
        b.Scene = b.Scene with { Territory = 99 };
        s.Update(true, b.Frame(), b);
        Check(b.Writes.Count == 0 && s.HiddenCount == 0);
    }),
    ("Reused object addresses with different furniture are rejected", () =>
    {
        var (s, b, a) = Start();
        s.Hide(s.Generation, b.Frame(), a, b);
        b.Writes.Clear();
        b.Entries[0] = Entry(a with { FurnitureId = 77 });
        s.Update(true, b.Frame(), b);
        s.RestoreAll(b.Frame(), b);
        Check(b.Writes.Count == 0 && s.HiddenCount == 0);
    }),
    ("Disappearing objects are forgotten without dereferencing them", () =>
    {
        var (s, b, a) = Start();
        s.Hide(s.Generation, b.Frame(), a, b);
        b.Writes.Clear();
        b.Entries.Clear();
        s.Update(true, b.Frame(), b);
        Check(b.Writes.Count == 0 && s.HiddenCount == 0);
    }),
    ("Unloaded housing clears state without writes", () =>
    {
        var (s, b, a) = Start();
        s.Hide(s.Generation, b.Frame(), a, b);
        b.Writes.Clear();
        s.Update(false, null, b);
        Check(b.Writes.Count == 0 && s.HiddenCount == 0);
    }),
    ("Delayed clicks from a previous GPose session are rejected", () =>
    {
        var (s, b, a) = Start();
        var oldGeneration = s.Generation;
        s.Update(false, b.Frame(), b);
        s.Update(true, b.Frame(), b);
        Check(!s.Hide(oldGeneration, b.Frame(), a, b));
        Check(b.Writes.Count == 0);
    }),
    ("Already hidden objects are not claimed or revealed", () =>
    {
        var (s, b, a) = Start();
        b.Entries[0] = Entry(a) with { Visibility = 1 };
        Check(!s.Hide(s.Generation, b.Frame(), a, b));
        s.RestoreAll(b.Frame(), b);
        Check(b.Writes.Count == 0 && b.Entries[0].Visibility == 1);
    }),
    ("External visibility changes are preserved on restore", () =>
    {
        var (s, b, a) = Start();
        s.Hide(s.Generation, b.Frame(), a, b);
        b.Entries[0] = Entry(a) with { Visibility = 2 };
        b.Writes.Clear();
        s.Restore(b.Frame(), a, b);
        Check(b.Writes.Count == 0 && b.Entries[0].Visibility == 2);
    }),
    ("A game visibility reset is reapplied without replacing the original", () =>
    {
        var (s, b, a) = Start();
        s.Hide(s.Generation, b.Frame(), a, b);
        b.Entries[0] = Entry(a);
        s.Update(true, b.Frame(), b);
        Check(b.Entries[0].Visibility == 1);
        Check(!s.Hide(s.Generation, b.Frame(), a, b));
        s.Restore(b.Frame(), a, b);
        Check(b.Entries[0].Visibility == 0);
    }),
    ("Rejected native writes do not leave phantom hidden entries", () =>
    {
        var (s, b, a) = Start();
        b.RejectWrites = true;
        Check(!s.Hide(s.Generation, b.Frame(), a, b));
        Check(s.HiddenCount == 0);
    }),
    ("Failed writes retain restoration information on exception", () =>
    {
        var (s, b, a) = Start();
        b.ThrowAfterWrite = true;
        try { s.Hide(s.Generation, b.Frame(), a, b); }
        catch (InvalidOperationException) { }
        Check(s.HiddenCount == 1 && b.Entries[0].Visibility == 1);
        b.ThrowAfterWrite = false;
        s.RestoreAll(b.Frame(), b);
        Check(s.HiddenCount == 0 && b.Entries[0].Visibility == 0);
    }),
};

var failures = 0;
foreach (var (name, test) in tests)
{
    try { test(); Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failures++; Console.Error.WriteLine($"FAIL {name}: {ex.Message}"); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed.");
return failures == 0 ? 0 : 1;

static void Check(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
static FurnitureEntry Entry(FurnitureKey key) => new(key, "Chair", Vector3.Zero, 0, 0, 2);
static (VisibilitySession, FakeBackend, FurnitureKey) Start()
{
    var key = new FurnitureKey(0, 0, 100, 1, 1, 1, 1);
    var backend = new FakeBackend();
    backend.Entries.Add(Entry(key));
    var session = new VisibilitySession();
    session.Update(true, backend.Frame(), backend);
    return (session, backend, key);
}

sealed class FakeBackend : IVisibilityWriter
{
    public SceneKey Scene = new(1, 2, 3);
    public List<FurnitureEntry> Entries = [];
    public List<(FurnitureKey Key, byte Visibility)> Writes = [];
    public bool RejectWrites;
    public bool ThrowAfterWrite;
    public HousingFrame Frame() => new(Scene, Entries.ToArray());
    public bool TrySetVisibility(SceneKey scene, FurnitureKey key, byte visibility)
    {
        var index = Entries.FindIndex(e => e.Key == key);
        if (RejectWrites || scene != Scene || index < 0) return false;
        Writes.Add((key, visibility));
        Entries[index] = Entries[index] with { Visibility = visibility };
        if (ThrowAfterWrite) throw new InvalidOperationException("Simulated exception after a write");
        return true;
    }
}
