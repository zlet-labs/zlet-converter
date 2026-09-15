# Third-party notices

## Microsoft .NET Runtime

The portable package contains the self-contained Microsoft .NET 8 runtime and
Windows Desktop runtime files produced by `dotnet publish`.

Microsoft .NET notices and license information:

- https://github.com/dotnet/runtime/blob/main/LICENSE.TXT
- https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT
- https://github.com/dotnet/wpf/blob/main/LICENSE.TXT
- https://github.com/dotnet/wpf/blob/main/THIRD-PARTY-NOTICES.TXT

Microsoft Office is not redistributed. The application automates a separately
installed local copy when the user selects a matching legacy format.

## anydoc and Rust dependencies

- **Component:** anydoc
- **Version:** 0.2.4
- **Pinned Revision:** `42bf1c5ecdde9eb0d96d6bd75a9e6698cf93b14c`
- **Upstream Source:** https://github.com/firecrawl/anydoc.git
- **License:** MIT (see `licenses/anydoc-MIT.txt`)
- **Bundled Status:** Compiled statically into `zlet-anydoc-worker.exe`
- **Runtime Behavior:** Executes strictly locally and offline as a child subprocess over standard input/output without making external network calls

The distributable package also contains `licenses/RUST_THIRD_PARTY_NOTICES.txt`.
That file is generated during packaging from the locked `zlet-anydoc-worker`
dependency graph using pinned `cargo-about 0.9.2`, the repository configuration
`licenses/cargo-about.toml`, and the template `licenses/cargo-about.hbs`.

The generated notice artifact preserves the license texts detected by
`cargo-about` from dependency source material and maps each text to the exact
package/version and available upstream metadata. It is intended as reproducible
redistribution evidence; it does not claim legal completeness or invent notices
that are absent from upstream sources.

`licenses/RUST_DEPENDENCIES.md` remains a human-readable dependency inventory.
The generated notice artifact is the packaged source of dependency-specific
license/attribution text.
