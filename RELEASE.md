# Release procedure

## Version and compatibility

The current working version is 1.0.5. Use the version in package.json as the authority for future releases; About reads it automatically.

Versions 1.0.2 through 1.0.4 use `autoDownload: false`; users on those versions must install 1.0.5 manually once. Starting with 1.0.5, the source implements automatic checking/downloading followed by explicit restart confirmation, fresh renderer dirty/busy checks and input locking. Checks occur after startup and every four hours. Normal quit never applies an update. Controller regression tests use a mock updater; actual Windows download/restart/install acceptance remains a separate requirement.

## Local release gate

From the repository root, after changing the version consistently in `package.json` and `package-lock.json`:

```powershell
npm ci
npm run typecheck
npm run test:regression
npm run build
node node_modules/electron-builder/cli.js --win nsis --x64 --publish never
node scripts/verify-release-package.cjs dist
```

Check that `dist/` contains the Windows NSIS x64 installer (`*.exe`), its blockmap (`*.exe.blockmap`), and `latest.yml`. These are generated and remain ignored by Git.

## Publish

1. For each authorized release, bump package.json/package-lock.json consistently and add `CHANGELOG-<version>.md`. Commit the approved changes and push `main`. A normal `main` push does not publish a release.
2. Create a tag exactly matching the package version, for example `v1.0.5`, and push that tag.
3. The GitHub Actions workflow uses Node 22 on Windows, runs the local release gate, and requires `v<package.json version>` to equal the pushed tag.
4. It creates a draft GitHub release in `bti0497-gif/PDF-Workspace`, uploads the installer, blockmap, and `latest.yml`, then downloads every asset and compares SHA-256 values before publication.
5. Only after hash verification does it publish the release. That published release becomes GitHub's latest release and provides `latest.yml` to the updater. An upload or verification failure must not create a published latest release; inspect and delete or repair the retained draft deliberately.

The workflow uses only the Actions-provided `GITHUB_TOKEN` with `contents: write`. Do not add repository secrets or plaintext credentials for this flow.

## Required real-world acceptance

The automated workflow does not verify actual installation. On Windows, manually test a clean install and an upgrade, moving the app across monitors, native font display, update download, explicit restart, and prevention of restart when unsaved changes need protection. Record results before presenting the release as fully accepted.
