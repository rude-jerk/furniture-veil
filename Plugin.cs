using System.Collections.Concurrent;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FurnitureVeil.Core;

namespace FurnitureVeil;

public sealed class Plugin : IDalamudPlugin
{
    private readonly IDalamudPluginInterface pi;
    private readonly IClientState client;
    private readonly IFramework framework;
    private readonly ICommandManager commands;
    private readonly IGameGui gameGui;
    private readonly IPluginLog log;
    private readonly HousingBackend backend;
    private readonly VisibilitySession session = new();
    private readonly ConcurrentQueue<Request> requests = new();
    private volatile View view = new(false, false, 0, [], 0, null);
    private volatile bool disposed;
    private bool windowOpen;
    private bool hiddenOnly;
    private bool marker = true;
    private string search = "";
    private FurnitureKey? selected;
    private long selectedGeneration;
    private bool faulted;
    private long nextScan;
    private sealed record Request(string Action, long Generation, FurnitureKey Key = default);
    private sealed record Row(FurnitureEntry Entry, bool Hidden);
    private sealed record View(bool GPose, bool Housing, long Generation, Row[] Rows, int HiddenCount, string? Error);

    public Plugin(IDalamudPluginInterface pi, IClientState client, IFramework framework,
        ICommandManager commands, IGameGui gameGui, IPluginLog log, IObjectTable objects, IDataManager data)
    {
        this.pi = pi;
        this.client = client;
        this.framework = framework;
        this.commands = commands;
        this.gameGui = gameGui;
        this.log = log;
        backend = new(client, objects, data);
        commands.AddHandler("/fveil", new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Furniture Veil. /fveil restore restores all furnishings hidden by this plugin.",
        });
        // Keep the controls usable while GPose hides the ordinary game UI.
        pi.UiBuilder.DisableGposeUiHide = true;
        pi.UiBuilder.Draw += Draw;
        pi.UiBuilder.OpenMainUi += Open;
        pi.UiBuilder.OpenConfigUi += Open;
        framework.Update += Update;
        client.TerritoryChanged += TerritoryChanged;
    }

    private void Open() => windowOpen = true;

    private void OnCommand(string command, string args)
    {
        if (args.Trim().Equals("restore", StringComparison.OrdinalIgnoreCase))
            requests.Enqueue(new("restore-all", view.Generation));
        else Open();
    }

    private void TerritoryChanged(uint territory)
    {
        // TerritoryChanged reports the new territory. Old pointers must not be followed.
        session.Forget();
        requests.Clear();
        view = new(false, false, session.Generation, [], 0, null);
        nextScan = 0;
        faulted = false;
    }

    private void Update(IFramework _)
    {
        if (disposed) return;
        // Full scans at 10 Hz when idle; every frame while objects are hidden or actions are pending.
        var now = Environment.TickCount64;
        if (now < nextScan && session.HiddenCount == 0 && requests.IsEmpty && client.IsGPosing == view.GPose) return;
        nextScan = now + 100;
        try
        {
            var gpose = client.IsGPosing;
            var frame = gpose || session.HiddenCount > 0 ? backend.Capture() : null;
            session.Update(gpose && !faulted, frame, backend);
            while (requests.TryDequeue(out var request))
            {
                if (request.Action == "retry") { faulted = false; continue; }
                // Refresh after enforcement/each action; restoration must observe current visibility.
                var current = backend.Capture();
                if (current == null) continue;
                if (request.Action == "restore-all") session.RestoreAll(current, backend);
                else if (request.Action == "restore" && request.Generation == session.Generation)
                    session.Restore(current, request.Key, backend);
                else if (request.Action == "hide" && gpose && !faulted)
                    session.Hide(request.Generation, current, request.Key, backend);
            }
            view = new(gpose, frame != null, session.Generation,
                frame?.Entries.Select(e => new Row(e, session.IsHidden(e.Key))).ToArray() ?? [],
                session.HiddenCount, faulted ? "Furniture controls paused after an error. Restore all, then retry." : null);
        }
        catch (Exception ex)
        {
            if (!faulted) log.Error(ex, "Furniture Veil could not update furniture visibility.");
            faulted = true;
            view = view with { Error = "Furniture scan failed. Details are available in /xllog. Try Restore all." };
        }
    }

    private void Draw()
    {
        if (disposed || !windowOpen) return;
        var current = view;
        if (selectedGeneration != current.Generation) selected = null;
        DrawMarker(current);
        ImGui.SetNextWindowSize(new Vector2(690, 520), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Furniture Veil###FurnitureVeil", ref windowOpen)) { ImGui.End(); return; }
        try
        {
            if (ImGui.Button($"Restore all ({current.HiddenCount})"))
                requests.Enqueue(new("restore-all", current.Generation));
            ImGui.SameLine();
            ImGui.Checkbox("Selection marker", ref marker);
            if (current.Error != null)
            {
                ImGui.TextWrapped(current.Error);
                if (ImGui.Button("Retry")) requests.Enqueue(new("retry", current.Generation));
            }
            if (!current.GPose)
            {
                ImGui.TextWrapped("Enter GPose inside a house, apartment, or housing ward to select furnishings.");
                return;
            }
            if (!current.Housing)
            {
                ImGui.TextWrapped("No loaded housing furniture found in this area.");
                return;
            }
            ImGui.TextWrapped("Hide an original furnishing, then spawn and resize its replacement in Brio. Hidden originals restore when GPose ends.");
            ImGui.SetNextItemWidth(340);
            ImGui.InputTextWithHint("##search", "Search furniture...", ref search, 200);
            ImGui.SameLine();
            ImGui.Checkbox("Hidden only", ref hiddenOnly);
            ImGui.TextDisabled($"{current.Rows.Length} furnishings | closest first | select a row to locate it");

            if (ImGui.BeginTable("furniture", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollY,
                new Vector2(0, Math.Max(120, ImGui.GetContentRegionAvail().Y - 92))))
            {
                ImGui.TableSetupColumn("Furnishing", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Instance", ImGuiTableColumnFlags.WidthFixed, 65);
                ImGui.TableSetupColumn("Distance", ImGuiTableColumnFlags.WidthFixed, 65);
                ImGui.TableSetupColumn("Visibility", ImGuiTableColumnFlags.WidthFixed, 95);
                ImGui.TableSetupScrollFreeze(0, 1);
                ImGui.TableHeadersRow();
                foreach (var row in current.Rows)
                {
                    var e = row.Entry;
                    if ((hiddenOnly && !row.Hidden) || !e.Name.Contains(search, StringComparison.OrdinalIgnoreCase)) continue;
                    ImGui.PushID(e.Key.Slot);
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    if (ImGui.Selectable(e.Name, selected == e.Key))
                    {
                        selected = e.Key;
                        selectedGeneration = current.Generation;
                    }
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted($"#{e.Key.Slot + 1}");
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted($"{e.Distance:F1}y");
                    ImGui.TableNextColumn();
                    ImGui.BeginDisabled(current.Error != null && !row.Hidden || !row.Hidden && e.Visibility != 0);
                    if (ImGui.SmallButton(row.Hidden ? "Restore" : "Hide"))
                        requests.Enqueue(new(row.Hidden ? "restore" : "hide", current.Generation, e.Key));
                    ImGui.EndDisabled();
                    ImGui.PopID();
                }
                ImGui.EndTable();
            }
            var selection = current.Rows.FirstOrDefault(r => r.Entry.Key == selected);
            if (selection != null)
            {
                var e = selection.Entry;
                ImGui.TextUnformatted($"{e.Name} | instance #{e.Key.Slot + 1}");
                ImGui.TextDisabled($"X {e.Position.X:F3}   Y {e.Position.Y:F3}   Z {e.Position.Z:F3}   Rotation {e.Rotation * 180 / MathF.PI:F1}°");
                if (ImGui.SmallButton("Copy position"))
                    ImGui.SetClipboardText(FormattableString.Invariant($"{e.Position.X:F3}, {e.Position.Y:F3}, {e.Position.Z:F3}"));
                ImGui.SameLine();
                if (ImGui.SmallButton("Copy item name")) ImGui.SetClipboardText(e.Name);
            }
            else ImGui.TextDisabled("Select a furnishing to see its position and mark its location.");
        }
        finally { ImGui.End(); }
    }

    private void DrawMarker(View current)
    {
        if (!marker || !current.GPose || selected == null) return;
        var row = current.Rows.FirstOrDefault(r => r.Entry.Key == selected);
        if (row == null || !gameGui.WorldToScreen(row.Entry.Position, out var screen)) return;
        var draw = ImGui.GetForegroundDrawList();
        var color = ImGui.GetColorU32(new Vector4(1, .8f, .15f, 1));
        draw.AddCircle(screen, 12, color, 24, 3);
        draw.AddLine(screen - new Vector2(18, 0), screen + new Vector2(18, 0), color, 2);
        draw.AddLine(screen - new Vector2(0, 18), screen + new Vector2(0, 18), color, 2);
        draw.AddText(screen + new Vector2(18, -18), color, $"{row.Entry.Name} #{row.Entry.Key.Slot + 1}" + (row.Hidden ? " (hidden)" : ""));
    }

    public void Dispose()
    {
        disposed = true;
        commands.RemoveHandler("/fveil");
        pi.UiBuilder.Draw -= Draw;
        pi.UiBuilder.OpenMainUi -= Open;
        pi.UiBuilder.OpenConfigUi -= Open;
        client.TerritoryChanged -= TerritoryChanged;
        framework.Update -= Update;
        requests.Clear();
        if (!framework.IsFrameworkUnloading)
        {
            // Synchronous disposal is declared in the manifest. RunOnFrameworkThread executes
            // immediately when already on that thread; otherwise wait for the queued restoration.
            framework.RunOnFrameworkThread(() =>
            {
                try
                {
                    var frame = backend.Capture();
                    if (frame != null) session.RestoreAll(frame, backend);
                }
                catch (Exception ex) { log.Error(ex, "Furniture Veil could not restore during unload."); }
                finally { session.Forget(); }
            }).GetAwaiter().GetResult();
        }
    }
}
