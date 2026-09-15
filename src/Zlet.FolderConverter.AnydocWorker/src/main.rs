mod protocol;
mod renderer;

use anydoc::Format;
use protocol::{HandshakeResponse, WorkerRequest, WorkerResponse};
use std::fs::{self, File};
use std::io::{self, BufRead, Read, Write};
use std::path::Path;

const PROTOCOL_VERSION: &str = "1.0";
const ANYDOC_VERSION: &str = "0.2.4";
const ANYDOC_REVISION: &str = "42bf1c5ecdde9eb0d96d6bd75a9e6698cf93b14c";
const MAX_INPUT_BYTES: u64 = 512 * 1024 * 1024;

fn resolve_format(ext_or_fmt: Option<&str>, path: &Path, bytes: &[u8]) -> Option<Format> {
    if let Some(fmt_str) = ext_or_fmt {
        let lower = fmt_str.to_ascii_lowercase();
        match lower.as_str() {
            "docx" | "docm" => return Some(Format::Docx),
            "doc" => return Some(Format::Doc),
            "xlsx" | "xlsm" | "xlsb" => return Some(Format::Excel),
            "xls" => return Some(Format::Excel),
            "pptx" | "pptm" => return Some(Format::Pptx),
            "ppt" => return Some(Format::Ppt),
            "pdf" => return Some(Format::Pdf),
            "odt" => return Some(Format::Odt),
            "ods" => return Some(Format::Ods),
            "odp" => return Some(Format::Odp),
            "rtf" => return Some(Format::Rtf),
            "csv" => return Some(Format::Csv),
            _ => {}
        }
    }

    Format::from_bytes(bytes).or_else(|| Format::from_path(path))
}

fn map_convert_error(err: anydoc::ConvertError) -> (&'static str, &'static str) {
    match err {
        anydoc::ConvertError::Unsupported(_) => ("unsupported_format", "Unsupported document format."),
        anydoc::ConvertError::NeedsOcr { .. } => ("pdf_specialist_required", "Document requires OCR."),
        anydoc::ConvertError::Malformed { .. } => ("malformed_document", "Document structure is corrupted or malformed."),
        anydoc::ConvertError::Encrypted => ("document_encrypted", "Document is encrypted or password-protected."),
        anydoc::ConvertError::ResourceLimit { .. } => ("resource_limit", "Document exceeded resource safety limits."),
        anydoc::ConvertError::MissingPart { .. } => ("malformed_document", "Required document part is missing."),
        anydoc::ConvertError::Io(_) => ("read_error", "Failed to read document input."),
        _ => ("conversion_failed", "Document conversion failed."),
    }
}

fn exceeds_input_limit(len: u64) -> bool {
    len > MAX_INPUT_BYTES
}

fn read_input_bounded(path: &Path) -> io::Result<Result<Vec<u8>, u64>> {
    let metadata = fs::metadata(path)?;
    if exceeds_input_limit(metadata.len()) {
        return Ok(Err(metadata.len()));
    }

    let file = File::open(path)?;
    let initial_capacity = usize::try_from(metadata.len().min(8 * 1024 * 1024)).unwrap_or(0);
    let mut bytes = Vec::with_capacity(initial_capacity);
    let mut limited = file.take(MAX_INPUT_BYTES + 1);
    limited.read_to_end(&mut bytes)?;

    if exceeds_input_limit(bytes.len() as u64) {
        Ok(Err(bytes.len() as u64))
    } else {
        Ok(Ok(bytes))
    }
}

fn process_convert(req: &WorkerRequest) -> WorkerResponse {
    let source_path = Path::new(&req.source_path);
    let output_path = Path::new(&req.output_path);

    if !source_path.exists() {
        return WorkerResponse {
            id: req.id.clone(),
            success: false,
            error_code: "source_not_found".into(),
            error_message: "Source file not found.".into(),
            has_extracted_text: false,
        };
    }

    let bytes = match read_input_bounded(source_path) {
        Ok(Ok(b)) => b,
        Ok(Err(_)) => {
            return WorkerResponse {
                id: req.id.clone(),
                success: false,
                error_code: "resource_limit".into(),
                error_message: "Input file exceeds the 512 MiB worker safety limit.".into(),
                has_extracted_text: false,
            };
        }
        Err(e) => {
            return WorkerResponse {
                id: req.id.clone(),
                success: false,
                error_code: "read_error".into(),
                error_message: format!("Failed to read source file: {}", e),
                has_extracted_text: false,
            };
        }
    };

    let format = match resolve_format(req.source_format.as_deref(), source_path, &bytes) {
        Some(f) => f,
        None => {
            return WorkerResponse {
                id: req.id.clone(),
                success: false,
                error_code: "unsupported_format".into(),
                error_message: "Unrecognized document format.".into(),
                has_extracted_text: false,
            };
        }
    };

    // Ensure parent output directory exists
    if let Some(parent) = output_path.parent() {
        let _ = fs::create_dir_all(parent);
    }

    if format == Format::Pdf {
        match anydoc::to_markdown_bytes(&bytes, Format::Pdf) {
            Ok(md) => {
                let trimmed = md.trim();
                if trimmed.is_empty() {
                    return WorkerResponse {
                        id: req.id.clone(),
                        success: false,
                        error_code: "pdf_specialist_required".into(),
                        error_message: "PDF contains no extractable text (OCR required).".into(),
                        has_extracted_text: false,
                    };
                }

                if let Err(e) = fs::write(output_path, &md) {
                    return WorkerResponse {
                        id: req.id.clone(),
                        success: false,
                        error_code: "write_error".into(),
                        error_message: format!("Failed to write markdown output: {}", e),
                        has_extracted_text: false,
                    };
                }

                return WorkerResponse {
                    id: req.id.clone(),
                    success: true,
                    error_code: String::new(),
                    error_message: String::new(),
                    has_extracted_text: true,
                };
            }
            Err(e) => {
                let (code, msg) = map_convert_error(e);
                return WorkerResponse {
                    id: req.id.clone(),
                    success: false,
                    error_code: code.into(),
                    error_message: msg.into(),
                    has_extracted_text: false,
                };
            }
        }
    }

    // Non-PDF documents: parse to structured AST model
    let doc = match anydoc::to_document(&bytes, format) {
        Ok(d) => d,
        Err(e) => {
            let (code, msg) = map_convert_error(e);
            return WorkerResponse {
                id: req.id.clone(),
                success: false,
                error_code: code.into(),
                error_message: msg.into(),
                has_extracted_text: false,
            };
        }
    };

    // Export assets if any
    let asset_map = match renderer::export_assets(&doc.assets, &doc, output_path, req.asset_dir.as_deref()) {
        Ok(m) => m,
        Err(e) => {
            return WorkerResponse {
                id: req.id.clone(),
                success: false,
                error_code: "asset_export_error".into(),
                error_message: format!("Asset extraction failed: {}", e),
                has_extracted_text: false,
            };
        }
    };

    // Render using adaptive hybrid table renderer
    let markdown = renderer::render_document_to_markdown(&doc, &asset_map);

    if let Err(e) = fs::write(output_path, &markdown) {
        return WorkerResponse {
            id: req.id.clone(),
            success: false,
            error_code: "write_error".into(),
            error_message: format!("Failed to write markdown output: {}", e),
            has_extracted_text: false,
        };
    }

    let has_text = !markdown.trim().is_empty();

    WorkerResponse {
        id: req.id.clone(),
        success: true,
        error_code: String::new(),
        error_message: String::new(),
        has_extracted_text: has_text,
    }
}

fn print_handshake() {
    let handshake = HandshakeResponse {
        ready: true,
        version: PROTOCOL_VERSION.into(),
        anydoc_version: ANYDOC_VERSION.into(),
        anydoc_revision: ANYDOC_REVISION.into(),
        error_code: String::new(),
        error_message: String::new(),
    };
    let json = serde_json::to_string(&handshake).unwrap();
    println!("{}", json);
    let _ = io::stdout().flush();
}

fn main() {
    let args: Vec<String> = std::env::args().collect();

    if args.len() > 1 && args[1] == "--handshake" {
        print_handshake();
        return;
    }

    if args.len() > 1 && args[1] == "--convert" {
        if args.len() < 4 {
            eprintln!("Usage: zlet-anydoc-worker --convert <source> <output> [--format <format>] [--asset-dir <asset_dir>]");
            std::process::exit(1);
        }
        let source = args[2].clone();
        let output = args[3].clone();
        let mut format = None;
        let mut asset_dir = None;

        let mut i = 4;
        while i < args.len() {
            if args[i] == "--format" && i + 1 < args.len() {
                format = Some(args[i + 1].clone());
                i += 2;
            } else if args[i] == "--asset-dir" && i + 1 < args.len() {
                asset_dir = Some(args[i + 1].clone());
                i += 2;
            } else {
                i += 1;
            }
        }

        let req = WorkerRequest {
            id: "cli".into(),
            source_path: source,
            output_path: output,
            asset_dir,
            source_format: format,
        };

        let res = process_convert(&req);
        let json = serde_json::to_string(&res).unwrap();
        println!("{}", json);
        if !res.success {
            std::process::exit(2);
        }
        return;
    }

    // Default: Stdin/Stdout JSON lines server
    print_handshake();

    let stdin = io::stdin();
    for line in stdin.lock().lines() {
        let line = match line {
            Ok(l) => l,
            Err(_) => break,
        };
        let trimmed = line.trim();
        if trimmed.is_empty() {
            continue;
        }

        let req: WorkerRequest = match serde_json::from_str(trimmed) {
            Ok(r) => r,
            Err(e) => {
                let err_res = WorkerResponse {
                    id: "unknown".into(),
                    success: false,
                    error_code: "invalid_request".into(),
                    error_message: format!("Invalid request json: {}", e),
                    has_extracted_text: false,
                };
                let json = serde_json::to_string(&err_res).unwrap();
                println!("{}", json);
                let _ = io::stdout().flush();
                continue;
            }
        };

        let res = process_convert(&req);
        let json = serde_json::to_string(&res).unwrap();
        println!("{}", json);
        let _ = io::stdout().flush();
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn input_size_limit_accepts_boundary_and_rejects_larger_files() {
        assert_eq!(MAX_INPUT_BYTES, 512 * 1024 * 1024);
        assert!(!exceeds_input_limit(MAX_INPUT_BYTES));
        assert!(exceeds_input_limit(MAX_INPUT_BYTES + 1));
    }
}
