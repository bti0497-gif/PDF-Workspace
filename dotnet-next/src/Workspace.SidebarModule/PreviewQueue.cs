using System.IO;
using System.Windows.Media.Imaging;
using Workspace.Contracts;
namespace Workspace.SidebarModule;

// One bounded queue for all pages. Visible pages move to the front without duplicating jobs.
// No PDF bytes are loaded here: each request asks the host for one small page preview.
internal sealed class PreviewQueue(IWorkspaceHost host) : IDisposable
{
    private readonly object gate = new();
    private readonly SemaphoreSlim slots = new(2, 2);
    private CancellationTokenSource lifetime = new();
    private LinkedList<int> pending = new();
    private readonly HashSet<int> running = new();
    private readonly Dictionary<int, BitmapSource> cache = new();
    private Guid document;
    private long version;
    private int generation;
    private bool disposed;
    public event Action<Guid, long, int, BitmapSource?, string?>? Completed;
    public void Reset(DocumentSnapshot snapshot)
    {
        lock (gate)
        {
            lifetime.Cancel(); lifetime.Dispose(); lifetime = new(); generation++;
            pending.Clear(); running.Clear(); cache.Clear();
            if (snapshot.DocumentId is not { } id) return;
            document = id; version = snapshot.ContentVersion;
            pending = new LinkedList<int>(Enumerable.Range(1, snapshot.PageCount));
            StartWorkers();
        }
    }
    private void StartWorkers()
    {
        var token = lifetime.Token; var current = generation; var id = document; var revision = version;
        for (var i = 0; i < 2; i++) _ = Task.Run(() => RunAsync(current, id, revision, token));
    }
    public void Prioritize(IEnumerable<int> pages)
    {
        lock (gate) foreach (var page in pages.Reverse())
            if (pending.Remove(page)) pending.AddFirst(page);
    }
    public void Retry(int page)
    {
        lock (gate)
        {
            if (disposed || running.Contains(page) || pending.Contains(page)) return;
            cache.Remove(page); pending.AddFirst(page); StartWorkers();
        }
    }
    private async Task RunAsync(int current, Guid id, long revision, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            int page;
            lock (gate)
            {
                if (current != generation || pending.First == null) return;
                page = pending.First.Value; pending.RemoveFirst(); running.Add(page);
            }
            BitmapSource? image = null; string? error = null; var entered = false;
            try
            {
                await slots.WaitAsync(token).ConfigureAwait(false); entered = true;
                var bytes = await host.RenderPageAsync(id, revision, page, 40, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                using var stream = new MemoryStream(bytes, writable: false);
                var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 220; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); image = bitmap;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (Exception ex) { error = ex.Message; }
            finally { if (entered) slots.Release(); }
            lock (gate)
            {
                if (disposed || current != generation || token.IsCancellationRequested) return;
                running.Remove(page); if (image != null) cache[page] = image;
                Completed?.Invoke(id, revision, page, image, error);
            }
        }
    }
    public void Dispose()
    {
        lock (gate) { disposed = true; generation++; lifetime.Cancel(); pending.Clear(); cache.Clear(); Completed = null; }
        // In-flight renderers may still release the semaphore. Do not dispose it prematurely.
    }
}

