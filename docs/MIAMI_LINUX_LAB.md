# Miami Linux conversion lab

Issue: #133

The Miami VPS is the primary persistent environment for repeatable conversion
quality, regression and performance runs. Windows packaged acceptance remains a
separate release gate because installer, portable packaging, GUI and
Windows-specific integration are different properties.

## What this lab checks

The Linux lab checks the conversion core against real document inputs using the
headless `zlet-converter` runtime. A run is identified by:

- exact git commit;
- corpus file hashes;
- runtime binary hashes;
- per-input exit status;
- output and report hashes;
- per-input duration;
- OS/runtime environment captured in the runner report.

The runner does not claim semantic quality merely because a command exits zero.
Its evidence is input to the project's quality/evidence review.

## Server layout

Recommended persistent paths:

```text
/srv/zlet-converter/
  test-packs/          public fixture packs only
  private-packs/       private real-world corpus, never web-served
  builds/              cached Linux runtimes by commit
  evidence/
    public/
    private/
```

The public HTTP test-pack endpoint must never expose `private-packs` or private
evidence.

## Run public fixtures

From a checkout of `zlet-labs/zlet-converter`:

```bash
bash scripts/run-linux-lab.sh \
  --git-ref main \
  --corpus /srv/zlet-converter/test-packs/zc-public-pack-5c2d1fae5d29.zip \
  --evidence-root /srv/zlet-converter/evidence/public \
  --label public
```

## Run private real-world acceptance

Copy the private pack to `/srv/zlet-converter/private-packs/` through an
authorized private channel, then run:

```bash
bash scripts/run-linux-lab.sh \
  --git-ref <exact-commit> \
  --corpus /srv/zlet-converter/private-packs/<pack>.zip \
  --evidence-root /srv/zlet-converter/evidence/private \
  --label private
```

Private source files and converted outputs are not uploaded by the runner.
Do not point nginx or another public file server at either private directory.

## Evidence

Each run creates an immutable run directory named with UTC time, label and
commit prefix. It contains:

- `runner-report.json`;
- `inputs.sha256`;
- `runtime.sha256`;
- `results.tsv`;
- build and per-input logs;
- conversion reports;
- converted Markdown outputs unless `--no-outputs` is used.

A failed conversion remains evidence. The runner records the failure rather
than silently deleting it.

## Windows release gate

Use Windows packaged acceptance only for properties that Linux cannot prove:

- installer behavior;
- portable Windows package behavior;
- Windows GUI startup/critical flows;
- Windows-specific Office/COM integration while it still exists.

This keeps routine corpus work on the already-paid Miami server and avoids a
personal-laptop dependency.
