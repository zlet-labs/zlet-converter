use serde::{Deserialize, Serialize};

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct WorkerRequest {
    pub id: String,
    pub source_path: String,
    pub output_path: String,
    pub asset_dir: Option<String>,
    pub source_format: Option<String>,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct WorkerResponse {
    pub id: String,
    pub success: bool,
    pub error_code: String,
    pub error_message: String,
    pub has_extracted_text: bool,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct HandshakeResponse {
    pub ready: bool,
    pub version: String,
    pub anydoc_version: String,
    pub anydoc_revision: String,
    pub error_code: String,
    pub error_message: String,
}