using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace HostsBlocker;

/// <summary>
/// One blocked site. Serialises to a single hosts line holding the exact
/// hostnames we block - no wildcards, so sibling subdomains stay reachable.
/// </summary>
public sealed class BlockEntry : INotifyPropertyChanged
{
    private bool _includeWww = true;
    private bool _enabled = true;

    public required string Domain { get; init; }

    public bool IncludeWww
    {
        get => _includeWww;
        set { if (Set(ref _includeWww, value)) Raise(nameof(HostList)); }
    }

    public bool Enabled
    {
        get => _enabled;
        set => Set(ref _enabled, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool Set(ref bool field, bool value, [CallerMemberName] string name = "")
    {
        if (field == value) return false;
        field = value;
        Raise(name);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Raised on any user edit so the window can enable "Apply".</summary>
    public event EventHandler? Changed;

    public IEnumerable<string> Hostnames
    {
        get
        {
            yield return Domain;
            if (IncludeWww && !Domain.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
                yield return "www." + Domain;
        }
    }

    public string HostList => string.Join(" ", Hostnames);

    public string ToLine()
    {
        var line = $"{HostsFile.BlackholeIp} {HostList}";
        return Enabled ? line : "#" + line;
    }
}

/// <summary>
/// Reads and writes the system hosts file, touching only our own delimited
/// region. Everything outside it - notably the block Docker Desktop rewrites
/// on its own - is carried through byte for byte.
/// </summary>
public static class HostsFile
{
    public const string BlackholeIp = "0.0.0.0";

    private const string BeginMarker = "# >>> HostsBlocker managed block - do not edit inside <<<";
    private const string EndMarker = "# <<< HostsBlocker managed block end >>>";

    /// <summary>Settable so tests can point at a scratch file.</summary>
    public static string Path { get; set; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System),
        "drivers", "etc", "hosts");

    /// <summary>Lines before our block, and after it, kept verbatim.</summary>
    private static (List<string> Before, List<string> After) _foreign = (new(), new());

    private static bool _hadBom;
    private static string _newline = "\r\n";

    public static List<BlockEntry> Load()
    {
        var entries = new List<BlockEntry>();

        if (!File.Exists(Path))
        {
            _foreign = (new List<string>(), new List<string>());
            return entries;
        }

        var raw = File.ReadAllBytes(Path);
        _hadBom = raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF;
        var text = new UTF8Encoding(false).GetString(raw, _hadBom ? 3 : 0, raw.Length - (_hadBom ? 3 : 0));
        _newline = text.Contains("\r\n") || !text.Contains('\n') ? "\r\n" : "\n";

        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        // A trailing newline produces a final empty element; drop it so we
        // don't grow the file by one blank line on every save.
        if (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);

        var begin = lines.FindIndex(l => l.Trim() == BeginMarker);
        var end = begin < 0 ? -1 : lines.FindIndex(begin + 1, l => l.Trim() == EndMarker);

        if (begin < 0 || end < 0)
        {
            _foreign = (lines, new List<string>());
            return entries;
        }

        _foreign = (lines.Take(begin).ToList(), lines.Skip(end + 1).ToList());

        for (var i = begin + 1; i < end; i++)
        {
            var entry = ParseLine(lines[i]);
            if (entry is not null) entries.Add(entry);
        }

        return entries;
    }

    private static BlockEntry? ParseLine(string line)
    {
        var s = line.Trim();
        if (s.Length == 0) return null;

        var enabled = true;
        if (s.StartsWith('#'))
        {
            enabled = false;
            s = s.TrimStart('#').Trim();
        }

        var parts = s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;

        var domain = parts[1];
        var www = "www." + domain;
        return new BlockEntry
        {
            Domain = domain,
            IncludeWww = parts.Skip(2).Any(h => h.Equals(www, StringComparison.OrdinalIgnoreCase)),
            Enabled = enabled,
        };
    }

    /// <summary>Renders the whole file; used for both saving and the preview.</summary>
    public static string Render(IEnumerable<BlockEntry> entries)
    {
        var sb = new StringBuilder();
        foreach (var l in _foreign.Before) sb.Append(l).Append(_newline);

        sb.Append(BeginMarker).Append(_newline);
        foreach (var e in entries.OrderBy(e => e.Domain, StringComparer.OrdinalIgnoreCase))
            sb.Append(e.ToLine()).Append(_newline);
        sb.Append(EndMarker).Append(_newline);

        foreach (var l in _foreign.After) sb.Append(l).Append(_newline);
        return sb.ToString();
    }

    public static void Save(IEnumerable<BlockEntry> entries)
    {
        var text = Render(entries);
        // GetBytes never emits the preamble, so re-attach the BOM by hand when
        // the file we read had one - Windows ships hosts with a UTF-8 BOM.
        var body = new UTF8Encoding(false).GetBytes(text);
        var bytes = _hadBom ? [.. new byte[] { 0xEF, 0xBB, 0xBF }, .. body] : body;

        // Write beside the target then swap, so a crash mid-write can't leave
        // the machine with a truncated hosts file.
        var temp = Path + ".hostsblocker.tmp";
        File.WriteAllBytes(temp, bytes);

        if (File.Exists(Path))
        {
            var backup = Path + ".hostsblocker.bak";
            File.Replace(temp, Path, backup, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(temp, Path);
        }
    }

    public static void FlushDns()
    {
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ipconfig",
                Arguments = "/flushdns",
                CreateNoWindow = true,
                UseShellExecute = false,
            });
            p?.WaitForExit(5000);
        }
        catch
        {
            // Cosmetic - the hosts change still takes effect, just maybe not instantly.
        }
    }
}
