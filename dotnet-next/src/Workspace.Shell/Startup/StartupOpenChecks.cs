#if STARTUP_CHECKS
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Workspace.Shell.Startup;
internal static class StartupOpenChecks
{
    private static int count;
    [STAThread]
    public static int Main(string[] args)
    {
        var root = Path.GetFullPath(args[0]); Directory.CreateDirectory(root);
        try
        {
            var document = Path.Combine(root, "실제 테스트 파일.pdf"); File.WriteAllText(document, "%PDF-1.4\n%%EOF");
            var missing = Path.Combine(root, "missing.pdf");
            var history = Path.Combine(root, "history", "recent-files.json");
            var store = new JsonRecentFilesStore(history);
            Check(store.Read().Entries.Count == 0, "empty first run has no synthetic entries");
            store.RecordOpened(document);
            Check(new JsonRecentFilesStore(history).Read().Entries.Single().Path == document, "history survives instance restart");
            store.RecordOpened(document.ToUpperInvariant());
            Check(store.Read().Entries.Count == 1, "case-insensitive deduplication");
            var model = new StartupOpenModel(store, new StartupFileProbe());
            Check(model.TrySelect(document) && model.SelectedPath == document, "actual readable PDF selection");
            Check(!model.TrySelect(missing) && model.SelectedPath is null && model.Message!.Contains("찾을"), "missing file clears stale selection and explains failure");
            Check(!model.TrySelect(root), "directory is rejected");
            var text = Path.Combine(root, "not-pdf.txt"); File.WriteAllText(text, "not pdf");
            Check(!model.TrySelect(text), "non-PDF selection rejected");
            Check(model.Remove(document) && File.Exists(document) && store.Read().Entries.Count == 0, "remove history never deletes document");
            store.RecordOpened(document);
            using (var locked = new FileStream(document, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Check(!new StartupFileProbe().Check(document).CanOpen, "sharing violation rejected");
            var original = File.ReadAllBytes(history);
            using (var gate = new FileStream(history + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                Check(!model.Remove(document), "concurrent writer fails safely");
                Check(File.ReadAllBytes(history).SequenceEqual(original), "concurrent write preserves history");
            }
            File.WriteAllText(history, "{broken"); var corrupt = File.ReadAllBytes(history);
            Check(store.Read().Warning is not null, "corruption produces clear warning");
            try { store.RecordOpened(document); throw new Exception("corruption overwritten"); } catch (System.Text.Json.JsonException) { count++; }
            Check(File.ReadAllBytes(history).SequenceEqual(corrupt), "corrupt history preserved exactly");
            File.WriteAllText(history, "{\"Version\":99,\"Entries\":[]}");
            Check(store.Read().Warning is not null, "future schema rejected safely");
            File.WriteAllBytes(history, original);
            for (var i = 0; i < 24; i++) { var p = Path.Combine(root, $"record-{i}.pdf"); File.WriteAllText(p, "%PDF-"); store.RecordOpened(p); }
            Check(store.Read().Entries.Count == 20, "bounded recent list");
            Check(!Directory.GetFiles(Path.GetDirectoryName(history)!, "*.tmp").Any(), "atomic writes leave no temporary files");
            Check(JsonRecentFilesStore.DefaultStoragePath.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), StringComparison.OrdinalIgnoreCase), "default is local-app-data scoped");
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var owner = new Window(); new WindowInteropHelper(owner).EnsureHandle();
            var fakeStore = new MemoryStore(new(document, DateTimeOffset.UtcNow), new(missing, DateTimeOffset.UtcNow));
            var picker = new TestPicker(document);
            var dialog = new StartupOpenWindow(owner, fakeStore, picker);
            Check(dialog.Owner == owner && !dialog.ShowInTaskbar && dialog.WindowStartupLocation == WindowStartupLocation.CenterOwner, "owner and modal chrome policy");
            Check(dialog.CancelButton.IsCancel, "Escape cancellation configured");
            Check(System.Windows.Input.KeyboardNavigation.GetTabNavigation(dialog) == System.Windows.Input.KeyboardNavigationMode.Cycle, "keyboard focus cycles within modal");
            Check(dialog.RecentList.Items.Count == 2, "actual persisted entries rendered");
            var firstRow = (Grid)((ListBoxItem)dialog.RecentList.Items[0]).Content;
            ((Button)firstRow.Children[0]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(dialog.SelectedPath == document, "single entry click selects actual path");
            dialog.Browse(); Check(picker.LastOwner == dialog && dialog.SelectedPath == document, "injected picker is modal-owned and selects real path");
            Check(fakeStore.RecordCount == 0, "selection does not falsely record successful document decoding");
            Check(!dialog.SelectPath(missing) && !string.IsNullOrWhiteSpace(dialog.Status.Text), "missing recent shows actionable error");
            Check(dialog.AllowDrop, "drag and drop enabled on startup window");
            Check(!dialog.TryDropFiles(new[] { text }) && dialog.Status.Text == "PDF 파일(.pdf)만 열 수 있습니다.", "dropping non-PDF shows actionable error");
            Check(!dialog.TryDropFiles(null) && dialog.Status.Text == "PDF 파일(.pdf)만 열 수 있습니다.", "dropping empty or null files rejected safely");
            Check(dialog.TryDropFiles(new[] { text, document }) && dialog.SelectedPath == document, "dropping PDF selects first PDF path");
            dialog.RemoveEntry(missing); Check(fakeStore.Read().Entries.Count == 1 && File.Exists(document), "UI removal only removes history entry");
            foreach (var size in new[] { new Size(644, 391), new Size(834, 501), new Size(1184, 721) })
            {
                dialog.Panel.Background = dialog.Background; dialog.Panel.Measure(size); dialog.Panel.Arrange(new Rect(size)); dialog.Panel.UpdateLayout();
                Check(dialog.BrowseButton.ActualWidth >= 150 && dialog.BrowseButton.ActualHeight >= 42, "responsive browse button remains usable");
                Check(dialog.RecentList.ActualWidth >= 290 && dialog.RecentList.ActualHeight > 100, "recent list retains usable area");
                var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(dialog.Panel);
                var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                using var output = File.Create(Path.Combine(root, $"startup-{size.Width}.png")); png.Save(output);
            }
            dialog.Close(); owner.Close(); app.Shutdown();
            Console.WriteLine($"PASS {count} startup checks. Offscreen only; no normal desktop or PDF decoding validation."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static void Check(bool passed, string name) { if (!passed) throw new Exception(name); count++; Console.WriteLine("PASS " + name); }
    private sealed class TestPicker(string path) : IStartupFilePicker { public Window? LastOwner; public string? PickPdf(Window owner) { LastOwner = owner; return path; } }
    private sealed class MemoryStore(params RecentFileEntry[] initial) : IRecentFilesStore
    {
        private readonly List<RecentFileEntry> entries = initial.ToList();
        public int RecordCount;
        public RecentFilesSnapshot Read() => new(entries.ToArray());
        public void RecordOpened(string path) => RecordCount++;
        public void Remove(string path) => entries.RemoveAll(e => e.Path == path);
    }
}
#endif

