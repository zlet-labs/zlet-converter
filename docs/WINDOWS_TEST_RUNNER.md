# One-command Windows test runner

`scripts/run-windows-tests.ps1` turns a real Windows 11 x64 machine into a reproducible Zlet Converter acceptance runner. Manual and remote execution use the same entrypoint.

## Full local build + packaged acceptance

```powershell
powershell -ExecutionPolicy Bypass -File scripts/run-windows-tests.ps1 -GitRef main -Mode Full
```

Pin a commit for acceptance evidence:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/run-windows-tests.ps1 -GitRef <commit-sha> -Mode Full
```

## Existing package

```powershell
powershell -ExecutionPolicy Bypass -File scripts/run-windows-tests.ps1 -GitRef <commit-sha> -Mode Acceptance -PackagePath C:\Zlet\package
```

## Versioned test pack

The pack host is deliberately configurable. A Miami VPS can serve the manifest/archive without becoming a product dependency.

```powershell
powershell -ExecutionPolicy Bypass -File scripts/run-windows-tests.ps1 -GitRef <commit-sha> -Mode Full -TestPackManifestUrl https://test-host.example/zlet/packs/current.json
```

Manifest schema:

```json
{
  "id": "zc-testpack-2026-09-25",
  "archiveUrl": "https://test-host.example/zlet/packs/zc-testpack-2026-09-25.zip",
  "sha256": "<lowercase-or-uppercase-sha256>"
}
```

The archive is cached under LocalAppData by pack identity **and SHA-256**, so a verified cache entry is immutable. A cached hash mismatch is treated as corruption and fails instead of silently replacing that entry. A mismatched download fails before any conversion starts.

After verification the pack is extracted into the run evidence directory and passed to the same `test-packaged-windows.ps1` acceptance logic using `-TestSetPath`. The built-in packaged acceptance still runs first; an external pack adds coverage and never replaces or weakens the built-in assertions. Packs may include their own `manifest.json` to select supported acceptance fixtures and verify per-file hashes.

A branch-like `-GitRef` is resolved against the freshly fetched `origin/<ref>` and then checked out by exact commit SHA. Literal commit/tag refs remain supported. This prevents a stale local `main` from being mistaken for current remote `main`.

Each run writes a timestamped evidence directory plus ZIP containing `runner-report.json`, logs, the downloaded manifest when used, and the existing packaged-acceptance evidence.

The external pack is transport/evidence infrastructure only. Public reproducible fixtures and private enterprise documents must remain separated; never put private documents in public GitHub.
