# WPF update contract

- Bootstrap TEST3 0.3.0 is a manual install. Existing TEST2 has no updater.
- Read assembly version (Directory.Build.props must be 0.3.0), never Electron latest.
- GitHub repository `bti0497-gif/PDF-Workspace`; only prereleases, non-drafts, tags `wpf-vMAJOR.MINOR.PATCH`, highest newer version among the first 100 releases. Older beyond this bounded window are not scanned.
- Fixed manifest `wpf-update.json` (16 KiB cap), exact schema below, reject unknown/duplicate keys. Installer asset `PDF-Workspace-WPF-{version}-win-x64-setup.exe`.
- No release prose, command line, remote URL or filename is executed. URLs are constructed from the validated version/repository/asset. HTTPS redirects limited to GitHub asset hosts (5 hops). Metadata 45 sec/2 MiB, payload 10 min/512 MiB with exact length and SHA-256 checked again immediately before execution.
- SHA-256 is supplied by the same repository and does NOT authenticate a publisher. No signature authenticity claim. Compromised repository/publishing credentials remain a trust risk; signing/key pinning is a future trust gate.
- Normal startup checks/downloads, then every 6h; errors visible in update button tooltip/dialog and retry available. Smoke paths bypass startup check. No installation on quit or timer.
- Explicit update click requires fresh clean+idle state, confirmation, disabled app input during final rehash, then another state check. Dirty documents must be saved first; no automatic discard. Cancel leaves app/document unchanged.
- Invoke installer visibly with `/UPDATE /WAITPID=<current pid>`. Installer MUST wait for PID exit and acquire `Local\DeojonPdfDotNetTest3.Lifetime` before touching installed files. App lifetime mutex/transaction journal exclusion is installer integration responsibility. Installer currently has no restart contract; UI says run app manually afterward. No silent `/S` failure suppression.
- Test3 cache is local user-owned random subdirectory, CreateNew payload. Executable remains an untrusted downloaded binary until user accepts repo trust. Same-user local malware is not sandboxed. No persisted pending execution; normal next launch rechecks/re-downloads. Failed downloads clean their random directory. Successful old caches may remain.

Example (size/hash must be generated from final installer, never these placeholders):

```json
{"schemaVersion":1,"channel":"wpf-preview","repository":"bti0497-gif/PDF-Workspace","tag":"wpf-v0.3.0","version":"0.3.0","asset":"PDF-Workspace-WPF-0.3.0-win-x64-setup.exe","size":123,"sha256":"<64 hexadecimal characters>"}
```

Offline checks: `Workspace.Shell.exe --update-smoke <existing absolute test-output-dir>`. These mock HTTP and do not prove GitHub publication, native installer replacement, restart, or physical GUI acceptance.
