from __future__ import annotations

import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
HANDOFF = ROOT / "quality-core-handoff" / "v0.1"
MANIFEST = HANDOFF / "handoff-manifest.json"


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def resolve(path: str) -> Path:
    if path.startswith("src/"):
        return ROOT / path
    return HANDOFF / path


def main() -> int:
    manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
    failures: list[str] = []
    for item in manifest["files"]:
        path = resolve(item["path"])
        if not path.is_file():
            failures.append(f"missing: {item['path']}")
            continue
        actual_hash = sha256(path)
        actual_size = path.stat().st_size
        if actual_hash != item["sha256"]:
            failures.append(f"sha256: {item['path']}: {actual_hash} != {item['sha256']}")
        if actual_size != item["sizeBytes"]:
            failures.append(f"size: {item['path']}: {actual_size} != {item['sizeBytes']}")

    profile = json.loads((HANDOFF / "qualification.json").read_text(encoding="utf-8"))
    if profile["profileId"] != manifest["profileId"]:
        failures.append("qualification profile identity does not match manifest")

    if failures:
        for failure in failures:
            print(failure)
        return 4

    print(json.dumps({
        "status": "PASS",
        "profileId": manifest["profileId"],
        "sourceCommit": manifest["sourceCommit"],
        "qualityCoreVersion": manifest["qualityCoreVersion"],
        "filesVerified": len(manifest["files"]),
        "manifestSha256": sha256(MANIFEST),
    }, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
