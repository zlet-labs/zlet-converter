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

## anydoc

- **Component:** anydoc
- **Version:** 0.2.4
- **Pinned Revision:** `42bf1c5ecdde9eb0d96d6bd75a9e6698cf93b14c`
- **Upstream Source:** https://github.com/firecrawl/anydoc.git
- **License:** MIT (see `licenses/anydoc-MIT.txt`)
- **Bundled Status:** Compiled statically into `zlet-anydoc-worker.exe`
- **Runtime Behavior:** Executes strictly locally and offline as a child subprocess over standard input/output without making external network calls
- **Transitive Rust Dependencies:** Detailed inventory available in `licenses/RUST_DEPENDENCIES.md`, with complete third-party license and notice texts provided in `licenses/RUST_THIRD_PARTY_LICENSES.txt`
