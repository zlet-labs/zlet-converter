#!/usr/bin/env bash
set -euo pipefail

GIT_REF="${GIT_REF:-HEAD}"
CORPUS_PATH="${CORPUS_PATH:-}"
EVIDENCE_ROOT="${EVIDENCE_ROOT:-/srv/zlet-converter/evidence}"
BUILD_ROOT="${BUILD_ROOT:-/srv/zlet-converter/builds}"
RUN_LABEL="${RUN_LABEL:-public}"
KEEP_OUTPUTS="${KEEP_OUTPUTS:-1}"

usage() {
  cat <<'EOF'
Usage:
  scripts/run-linux-lab.sh [--git-ref REF] --corpus PATH [--evidence-root PATH] [--label NAME]

The corpus may be a directory or a ZIP. The runner builds the Linux headless
runtime from the exact resolved commit, converts supported inputs one-by-one,
and writes immutable evidence under EVIDENCE_ROOT.

Private corpus rule: point --corpus at a private local server path. The runner
never uploads source documents or converted outputs.
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --git-ref) GIT_REF="$2"; shift 2 ;;
    --corpus) CORPUS_PATH="$2"; shift 2 ;;
    --evidence-root) EVIDENCE_ROOT="$2"; shift 2 ;;
    --build-root) BUILD_ROOT="$2"; shift 2 ;;
    --label) RUN_LABEL="$2"; shift 2 ;;
    --no-outputs) KEEP_OUTPUTS=0; shift ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; usage >&2; exit 2 ;;
  esac
done

[[ -n "$CORPUS_PATH" ]] || { echo "--corpus is required" >&2; exit 2; }
[[ -e "$CORPUS_PATH" ]] || { echo "Corpus not found: $CORPUS_PATH" >&2; exit 2; }
command -v git >/dev/null
command -v cargo >/dev/null
command -v dotnet >/dev/null
command -v python3 >/dev/null
command -v sha256sum >/dev/null

REPO_ROOT="$(git rev-parse --show-toplevel)"
cd "$REPO_ROOT"
git fetch --quiet origin
COMMIT="$(git rev-parse "$GIT_REF^{commit}")"
SHORT="${COMMIT:0:12}"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
SAFE_LABEL="$(printf '%s' "$RUN_LABEL" | tr -cs 'A-Za-z0-9._-' '_')"
RUN_ID="${STAMP}-${SAFE_LABEL}-${SHORT}"
RUN_DIR="$EVIDENCE_ROOT/$RUN_ID"
BUILD_DIR="$BUILD_ROOT/$COMMIT"
RUNTIME_DIR="$BUILD_DIR/linux-x64"
WORK_DIR="$(mktemp -d)"
CORPUS_DIR="$WORK_DIR/corpus"
mkdir -p "$RUN_DIR" "$BUILD_DIR" "$CORPUS_DIR"

cleanup() { rm -rf "$WORK_DIR"; }
trap cleanup EXIT

if [[ -d "$CORPUS_PATH" ]]; then
  cp -a "$CORPUS_PATH"/. "$CORPUS_DIR"/
else
  case "$CORPUS_PATH" in
    *.zip) unzip -q "$CORPUS_PATH" -d "$CORPUS_DIR" ;;
    *) echo "Unsupported corpus container: $CORPUS_PATH" >&2; exit 2 ;;
  esac
fi

git archive "$COMMIT" | tar -x -C "$WORK_DIR"
SRC="$WORK_DIR/src"
mkdir -p "$SRC"
# git archive extracted at WORK_DIR root; isolate it without touching the checkout.
find "$WORK_DIR" -mindepth 1 -maxdepth 1 ! -name corpus ! -name src -exec mv {} "$SRC"/ \;

(
  cd "$SRC"
  cargo build --manifest-path src/Zlet.FolderConverter.AnydocWorker/Cargo.toml --release --locked
  dotnet publish src/Zlet.FolderConverter.Cli/Zlet.FolderConverter.Cli.csproj \
    -c Release -r linux-x64 --self-contained true -o "$RUNTIME_DIR"
  cp target/release/zlet-anydoc-worker "$RUNTIME_DIR/zlet-anydoc-worker"
  chmod +x "$RUNTIME_DIR/zlet-converter" "$RUNTIME_DIR/zlet-anydoc-worker"
) >"$RUN_DIR/build.log" 2>&1

INPUT_MANIFEST="$RUN_DIR/inputs.sha256"
(
  cd "$CORPUS_DIR"
  find . -type f -print0 | sort -z | xargs -0 -r sha256sum
) >"$INPUT_MANIFEST"

mkdir -p "$RUN_DIR/reports" "$RUN_DIR/logs"
[[ "$KEEP_OUTPUTS" == "1" ]] && mkdir -p "$RUN_DIR/outputs"

RESULTS_TSV="$RUN_DIR/results.tsv"
printf 'input\textension\texit_code\toutput_sha256\treport_sha256\tduration_ms\n' >"$RESULTS_TSV"

SUPPORTED_RE='\.(pdf|docx|pptx|xlsx|txt)$'
while IFS= read -r -d '' input; do
  rel="${input#"$CORPUS_DIR"/}"
  if [[ ! "$rel" =~ $SUPPORTED_RE ]]; then
    continue
  fi
  ext="${rel##*.}"
  key="$(printf '%s' "$rel" | sha256sum | cut -c1-16)"
  out_dir="$WORK_DIR/out-$key"
  report="$RUN_DIR/reports/$key.json"
  log="$RUN_DIR/logs/$key.log"
  mkdir -p "$out_dir"
  start_ns="$(date +%s%N)"
  set +e
  "$RUNTIME_DIR/zlet-converter" batch \
    --source "$(dirname "$input")" \
    --destination "$out_dir" \
    --target markdown \
    --recursive false \
    --report-json "$report" >"$log" 2>&1
  rc=$?
  set -e
  end_ns="$(date +%s%N)"
  duration_ms=$(( (end_ns - start_ns) / 1000000 ))
  output_file="$out_dir/$(basename "${input%.*}").md"
  output_sha=""
  report_sha=""
  [[ -f "$output_file" ]] && output_sha="$(sha256sum "$output_file" | awk '{print $1}')"
  [[ -f "$report" ]] && report_sha="$(sha256sum "$report" | awk '{print $1}')"
  printf '%s\t%s\t%s\t%s\t%s\t%s\n' "$rel" "$ext" "$rc" "$output_sha" "$report_sha" "$duration_ms" >>"$RESULTS_TSV"
  if [[ "$KEEP_OUTPUTS" == "1" && -f "$output_file" ]]; then
    cp "$output_file" "$RUN_DIR/outputs/$key.md"
  fi
done < <(find "$CORPUS_DIR" -type f -print0 | sort -z)

python3 - "$RUN_DIR" "$RUN_ID" "$COMMIT" "$CORPUS_PATH" "$RUN_LABEL" <<'PY'
import csv, hashlib, json, os, platform, subprocess, sys
run_dir, run_id, commit, corpus, label = sys.argv[1:]
rows = list(csv.DictReader(open(os.path.join(run_dir, "results.tsv"), encoding="utf-8"), delimiter="\t"))
payload = {
    "schema": "zlet-converter-linux-lab/v1",
    "runId": run_id,
    "label": label,
    "gitCommit": commit,
    "corpusPath": corpus,
    "environment": {
        "platform": platform.platform(),
        "machine": platform.machine(),
        "python": platform.python_version(),
    },
    "filesAttempted": len(rows),
    "commandFailures": sum(int(r["exit_code"]) != 0 for r in rows),
    "outputsProduced": sum(bool(r["output_sha256"]) for r in rows),
    "results": rows,
}
with open(os.path.join(run_dir, "runner-report.json"), "w", encoding="utf-8") as f:
    json.dump(payload, f, ensure_ascii=False, indent=2)
PY

sha256sum "$RUNTIME_DIR/zlet-converter" "$RUNTIME_DIR/zlet-anydoc-worker" >"$RUN_DIR/runtime.sha256"
sha256sum "$RUN_DIR/runner-report.json" >"$RUN_DIR/evidence.sha256"

attempted="$(awk 'NR>1{n++} END{print n+0}' "$RESULTS_TSV")"
[[ "$attempted" -gt 0 ]] || { echo "No supported inputs found" >&2; exit 3; }

echo "Run: $RUN_ID"
echo "Evidence: $RUN_DIR"
echo "Attempted: $attempted"
