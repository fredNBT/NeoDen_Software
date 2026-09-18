using System.IO;
using System.Text.Json;
using NeoDenSoftware.Models;

namespace NeoDenSoftware.Feeders;

/// <summary>
/// Persists user edits to tape/tray feeder positions (Settings tab) to disk under
/// %LocalAppData%\NeoDenSoftware, so they survive app restarts - previously these lived only in
/// memory for the life of the process (documented as a known gap in <c>TapeFeederLibrary</c> /
/// <c>TrayFeederLibrary</c>), so every edit was silently discarded on the next launch. Mirrors
/// <see cref="Footprints.CustomFootprintStore"/>'s plain-JSON, read-modify-write-whole-file
/// approach and its test-isolation seam - same env var, since both stores must never let a test
/// process clobber the user's real file while the live app is also writing to it.
/// </summary>
public static class FeederPositionStore
{
    private static readonly string RootFolder = ResolveRootFolder();
    private static readonly string FilePath = Path.Combine(RootFolder, "feeder_positions.json");

    private static string ResolveRootFolder()
    {
        var overridePath = Environment.GetEnvironmentVariable("NEODEN_FOOTPRINT_STORE_ROOT");
        return !string.IsNullOrEmpty(overridePath)
            ? overridePath
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NeoDenSoftware");
    }

    // Tray entries are keyed by FeederId.ToString() rather than a raw int, since JSON object keys
    // must be strings - System.Text.Json won't serialize a Dictionary<int,_> as an object without
    // extra configuration.
    private sealed record StoreData(
        Dictionary<string, TapeFeederXY>? Tape,
        Dictionary<string, TrayFeederPosition>? Tray,
        HashSet<string>? DeletedTapeDefaults);

    private static StoreData LoadRaw()
    {
        if (!File.Exists(FilePath)) return new StoreData(null, null, null);
        try
        {
            return JsonSerializer.Deserialize<StoreData>(File.ReadAllText(FilePath)) ?? new StoreData(null, null, null);
        }
        catch (Exception)
        {
            // A corrupt/unreadable file shouldn't prevent the app from starting - it just means
            // every feeder falls back to its hardcoded default position this session.
            return new StoreData(null, null, null);
        }
    }

    /// <summary>Overrides keyed by tape feeder Number ("1", "21", ...) - a feeder with no saved
    /// override here should keep using <see cref="TapeFeederLibrary"/>'s default.</summary>
    public static IReadOnlyDictionary<string, TapeFeederXY> LoadTapeOverrides() =>
        LoadRaw().Tape ?? new Dictionary<string, TapeFeederXY>();

    /// <summary>Overrides keyed by tray FeederId - a tray with no saved override here should keep
    /// using <see cref="TrayFeederLibrary"/>'s default.</summary>
    public static IReadOnlyDictionary<int, TrayFeederPosition> LoadTrayOverrides() =>
        (LoadRaw().Tray ?? new Dictionary<string, TrayFeederPosition>())
        .Values.ToDictionary(p => p.FeederId);

    /// <summary>Called on every edit to a tape feeder's X/Y/Angle (Settings tab) - writes the
    /// whole file back out immediately rather than batching, since feeder counts here (tens, not
    /// thousands) make that cost negligible and it guarantees no edit is ever lost to a crash
    /// between edits.</summary>
    public static void SaveTapeFeeder(string number, TapeFeederXY position)
    {
        var data = LoadRaw();
        var tape = data.Tape is null ? new Dictionary<string, TapeFeederXY>() : new Dictionary<string, TapeFeederXY>(data.Tape);
        tape[number] = position;
        WriteAll(data with { Tape = tape });
    }

    /// <summary>Called on every edit to a tray feeder's Begin/End X/Y or Rows/Columns.</summary>
    public static void SaveTrayFeeder(TrayFeederPosition position)
    {
        var data = LoadRaw();
        var tray = data.Tray is null ? new Dictionary<string, TrayFeederPosition>() : new Dictionary<string, TrayFeederPosition>(data.Tray);
        tray[position.FeederId.ToString()] = position;
        WriteAll(data with { Tray = tray });
    }

    /// <summary>Called when a user-added tape feeder (one with no corresponding entry in
    /// <see cref="TapeFeederLibrary.Defaults"/>) is removed from the Settings tab - without this,
    /// the deleted number's last-saved position would linger in the file and silently get reused
    /// if a later "+ Add" ever lands on that same auto-assigned number again. Harmless no-op for a
    /// number that was never saved (e.g. removing a just-added, never-edited feeder).</summary>
    public static void DeleteTapeFeeder(string number)
    {
        var data = LoadRaw();
        if (data.Tape is null || !data.Tape.ContainsKey(number)) return;
        var tape = new Dictionary<string, TapeFeederXY>(data.Tape);
        tape.Remove(number);
        WriteAll(data with { Tape = tape });
    }

    /// <summary>Same as <see cref="DeleteTapeFeeder"/> but for a user-added tray feeder.</summary>
    public static void DeleteTrayFeeder(int feederId)
    {
        var data = LoadRaw();
        var key = feederId.ToString();
        if (data.Tray is null || !data.Tray.ContainsKey(key)) return;
        var tray = new Dictionary<string, TrayFeederPosition>(data.Tray);
        tray.Remove(key);
        WriteAll(data with { Tray = tray });
    }

    /// <summary>Tape feeder numbers ("1", "21", ...) the user has deleted even though they're one
    /// of <see cref="TapeFeederLibrary.Defaults"/> - <c>MainViewModel.BuildTapeFeederSettings</c>
    /// checks this and skips recreating any number listed here, since otherwise the hardcoded
    /// default list would silently resurrect a deleted feeder on every restart.</summary>
    public static IReadOnlySet<string> LoadDeletedTapeDefaults() =>
        LoadRaw().DeletedTapeDefaults ?? new HashSet<string>();

    /// <summary>Called when the user deletes one of the hardcoded default tape feeders (as
    /// opposed to one they added themselves, which just uses <see cref="DeleteTapeFeeder"/>).</summary>
    public static void MarkTapeDefaultDeleted(string number)
    {
        var data = LoadRaw();
        var deleted = data.DeletedTapeDefaults is null ? new HashSet<string>() : new HashSet<string>(data.DeletedTapeDefaults);
        deleted.Add(number);
        WriteAll(data with { DeletedTapeDefaults = deleted });
    }

    private static void WriteAll(StoreData data)
    {
        Directory.CreateDirectory(RootFolder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(data));
    }
}
