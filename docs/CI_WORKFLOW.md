# CI workflow

Zlet Converter separates fast pull-request feedback from full main-branch verification.

## Pull requests

Every pull-request update runs the Windows CI job with:

- Rust worker tests and release build;
- .NET restore and Release build;
- .NET unit/regression tests.

The portable package verification step is intentionally skipped on pull-request runs. GitHub Actions concurrency cancels an older in-progress run when a newer commit is pushed to the same PR ref, so only the latest revision needs to finish.

## Main branch and manual full verification

A push to `main` runs the same build/test checks plus `scripts/publish-portable.ps1`. `workflow_dispatch` provides the same full verification gate on demand.

Packaged Windows runtime acceptance is a separate stage tracked by #105. Passing this workflow does not replace packaged acceptance where that evidence is required.

## Delivery status model

Repository Issues, PRs, CI and acceptance evidence are authoritative. A GitHub Project may reflect them using:

`Backlog -> Ready -> In Progress -> PR / CI -> Acceptance -> Done`

Opening or linking a PR represents `PR / CI`. A merge is not automatically equivalent to `Done` when packaged/runtime acceptance is required. Such work remains in `Acceptance` until the required evidence is recorded.
