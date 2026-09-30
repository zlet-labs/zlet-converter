#!/usr/bin/env bash
set -euo pipefail

GIT_REF="${GIT_REF:-HEAD}"
EVIDENCE_ROOT="${EVIDENCE_ROOT:-/srv/zlet-converter/evidence/wine}"
BUILD_ROOT="${BUILD_ROOT:-/srv/zlet-converter/builds/wine}"
RUN_LABEL="${RUN_LABEL:-wine-smoke}"
CORPUS_PATH="${CORPUS_PATH:-}"

usage() {
  cat <<'USAGE'
Usage: scripts/run-wine-smoke.sh [--git-ref REF] [--corpus PATH] [--evidence-root PATH] [--build-root PATH] [--label NAME]

Builds the Windows x64 self-contained headless runtime from the exact resolved
commit and runs a compatibility smoke under Wine on Linux. This is NOT packaged
Windows acceptance and does not validate WPF UI, installer, COM, or other
Windows-specific integrations.

Without --corpus the runner creates a deterministic TXT fixture. With --corpus
it also cross-builds the pinned Windows anydoc worker and can exercise the
PDF/DOCX/PPTX/XLSX conversion routes under Wine.
USAGE
}
while [[ $# -gt 0 ]]; do
  case "$1" in
    --git-ref) GIT_REF="$2"; shift 2 ;;
    --corpus) CORPUS_PATH="$2"; shift 2 ;;
    --evidence-root) EVIDENCE_ROOT="$2"; shift 2 ;;
    --build-root) BUILD_ROOT="$2"; shift 2 ;;
    --label) RUN_LABEL="$2"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; usage >&2; exit 2 ;;
  esac
done

for cmd in git dotnet wine python3 sha256sum cargo rustup x86_64-w64-mingw32-gcc; do command -v "$cmd" >/dev/null || { echo "Missing dependency: $cmd" >&2; exit 2; }; done
REPO_ROOT="$(git rev-parse --show-toplevel)"
cd "$REPO_ROOT"
git fetch --quiet origin
COMMIT="$(git rev-parse "$GIT_REF^{commit}")"
SHORT="${COMMIT:0:12}"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
SAFE_LABEL="$(printf '%s' "$RUN_LABEL" | tr -cs 'A-Za-z0-9._-' '_')"
RUN_ID="${STAMP}-${SAFE_LABEL}-${SHORT}"
RUN_DIR="$EVIDENCE_ROOT/$RUN_ID"
RUNTIME_DIR="$BUILD_ROOT/$COMMIT/win-x64-headless"
WORK_DIR="$(mktemp -d)"
SRC="$WORK_DIR/source-tree"
INPUT_DIR="$WORK_DIR/input"
OUTPUT_DIR="$WORK_DIR/output"
WINEPREFIX_DIR="$WORK_DIR/wineprefix"
mkdir -p "$RUN_DIR/logs" "$RUN_DIR/outputs" "$SRC" "$INPUT_DIR" "$OUTPUT_DIR" "$RUNTIME_DIR"
cleanup() { wineserver -k >/dev/null 2>&1 || true; rm -rf "$WORK_DIR"; }
trap cleanup EXIT

git archive "$COMMIT" | tar -x -C "$SRC"
(
  cd "$SRC"
  dotnet publish src/Zlet.FolderConverter.Cli/Zlet.FolderConverter.Cli.csproj \
    -c Release -r win-x64 --self-contained true -o "$RUNTIME_DIR"
  HOME=/root rustup target add x86_64-pc-windows-gnu --toolchain 1.88.0
  HOME=/root cargo +1.88.0 build --manifest-path src/Zlet.FolderConverter.AnydocWorker/Cargo.toml \
    --release --locked --target x86_64-pc-windows-gnu
  cp src/Zlet.FolderConverter.AnydocWorker/target/x86_64-pc-windows-gnu/release/zlet-anydoc-worker.exe "$RUNTIME_DIR/zlet-anydoc-worker.exe"
) >"$RUN_DIR/build.log" 2>&1

if [[ -n "$CORPUS_PATH" ]]; then
  [[ -e "$CORPUS_PATH" ]] || { echo "Corpus not found: $CORPUS_PATH" >&2; exit 2; }
  if [[ -d "$CORPUS_PATH" ]]; then cp -a "$CORPUS_PATH"/. "$INPUT_DIR"/; else
    case "$CORPUS_PATH" in *.zip) unzip -q "$CORPUS_PATH" -d "$INPUT_DIR" ;; *) echo "Unsupported corpus container" >&2; exit 2 ;; esac
  fi
else
  printf '# Zlet Wine smoke\n\nDeterministic Windows-runtime compatibility fixture.\n' >"$INPUT_DIR/wine-smoke.txt"
fi

mapfile -d '' INPUTS < <(find "$INPUT_DIR" -type f ! -iname 'manifest.json' \( -iname '*.txt' -o -iname '*.pdf' -o -iname '*.docx' -o -iname '*.pptx' -o -iname '*.xlsx' \) -print0 | sort -z)
[[ ${#INPUTS[@]} -gt 0 ]] || { echo "No supported smoke inputs found" >&2; exit 3; }
UNSUPPORTED="$(find "$INPUT_DIR" -type f ! -name manifest.json ! \( -iname '*.txt' -o -iname '*.pdf' -o -iname '*.docx' -o -iname '*.pptx' -o -iname '*.xlsx' \) | head -1 || true)"
[[ -z "$UNSUPPORTED" ]] || { echo "Unsupported Wine smoke input: $UNSUPPORTED" >&2; exit 3; }
(
  cd "$INPUT_DIR"
  find . -type f -print0 | sort -z | xargs -0 -r sha256sum
) >"$RUN_DIR/inputs.sha256"

REPORT="$RUN_DIR/conversion-report.json"
LOG="$RUN_DIR/logs/wine.log"
RUN_INPUT_DIR="$WORK_DIR/run-input"
mkdir -p "$RUN_INPUT_DIR"
for f in "${INPUTS[@]}"; do rel="${f#$INPUT_DIR/}"; mkdir -p "$RUN_INPUT_DIR/$(dirname "$rel")"; cp "$f" "$RUN_INPUT_DIR/$rel"; done
WIN_SOURCE="Z:$(printf '%s' "$RUN_INPUT_DIR" | sed 's#/#\\\\#g')"
WIN_DEST="Z:$(printf '%s' "$OUTPUT_DIR" | sed 's#/#\\\\#g')"
WIN_REPORT="Z:$(printf '%s' "$REPORT" | sed 's#/#\\\\#g')"
set +e
WINEPREFIX="$WINEPREFIX_DIR" WINEDEBUG=-all wine "$RUNTIME_DIR/zlet-converter.exe" batch \
  --source "$WIN_SOURCE" --destination "$WIN_DEST" --target markdown --recursive true --report-json "$WIN_REPORT" >"$LOG" 2>&1
RC=$?
set -e
printf '%s\n' "$RC" >"$RUN_DIR/exit-code.txt"
cp -a "$OUTPUT_DIR"/. "$RUN_DIR/outputs"/ 2>/dev/null || true

OUTPUT_COUNT="$(find "$OUTPUT_DIR" -type f -name '*.md' | wc -l)"
[[ "$RC" -eq 0 ]] || { echo "Wine conversion failed: exit $RC" >&2; exit 4; }
[[ -f "$REPORT" ]] || { echo "Wine conversion did not create report" >&2; exit 4; }
[[ "$OUTPUT_COUNT" -eq "${#INPUTS[@]}" ]] || { echo "Expected ${#INPUTS[@]} Markdown outputs, got $OUTPUT_COUNT" >&2; exit 4; }
while IFS= read -r -d '' f; do [[ -s "$f" ]] || { echo "Empty Markdown output: $f" >&2; exit 4; }; done < <(find "$OUTPUT_DIR" -type f -name '*.md' -print0)

python3 - "$RUN_DIR" "$RUN_ID" "$COMMIT" "$RUNTIME_DIR" "$OUTPUT_COUNT" <<'PY'
import hashlib, json, os, platform, subprocess, sys
run_dir, run_id, commit, runtime_dir, output_count = sys.argv[1:]
def version(cmd):
    p=subprocess.run(cmd, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    lines=[line.strip() for line in p.stdout.splitlines() if line.strip()]
    if cmd and cmd[0] == 'wine':
        return next((line for line in lines if line.lower().startswith('wine-')), lines[-1] if lines else '')
    return lines[0] if lines else ''
def sha(path):
    h=hashlib.sha256()
    with open(path,'rb') as f:
        for b in iter(lambda:f.read(1024*1024),b''): h.update(b)
    return h.hexdigest()
exe=os.path.join(runtime_dir,'zlet-converter.exe')
payload={
  'schema':'zlet-converter-wine-smoke/v1',
  'status':'PASS',
  'scope':'Windows x64 headless/core compatibility under Wine; not Windows acceptance',
  'runId':run_id,
  'gitCommit':commit,
  'environment':{'platform':platform.platform(),'machine':platform.machine(),'wine':version(['wine','--version']),'dotnet':version(['dotnet','--version'])},
  'runtime':{'path':runtime_dir,'exeSha256':sha(exe),'anydocWorkerSha256':sha(os.path.join(runtime_dir,'zlet-anydoc-worker.exe'))},
  'outputsProduced':int(output_count),
  'limitations':['WPF GUI not tested','installer not tested','COM/Office integration not tested','Windows anydoc worker is cross-built with Rust 1.88.0 GNU target and executed under Wine'],
}
with open(os.path.join(run_dir,'runner-report.json'),'w',encoding='utf-8') as f: json.dump(payload,f,ensure_ascii=False,indent=2)
PY
sha256sum "$RUNTIME_DIR/zlet-converter.exe" "$RUNTIME_DIR/zlet-anydoc-worker.exe" >"$RUN_DIR/runtime.sha256"
(
 cd "$RUN_DIR"
 find . -type f ! -name evidence.sha256 -print0 | sort -z | xargs -0 sha256sum
) >"$RUN_DIR/evidence.sha256"
echo "Run: $RUN_ID"
echo "Evidence: $RUN_DIR"
echo "Verdict: PASS (Wine compatibility smoke; NOT Windows acceptance)"
