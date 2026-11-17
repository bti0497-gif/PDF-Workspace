using System.IO;
using System.Text.Json;

namespace Workspace.Shell.Startup;

public sealed record RecentFileEntry(string Path, DateTimeOffset LastOpenedUtc);
public sealed record RecentFilesSnapshot(IReadOnlyList<RecentFileEntry> Entries, string? Warning = null);
public interface IRecentFilesStore
{
    RecentFilesSnapshot Read();
    void RecordOpened(string path);
    void Remove(string path);
}

/// <summary>Product-local history only. Never deletes or writes document files.</summary>
public sealed class JsonRecentFilesStore : IRecentFilesStore
{
    public const int Capacity = 20;
    public string StoragePath { get; }
    public static string DefaultStoragePath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Deojon", "PdfWorkspace.Wpf", "Startup", "recent-files.json");

    // An alternate path is intentionally injectable for isolated checks and product variants.
    public JsonRecentFilesStore(string? storagePath = null) => StoragePath = System.IO.Path.GetFullPath(storagePath ?? DefaultStoragePath);
    public RecentFilesSnapshot Read()
    {
        try { return new(ReadEntries()); }
        catch (Exception ex) when (IsStorageFailure(ex))
        {
            return new(Array.Empty<RecentFileEntry>(), "최근 파일 기록을 읽지 못했습니다. 기존 기록은 보존합니다. 파일 불러오기는 계속 사용할 수 있습니다.");
        }
    }
    public void RecordOpened(string path)
    {
        var full = Normalize(path);
        if (!string.Equals(System.IO.Path.GetExtension(full), ".pdf", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("PDF 파일만 기록할 수 있습니다.", nameof(path));
        if (!File.Exists(full)) throw new FileNotFoundException("존재하는 파일만 최근 기록에 추가할 수 있습니다.", full);
        Change(entries =>
        {
            entries.RemoveAll(e => string.Equals(e.Path, full, StringComparison.OrdinalIgnoreCase));
            entries.Insert(0, new(full, DateTimeOffset.UtcNow));
            if (entries.Count > Capacity) entries.RemoveRange(Capacity, entries.Count - Capacity);
        });
    }
    public void Remove(string path)
    {
        var full = Normalize(path);
        Change(entries => entries.RemoveAll(e => string.Equals(e.Path, full, StringComparison.OrdinalIgnoreCase)));
    }
    private static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !System.IO.Path.IsPathFullyQualified(path))
            throw new ArgumentException("절대 파일 경로가 필요합니다.", nameof(path));
        return System.IO.Path.GetFullPath(path);
    }
    private List<RecentFileEntry> ReadEntries()
    {
        if (!File.Exists(StoragePath)) return new();
        var info = new FileInfo(StoragePath);
        if (info.Length > 1024 * 1024) throw new InvalidDataException("최근 기록 크기가 허용 범위를 넘었습니다.");
        using var input = new FileStream(StoragePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        var document = JsonSerializer.Deserialize<HistoryDocument>(input) ?? throw new InvalidDataException("최근 기록이 비어 있습니다.");
        if (document.Version != 1 || document.Entries is null || document.Entries.Count > Capacity)
            throw new InvalidDataException("지원하지 않는 최근 기록 형식입니다.");
        var entries = new List<RecentFileEntry>();
        foreach (var entry in document.Entries)
        {
            if (entry is null) throw new InvalidDataException("잘못된 최근 기록입니다.");
            var full = Normalize(entry.Path);
            if (!string.Equals(System.IO.Path.GetExtension(full), ".pdf", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("PDF 경로가 아닙니다.");
            if (!entries.Any(e => string.Equals(e.Path, full, StringComparison.OrdinalIgnoreCase))) entries.Add(entry with { Path = full });
        }
        return entries.OrderByDescending(e => e.LastOpenedUtc).ToList();
    }
    private void Change(Action<List<RecentFileEntry>> change)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(StoragePath)!);
        // A second instance must fail safely rather than silently lose a concurrent update.
        using var gate = new FileStream(StoragePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var entries = ReadEntries(); // Corrupt/unknown history is never overwritten.
        change(entries);
        var temp = StoragePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(output, new HistoryDocument(1, entries));
                output.Flush(true);
            }
            if (File.Exists(StoragePath)) File.Replace(temp, StoragePath, null);
            else File.Move(temp, StoragePath);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    internal static bool IsStorageFailure(Exception ex) => ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException or System.Security.SecurityException;
    private sealed record HistoryDocument(int Version, List<RecentFileEntry>? Entries);
}

public sealed record FileAccessResult(bool CanOpen, string? Message = null);
public interface IStartupFileProbe { FileAccessResult Check(string path); }
public sealed class StartupFileProbe : IStartupFileProbe
{
    public FileAccessResult Check(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !System.IO.Path.IsPathFullyQualified(path)) return new(false, "올바른 파일 경로가 아닙니다.");
            if (!string.Equals(System.IO.Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase)) return new(false, "PDF 파일을 선택해 주세요.");
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return new(true);
        }
        catch (FileNotFoundException) { return new(false, "파일을 찾을 수 없습니다. 이동되거나 삭제되었을 수 있습니다."); }
        catch (DirectoryNotFoundException) { return new(false, "폴더 또는 드라이브를 찾을 수 없습니다."); }
        catch (UnauthorizedAccessException) { return new(false, "파일에 접근할 권한이 없습니다."); }
        catch (Exception ex) when (JsonRecentFilesStore.IsStorageFailure(ex)) { return new(false, "파일을 읽을 수 없습니다. 드라이브 연결이나 다른 프로그램의 사용 여부를 확인해 주세요."); }
    }
}

/// <summary>Selection is not proof of PDF decoding. RecordOpened only after the caller really opens the document.</summary>
public sealed class StartupOpenModel
{
    public IRecentFilesStore Store { get; }
    public IStartupFileProbe Probe { get; }
    public RecentFilesSnapshot Snapshot { get; private set; } = new(Array.Empty<RecentFileEntry>());
    public string? SelectedPath { get; private set; }
    public string? Message { get; private set; }
    public StartupOpenModel(IRecentFilesStore store, IStartupFileProbe probe) { Store = store; Probe = probe; Refresh(); }
    public void Refresh()
    {
        try { Snapshot = Store.Read(); Message = Snapshot.Warning; }
        catch (Exception ex) when (JsonRecentFilesStore.IsStorageFailure(ex)) { Snapshot = new(Array.Empty<RecentFileEntry>()); Message = "최근 파일 기록을 읽지 못했습니다. 파일 불러오기를 이용해 주세요."; }
    }
    public bool TrySelect(string path)
    {
        SelectedPath = null;
        var result = Probe.Check(path);
        Message = result.Message;
        if (!result.CanOpen) return false;
        SelectedPath = System.IO.Path.GetFullPath(path);
        return true;
    }
    public bool Remove(string path)
    {
        try { Store.Remove(path); Refresh(); Message = null; return true; }
        catch (Exception ex) when (JsonRecentFilesStore.IsStorageFailure(ex)) { Message = "최근 목록을 변경하지 못했습니다. 기존 기록과 실제 파일은 보존했습니다."; return false; }
    }
}



