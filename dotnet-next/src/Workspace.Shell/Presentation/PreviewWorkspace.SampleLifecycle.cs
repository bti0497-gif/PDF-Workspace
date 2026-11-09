using System;
using System.Collections.Generic;
using System.Linq;

namespace Workspace.Shell.Presentation;

public enum SampleCloseDecision { Save, Discard, Cancel }
public sealed record SampleCloseRequest(bool IsApplicationExit, IReadOnlyList<string> DocumentNames);

public sealed partial class PreviewWorkspace
{
    private bool _confirmingClose, _closeReentered;
    private readonly List<SavedSample> _savedClosedSamples = new();
    private sealed record SavedSample(string Name, string Category, SamplePage[] Pages);
    public Func<SampleCloseRequest, SampleCloseDecision>? ConfirmClose { get; set; }
    public bool IsDirty => Current is { } d && DocumentDirty(d);
    public bool HasDirtyDocuments => _documents.Any(DocumentDirty);
    public IReadOnlyList<string> DirtyDocumentNames => _documents.Where(DocumentDirty).Select(d => d.Name).ToArray();
    public int SavedClosedSampleCount => _savedClosedSamples.Count;

    // Compare complete content, never view state or history depth. Copies isolate saved arrays.
    private static bool SameContent(SamplePage[] a, SamplePage[] b) => a.Length == b.Length &&
        a.Zip(b).All(pair => SamePage(pair.First, pair.Second));
    private static bool SamePage(SamplePage a, SamplePage b) =>
        a.Identity == b.Identity && a.Rotation == b.Rotation && a.IsBlank == b.IsBlank &&
        a.Title == b.Title && a.ShortTitle == b.ShortTitle && a.Introduction == b.Introduction &&
        a.Section == b.Section && a.Description == b.Description && a.Columns.SequenceEqual(b.Columns) &&
        a.Notes.SequenceEqual(b.Notes) && a.Rows.Length == b.Rows.Length &&
        a.Rows.Zip(b.Rows).All(pair => pair.First.SequenceEqual(pair.Second));
    private static bool DocumentDirty(SampleDocument d) => d.SavedPages == null || !SameContent(d.Pages, d.SavedPages);
    private static void Checkpoint(SampleDocument d) => d.SavedPages = d.Pages.Select(CopyPage).ToArray();

    public bool SaveCurrentSample()
    {
        if (_confirmingClose || Current is not { } d || !DocumentDirty(d)) return false;
        Checkpoint(d); RebuildTabs(); UpdateControls(); return true;
    }

    private SampleCloseDecision AskClose(bool exit, SampleDocument[] documents)
    {
        if (_confirmingClose) { _closeReentered = true; return SampleCloseDecision.Cancel; }
        _confirmingClose = true; _closeReentered = false;
        var open = _documents.ToArray();
        var content = open.Select(d => d.Pages.Select(CopyPage).ToArray()).ToArray();
        try
        {
            var decision = ConfirmClose?.Invoke(new SampleCloseRequest(exit, documents.Select(d => d.Name).ToArray()))
                ?? SampleCloseDecision.Cancel;
            // A nested callback must not approve a changed document set/content.
            if (_closeReentered || !_documents.SequenceEqual(open) || open.Where((d, i) => !SameContent(d.Pages, content[i])).Any())
                return SampleCloseDecision.Cancel;
            return decision is SampleCloseDecision.Save or SampleCloseDecision.Discard ? decision : SampleCloseDecision.Cancel;
        }
        catch { return SampleCloseDecision.Cancel; }
        finally { _confirmingClose = false; }
    }

    public bool ConfirmApplicationExit()
    {
        if (_confirmingClose) { _closeReentered = true; return false; }
        var dirty = _documents.Where(DocumentDirty).ToArray();
        if (dirty.Length == 0) return true;
        var decision = AskClose(true, dirty);
        if (decision == SampleCloseDecision.Cancel) return false;
        if (decision == SampleCloseDecision.Save)
        {
            foreach (var document in dirty) Checkpoint(document);
            RebuildTabs(); UpdateControls();
        }
        return true;
    }

    public bool ReopenLastSavedSample()
    {
        if (_confirmingClose || _savedClosedSamples.Count == 0) return false;
        var saved = _savedClosedSamples[^1];
        _savedClosedSamples.RemoveAt(_savedClosedSamples.Count - 1);
        _documents.Add(new SampleDocument(saved.Name, saved.Category, saved.Pages.Select(CopyPage).ToArray()));
        SelectDocument(_documents.Count - 1);
        return true;
    }
}
