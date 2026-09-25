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

The archive is cached under LocalAppData by pack identity. It is reused only when its SHA-256 still matches. A mismatched download fails before acceptance starts.

Each run writes a timestamped evidence directory plus ZIP containing `runner-report.json`, logs, the downloaded manifest when used, and the existing packaged-acceptance evidence.

The external pack is transport/evidence infrastructure only. Public reproducible fixtures and private enterprise documents must remain separated; never put private documents in public GitHub.
