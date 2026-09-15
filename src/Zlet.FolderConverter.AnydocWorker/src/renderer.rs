use anydoc::model::{
    Asset, AssetId, Block, CellSlot, Document, ImageSource, Inline, LinkTarget, List, MarkerKind,
    Note, Table,
};
use std::collections::{HashMap, HashSet};
use std::fs;
use std::path::{Path, PathBuf};

pub struct RenderContext {
    pub asset_map: HashMap<usize, String>,
}

pub fn html_escape(s: &str) -> String {
    let mut out = String::with_capacity(s.len());
    for c in s.chars() {
        match c {
            '&' => out.push_str("&amp;"),
            '<' => out.push_str("&lt;"),
            '>' => out.push_str("&gt;"),
            '"' => out.push_str("&quot;"),
            '\'' => out.push_str("&#39;"),
            _ => out.push(c),
        }
    }
    out
}

pub fn sanitize_url(url: &str) -> String {
    let trimmed = url.trim();
    let lower = trimmed.to_ascii_lowercase();
    if lower.starts_with("javascript:")
        || lower.starts_with("data:")
        || lower.starts_with("vbscript:")
    {
        return "#".to_string();
    }
    html_escape(trimmed)
}

pub fn sanitize_markdown_url(url: &str) -> String {
    let trimmed = url.trim();
    let lower = trimmed.to_ascii_lowercase();
    if lower.starts_with("javascript:")
        || lower.starts_with("data:")
        || lower.starts_with("vbscript:")
    {
        return "#".to_string();
    }
    trimmed
        .replace(' ', "%20")
        .replace('(', "%28")
        .replace(')', "%29")
}

pub fn escape_markdown_text(text: &str) -> String {
    let mut out = String::with_capacity(text.len());
    let mut at_line_start = true;
    for c in text.chars() {
        match c {
            '\\' => out.push_str("\\\\"),
            '*' => out.push_str("\\*"),
            '_' => out.push_str("\\_"),
            '`' => out.push_str("\\`"),
            '[' => out.push_str("\\["),
            ']' => out.push_str("\\]"),
            '<' => out.push_str("\\<"),
            '>' if at_line_start => out.push_str("\\>"),
            '#' if at_line_start => out.push_str("\\#"),
            '\n' => {
                out.push('\n');
                at_line_start = true;
                continue;
            }
            _ => out.push(c),
        }
        at_line_start = false;
    }
    out
}

pub fn escape_link_label(text: &str) -> String {
    let mut out = String::with_capacity(text.len());
    for c in text.chars() {
        match c {
            '\\' => out.push_str("\\\\"),
            '[' => out.push_str("\\["),
            ']' => out.push_str("\\]"),
            _ => out.push(c),
        }
    }
    out
}

pub fn escape_code_span(text: &str) -> String {
    let max_backticks = text
        .split(|c| c != '`')
        .map(|s| s.len())
        .max()
        .unwrap_or(0);
    let delim = "`".repeat(max_backticks + 1);
    if text.starts_with('`') || text.ends_with('`') {
        format!("{} {} {}", delim, text, delim)
    } else {
        format!("{}{}{}", delim, text, delim)
    }
}

pub fn is_valid_image_mime(mime: &str) -> Option<&'static str> {
    match mime.to_ascii_lowercase().as_str() {
        "image/png" => Some("png"),
        "image/jpeg" | "image/jpg" => Some("jpg"),
        "image/gif" => Some("gif"),
        "image/webp" => Some("webp"),
        "image/svg+xml" => Some("svg"),
        "image/bmp" => Some("bmp"),
        "image/tiff" => Some("tiff"),
        _ => None,
    }
}
pub fn collect_referenced_asset_ids(doc: &Document) -> Vec<AssetId> {
    let mut referenced = Vec::new();
    let mut seen = HashSet::new();

    fn scan_inlines(
        inlines: &[Inline],
        referenced: &mut Vec<AssetId>,
        seen: &mut HashSet<usize>,
    ) {
        for inline in inlines {
            match inline {
                Inline::Image {
                    source: ImageSource::Asset(id),
                    ..
                } => {
                    if seen.insert(id.0) {
                        referenced.push(*id);
                    }
                }
                Inline::Link { content, .. } => scan_inlines(content, referenced, seen),
                _ => {}
            }
        }
    }

    fn scan_blocks(
        blocks: &[Block],
        referenced: &mut Vec<AssetId>,
        seen: &mut HashSet<usize>,
    ) {
        for b in blocks {
            match b {
                Block::Heading { content, .. } | Block::Paragraph(content) => {
                    scan_inlines(content, referenced, seen)
                }
                Block::List(l) => {
                    for item in &l.items {
                        scan_blocks(&item.blocks, referenced, seen);
                    }
                }
                Block::Table(t) => {
                    for row in &t.grid {
                        for slot in row {
                            if let CellSlot::Origin(cell) = slot {
                                scan_blocks(&cell.blocks, referenced, seen);
                            }
                        }
                    }
                }
                Block::BlockQuote(sub) => scan_blocks(sub, referenced, seen),
                _ => {}
            }
        }
    }

    scan_blocks(&doc.blocks, &mut referenced, &mut seen);
    for note in &doc.notes {
        scan_blocks(&note.blocks, &mut referenced, &mut seen);
    }
    referenced
}

pub fn export_assets(
    assets: &[Asset],
    doc: &Document,
    output_path: &Path,
    custom_asset_dir: Option<&str>,
) -> Result<HashMap<usize, String>, std::io::Error> {
    let mut asset_map = HashMap::new();
    let referenced_ids = collect_referenced_asset_ids(doc);
    if referenced_ids.is_empty() {
        return Ok(asset_map);
    }

    let output_dir = output_path
        .parent()
        .unwrap_or_else(|| Path::new("."));

    let stem = output_path
        .file_stem()
        .and_then(|s| s.to_str())
        .unwrap_or("document");

    let (asset_dir_path, asset_dir_rel) = if let Some(custom) = custom_asset_dir {
        let p = PathBuf::from(custom);
        let rel_name = p
            .file_name()
            .and_then(|s| s.to_str())
            .unwrap_or("assets")
            .to_string();
        (p, rel_name)
    } else {
        let dir_name = format!("{}_assets", stem);
        let p = output_dir.join(&dir_name);
        (p, dir_name)
    };

    fs::create_dir_all(&asset_dir_path)?;

    let asset_lookup: HashMap<usize, &Asset> = assets.iter().map(|a| (a.id.0, a)).collect();

    let mut counter = 1;
    for asset_id in referenced_ids {
        if let Some(asset) = asset_lookup.get(&asset_id.0) {
            let ext = match is_valid_image_mime(&asset.media_type) {
                Some(e) => e,
                None => {
                    let p_ext = Path::new(&asset.origin_part)
                        .extension()
                        .and_then(|s| s.to_str())
                        .unwrap_or("");
                    match p_ext.to_ascii_lowercase().as_str() {
                        "png" | "jpg" | "jpeg" | "gif" | "webp" | "svg" | "bmp" | "tiff" => p_ext,
                        _ => continue,
                    }
                }
            };

            let filename = format!("image-{:03}.{}", counter, ext);
            let target_file = asset_dir_path.join(&filename);
            fs::write(&target_file, &asset.bytes)?;

            let rel_path = format!("{}/{}", asset_dir_rel, filename);
            asset_map.insert(asset.id.0, rel_path);
            counter += 1;
        }
    }

    Ok(asset_map)
}

pub fn render_document_to_markdown(
    doc: &Document,
    asset_map: &HashMap<usize, String>,
) -> String {
    let ctx = RenderContext {
        asset_map: asset_map.clone(),
    };

    let mut out = String::new();

    for block in &doc.blocks {
        render_block(block, &ctx, 0, &mut out);
    }

    if !doc.notes.is_empty() {
        out.push_str("\n\n---\n\n");
        for note in &doc.notes {
            render_note(note, &ctx, &mut out);
        }
    }

    let trimmed = out.trim();
    if trimmed.is_empty() {
        String::new()
    } else {
        format!("{}\n", trimmed)
    }
}

fn render_note(note: &Note, ctx: &RenderContext, out: &mut String) {
    out.push_str(&format!("[^{}]: ", note.id));
    let mut note_content = String::new();
    for block in &note.blocks {
        render_block(block, ctx, 0, &mut note_content);
    }
    out.push_str(note_content.trim());
    out.push_str("\n\n");
}
fn render_block(block: &Block, ctx: &RenderContext, depth: usize, out: &mut String) {
    match block {
        Block::Heading { level, content, .. } => {
            out.push('\n');
            let clamped = (*level).clamp(1, 6) as usize;
            for _ in 0..clamped {
                out.push('#');
            }
            out.push(' ');
            render_inlines(content, ctx, out);
            out.push_str("\n\n");
        }
        Block::Paragraph(inlines) => {
            render_inlines(inlines, ctx, out);
            out.push_str("\n\n");
        }
        Block::List(list) => {
            render_list(list, ctx, depth, out);
            out.push('\n');
        }
        Block::Table(table) => {
            render_adaptive_table(table, ctx, out);
            out.push('\n');
        }
        Block::BlockQuote(blocks) => {
            let mut sub = String::new();
            for b in blocks {
                render_block(b, ctx, depth + 1, &mut sub);
            }
            for line in sub.lines() {
                out.push_str("> ");
                out.push_str(line);
                out.push('\n');
            }
            out.push('\n');
        }
        Block::CodeBlock { lang, text } => {
            out.push_str("```");
            if let Some(l) = lang {
                out.push_str(l);
            }
            out.push('\n');
            out.push_str(text);
            if !text.ends_with('\n') {
                out.push('\n');
            }
            out.push_str("```\n\n");
        }
        Block::Rule => {
            out.push_str("---\n\n");
        }
        Block::Math(m) => {
            out.push_str("$$\n");
            out.push_str(m.trim());
            out.push_str("\n$$\n\n");
        }
    }
}

fn render_list(list: &List, ctx: &RenderContext, depth: usize, out: &mut String) {
    let indent = "  ".repeat(depth);
    for (i, item) in list.items.iter().enumerate() {
        let prefix = if let Some(ref label) = item.marker_label {
            format!("{}{} ", indent, label.trim())
        } else if list.marker == MarkerKind::Bullet {
            format!("{}* ", indent)
        } else {
            format!("{}{}. ", indent, list.marker.ordinal(list.start + i as u64))
        };

        if item.blocks.is_empty() {
            out.push_str(&prefix);
            out.push('\n');
            continue;
        }

        for (b_idx, block) in item.blocks.iter().enumerate() {
            if b_idx == 0 {
                out.push_str(&prefix);
                match block {
                    Block::Paragraph(inlines) => {
                        render_inlines(inlines, ctx, out);
                        out.push('\n');
                    }
                    Block::List(sub_list) => {
                        out.push('\n');
                        render_list(sub_list, ctx, depth + 1, out);
                    }
                    _ => {
                        let mut sub = String::new();
                        render_block(block, ctx, depth + 1, &mut sub);
                        out.push_str(sub.trim());
                        out.push('\n');
                    }
                }
            } else {
                let sub_indent = "  ".repeat(depth + 1);
                match block {
                    Block::Paragraph(inlines) => {
                        out.push_str(&sub_indent);
                        render_inlines(inlines, ctx, out);
                        out.push('\n');
                    }
                    Block::List(sub_list) => {
                        render_list(sub_list, ctx, depth + 1, out);
                    }
                    _ => {
                        let mut sub = String::new();
                        render_block(block, ctx, depth + 1, &mut sub);
                        for line in sub.lines() {
                            out.push_str(&sub_indent);
                            out.push_str(line);
                            out.push('\n');
                        }
                    }
                }
            }
        }
    }
}

pub fn is_table_complex(table: &Table) -> bool {
    for row in &table.grid {
        for slot in row {
            match slot {
                CellSlot::Covered { .. } => return true,
                CellSlot::Origin(cell) => {
                    if cell.col_span > 1 || cell.row_span > 1 {
                        return true;
                    }
                    if cell.blocks.len() > 1 {
                        return true;
                    }
                    for b in &cell.blocks {
                        match b {
                            Block::List(_)
                            | Block::Table(_)
                            | Block::BlockQuote(_)
                            | Block::CodeBlock { .. }
                            | Block::Rule
                            | Block::Math(_) => {
                                return true;
                            }
                            _ => {}
                        }
                    }
                }
            }
        }
    }
    false
}

pub fn render_adaptive_table(table: &Table, ctx: &RenderContext, out: &mut String) {
    if table.grid.is_empty() {
        return;
    }

    if is_table_complex(table) {
        render_html_table(table, ctx, out);
    } else {
        render_gfm_table(table, ctx, out);
    }
}

pub fn render_gfm_table(table: &Table, ctx: &RenderContext, out: &mut String) {
    let width = table.grid.iter().map(|r| r.len()).max().unwrap_or(0);
    if width == 0 {
        return;
    }

    let mut rows: Vec<Vec<String>> = Vec::new();
    for r in &table.grid {
        let mut row_cells = Vec::new();
        for slot in r {
            match slot {
                CellSlot::Origin(cell) => {
                    let mut cell_text = String::new();
                    for (i, b) in cell.blocks.iter().enumerate() {
                        if i > 0 {
                            cell_text.push_str(" <br> ");
                        }
                        if let Block::Paragraph(inlines) = b {
                            let mut s = String::new();
                            render_inlines(inlines, ctx, &mut s);
                            cell_text.push_str(&s);
                        } else {
                            let mut s = String::new();
                            render_block(b, ctx, 0, &mut s);
                            cell_text.push_str(s.trim());
                        }
                    }
                    let clean = cell_text
                        .replace('\n', " ")
                        .replace('\r', "")
                        .replace('|', "\\|");
                    row_cells.push(clean.trim().to_string());
                }
                CellSlot::Covered { .. } => {
                    row_cells.push(String::new());
                }
            }
        }
        while row_cells.len() < width {
            row_cells.push(String::new());
        }
        rows.push(row_cells);
    }

    if rows.is_empty() {
        return;
    }

    out.push('\n');
    let delimiters: Vec<&str> = (0..width).map(|_| "---").collect();

    if table.header_rows >= 1 {
        out.push_str("| ");
        out.push_str(&rows[0].join(" | "));
        out.push_str(" |\n");

        out.push_str("| ");
        out.push_str(&delimiters.join(" | "));
        out.push_str(" |\n");

        for row in rows.iter().skip(1) {
            out.push_str("| ");
            out.push_str(&row.join(" | "));
            out.push_str(" |\n");
        }
    } else {
        let empty_headers: Vec<&str> = (0..width).map(|_| "").collect();
        out.push_str("| ");
        out.push_str(&empty_headers.join(" | "));
        out.push_str(" |\n");

        out.push_str("| ");
        out.push_str(&delimiters.join(" | "));
        out.push_str(" |\n");

        for row in &rows {
            out.push_str("| ");
            out.push_str(&row.join(" | "));
            out.push_str(" |\n");
        }
    }
    out.push('\n');
}
pub fn render_html_table(table: &Table, ctx: &RenderContext, out: &mut String) {
    out.push_str("<table>\n");

    let header_count = table.header_rows;
    let total_rows = table.grid.len();

    let render_row = |row: &[CellSlot], is_header: bool, out: &mut String| {
        out.push_str("  <tr>\n");
        for slot in row {
            match slot {
                CellSlot::Covered { .. } => {
                    continue;
                }
                CellSlot::Origin(cell) => {
                    let tag = if is_header { "th" } else { "td" };
                    out.push_str(&format!("    <{}", tag));
                    if cell.col_span > 1 {
                        out.push_str(&format!(" colspan=\"{}\"", cell.col_span));
                    }
                    if cell.row_span > 1 {
                        out.push_str(&format!(" rowspan=\"{}\"", cell.row_span));
                    }
                    out.push('>');

                    let mut cell_html = String::new();
                    render_cell_blocks_html(&cell.blocks, ctx, &mut cell_html);
                    out.push_str(&cell_html);

                    out.push_str(&format!("</{}>\n", tag));
                }
            }
        }
        out.push_str("  </tr>\n");
    };

    if header_count > 0 && header_count <= total_rows {
        out.push_str("<thead>\n");
        for r in 0..header_count {
            render_row(&table.grid[r], true, out);
        }
        out.push_str("</thead>\n");

        if header_count < total_rows {
            out.push_str("<tbody>\n");
            for r in header_count..total_rows {
                render_row(&table.grid[r], false, out);
            }
            out.push_str("</tbody>\n");
        }
    } else {
        out.push_str("<tbody>\n");
        for row in &table.grid {
            render_row(row, false, out);
        }
        out.push_str("</tbody>\n");
    }

    out.push_str("</table>\n\n");
}

pub fn render_cell_blocks_html(blocks: &[Block], ctx: &RenderContext, out: &mut String) {
    if blocks.len() == 1 {
        if let Block::Paragraph(inlines) = &blocks[0] {
            render_inlines_html(inlines, ctx, out);
            return;
        }
    }

    for b in blocks {
        match b {
            Block::Paragraph(inlines) => {
                out.push_str("<p>");
                render_inlines_html(inlines, ctx, out);
                out.push_str("</p>");
            }
            Block::Heading { level, content, .. } => {
                let clamped = (*level).clamp(1, 6);
                out.push_str(&format!("<h{}>", clamped));
                render_inlines_html(content, ctx, out);
                out.push_str(&format!("</h{}>", clamped));
            }
            Block::List(list) => {
                let tag = match list.marker {
                    MarkerKind::Bullet => "ul",
                    _ => "ol",
                };
                out.push_str(&format!("<{}>", tag));
                for item in &list.items {
                    out.push_str("<li>");
                    render_cell_blocks_html(&item.blocks, ctx, out);
                    out.push_str("</li>");
                }
                out.push_str(&format!("</{}>", tag));
            }
            Block::Table(sub_table) => {
                render_html_table(sub_table, ctx, out);
            }
            Block::BlockQuote(sub_blocks) => {
                out.push_str("<blockquote>");
                render_cell_blocks_html(sub_blocks, ctx, out);
                out.push_str("</blockquote>");
            }
            Block::CodeBlock { lang, text } => {
                out.push_str("<pre>");
                if let Some(l) = lang {
                    out.push_str(&format!("<code class=\"language-{}\">", html_escape(l)));
                } else {
                    out.push_str("<code>");
                }
                out.push_str(&html_escape(text));
                out.push_str("</code></pre>");
            }
            Block::Rule => {
                out.push_str("<hr />");
            }
            Block::Math(m) => {
                out.push_str(&format!("<div class=\"math\">{}</div>", html_escape(m.trim())));
            }
        }
    }
}

pub fn render_inlines_html(inlines: &[Inline], ctx: &RenderContext, out: &mut String) {
    for inline in inlines {
        match inline {
            Inline::Text { text, style } => {
                let escaped = html_escape(text);
                let mut s = escaped;
                if style.code {
                    s = format!("<code>{}</code>", s);
                }
                if style.bold {
                    s = format!("<strong>{}</strong>", s);
                }
                if style.italic {
                    s = format!("<em>{}</em>", s);
                }
                if style.strike {
                    s = format!("<del>{}</del>", s);
                }
                out.push_str(&s);
            }
            Inline::Link { content, target } => {
                let href = match target {
                    LinkTarget::External(u) | LinkTarget::Relative(u) => sanitize_url(u),
                    LinkTarget::Anchor(a) => format!("#{}", html_escape(a)),
                };
                out.push_str(&format!("<a href=\"{}\">", href));
                render_inlines_html(content, ctx, out);
                out.push_str("</a>");
            }
            Inline::Image { alt, source } => {
                let src = match source {
                    ImageSource::External(u) => sanitize_url(u),
                    ImageSource::Asset(id) => {
                        ctx.asset_map.get(&id.0).map(|s| html_escape(s)).unwrap_or_default()
                    }
                    ImageSource::Unavailable => String::new(),
                };
                out.push_str(&format!("<img src=\"{}\" alt=\"{}\" />", src, html_escape(alt)));
            }
            Inline::LineBreak => {
                out.push_str("<br />");
            }
            Inline::Checkbox(checked) => {
                out.push_str(if *checked { "[x]" } else { "[ ]" });
            }
            Inline::Math(m) => {
                out.push_str(&format!("<code>{}</code>", html_escape(m)));
            }
            Inline::Anchor(a) => {
                out.push_str(&format!("<span id=\"{}\"></span>", html_escape(a)));
            }
            Inline::NoteRef(n) => {
                out.push_str(&format!(
                    "<sup><a href=\"#{}\">[{}]</a></sup>",
                    html_escape(n),
                    html_escape(n)
                ));
            }
        }
    }
}

pub fn render_inlines(inlines: &[Inline], ctx: &RenderContext, out: &mut String) {
    for inline in inlines {
        match inline {
            Inline::Text { text, style } => {
                if style.code {
                    out.push_str(&escape_code_span(text));
                } else {
                    let escaped = escape_markdown_text(text);
                    let mut s = escaped;
                    if style.bold && style.italic {
                        s = format!("***{}***", s);
                    } else if style.bold {
                        s = format!("**{}**", s);
                    } else if style.italic {
                        s = format!("*{}*", s);
                    }
                    if style.strike {
                        s = format!("~~{}~~", s);
                    }
                    out.push_str(&s);
                }
            }
            Inline::Link { content, target } => {
                let url = match target {
                    LinkTarget::External(u) | LinkTarget::Relative(u) => sanitize_markdown_url(u),
                    LinkTarget::Anchor(a) => format!("#{}", a),
                };
                out.push('[');
                let mut label = String::new();
                render_inlines(content, ctx, &mut label);
                out.push_str(&escape_link_label(&label));
                out.push_str(&format!("]({})", url));
            }
            Inline::Image { alt, source } => {
                let url = match source {
                    ImageSource::External(u) => sanitize_markdown_url(u),
                    ImageSource::Asset(id) => {
                        ctx.asset_map.get(&id.0).cloned().unwrap_or_default()
                    }
                    ImageSource::Unavailable => String::new(),
                };
                out.push_str(&format!("![{}]({})", escape_link_label(alt), url));
            }
            Inline::LineBreak => {
                out.push_str("  \n");
            }
            Inline::Checkbox(checked) => {
                out.push_str(if *checked { "[x] " } else { "[ ] " });
            }
            Inline::Math(m) => {
                out.push_str(&format!("${}$", m.trim()));
            }
            Inline::Anchor(_) => {}
            Inline::NoteRef(n) => {
                out.push_str(&format!("[^{}]", n));
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use anydoc::model::*;

    #[test]
    fn test_escape_markdown_text() {
        let input = "*bold* _italic_ `code` [link] <tag> \\backslash";
        let escaped = escape_markdown_text(input);
        assert_eq!(escaped, "\\*bold\\* \\_italic\\_ \\`code\\` \\[link\\] \\<tag> \\\\backslash");

        let heading = "# Header\n> Quote";
        let escaped_heading = escape_markdown_text(heading);
        assert_eq!(escaped_heading, "\\# Header\n\\> Quote");
    }

    #[test]
    fn test_escape_code_span() {
        assert_eq!(escape_code_span("normal"), "`normal`");
        assert_eq!(escape_code_span("code with ` backtick"), "``code with ` backtick``");
        assert_eq!(escape_code_span("`leading"), "`` `leading ``");
        assert_eq!(escape_code_span("trailing`"), "`` trailing` ``");
    }

    #[test]
    fn test_sanitize_url() {
        assert_eq!(sanitize_url("javascript:alert(1)"), "#");
        assert_eq!(sanitize_url("JAVASCRIPT:evil()"), "#");
        assert_eq!(sanitize_url("data:text/html;base64,abc"), "#");
        assert_eq!(sanitize_url("vbscript:msgbox"), "#");
        assert_eq!(sanitize_url("https://example.com?a=1&b=2"), "https://example.com?a=1&amp;b=2");
    }

    #[test]
    fn test_sanitize_markdown_url() {
        assert_eq!(sanitize_markdown_url("javascript:alert(1)"), "#");
        assert_eq!(
            sanitize_markdown_url("https://example.com/foo bar(1)"),
            "https://example.com/foo%20bar%281%29"
        );
    }

    #[test]
    fn test_gfm_table_with_headers() {
        let table = Table {
            grid: vec![
                vec![
                    CellSlot::Origin(Cell::new(vec![Block::Paragraph(vec![Inline::plain("H1")])])),
                    CellSlot::Origin(Cell::new(vec![Block::Paragraph(vec![Inline::plain("H2")])])),
                ],
                vec![
                    CellSlot::Origin(Cell::new(vec![Block::Paragraph(vec![Inline::plain("V1")])])),
                    CellSlot::Origin(Cell::new(vec![Block::Paragraph(vec![Inline::plain("V2")])])),
                ],
            ],
            header_rows: 1,
            kind: TableKind::Data,
        };

        let ctx = RenderContext { asset_map: HashMap::new() };
        let mut out = String::new();
        render_adaptive_table(&table, &ctx, &mut out);

        let trimmed = out.trim();
        assert!(trimmed.contains("| H1 | H2 |"));
        assert!(trimmed.contains("| --- | --- |"));
        assert!(trimmed.contains("| V1 | V2 |"));
    }

    #[test]
    fn test_gfm_table_without_headers() {
        let table = Table {
            grid: vec![
                vec![
                    CellSlot::Origin(Cell::new(vec![Block::Paragraph(vec![Inline::plain("R1C1")])])),
                    CellSlot::Origin(Cell::new(vec![Block::Paragraph(vec![Inline::plain("R1C2")])])),
                ],
                vec![
                    CellSlot::Origin(Cell::new(vec![Block::Paragraph(vec![Inline::plain("R2C1")])])),
                    CellSlot::Origin(Cell::new(vec![Block::Paragraph(vec![Inline::plain("R2C2")])])),
                ],
            ],
            header_rows: 0,
            kind: TableKind::Data,
        };

        let ctx = RenderContext { asset_map: HashMap::new() };
        let mut out = String::new();
        render_adaptive_table(&table, &ctx, &mut out);

        let trimmed = out.trim();
        // Verifies synthetic empty header is emitted so row 0 is not lost
        assert!(trimmed.starts_with("|  |  |"));
        assert!(trimmed.contains("| --- | --- |"));
        assert!(trimmed.contains("| R1C1 | R1C2 |"));
        assert!(trimmed.contains("| R2C1 | R2C2 |"));
    }

    #[test]
    fn test_is_complex_table() {
        let simple_table = Table {
            grid: vec![vec![CellSlot::Origin(Cell::new(vec![Block::Paragraph(vec![Inline::plain("A")])]))]],
            header_rows: 0,
            kind: TableKind::Data,
        };
        assert!(!is_table_complex(&simple_table));

        let colspan_table = Table {
            grid: vec![vec![
                CellSlot::Origin(Cell {
                    blocks: vec![Block::Paragraph(vec![Inline::plain("Span")])],
                    col_span: 2,
                    row_span: 1,
                }),
                CellSlot::Covered { origin_row: 0, origin_col: 0 },
            ]],
            header_rows: 0,
            kind: TableKind::Data,
        };
        assert!(is_table_complex(&colspan_table));

        let multi_block_table = Table {
            grid: vec![vec![
                CellSlot::Origin(Cell {
                    blocks: vec![
                        Block::Paragraph(vec![Inline::plain("P1")]),
                        Block::Paragraph(vec![Inline::plain("P2")]),
                    ],
                    col_span: 1,
                    row_span: 1,
                }),
            ]],
            header_rows: 0,
            kind: TableKind::Data,
        };
        assert!(is_table_complex(&multi_block_table));

        let nested_list_table = Table {
            grid: vec![vec![
                CellSlot::Origin(Cell {
                    blocks: vec![
                        Block::List(List {
                            marker: MarkerKind::Bullet,
                            start: 1,
                            items: vec![ListItem {
                                blocks: vec![Block::Paragraph(vec![Inline::plain("Item")])],
                                marker_label: None,
                            }],
                        }),
                    ],
                    col_span: 1,
                    row_span: 1,
                }),
            ]],
            header_rows: 0,
            kind: TableKind::Data,
        };
        assert!(is_table_complex(&nested_list_table));
    }

    #[test]
    fn test_render_html_table() {
        let table = Table {
            grid: vec![
                vec![
                    CellSlot::Origin(Cell {
                        blocks: vec![Block::Paragraph(vec![Inline::plain("Header Span")])],
                        col_span: 2,
                        row_span: 1,
                    }),
                    CellSlot::Covered { origin_row: 0, origin_col: 0 },
                ],
                vec![
                    CellSlot::Origin(Cell::new(vec![Block::Paragraph(vec![Inline::plain("A")])])),
                    CellSlot::Origin(Cell::new(vec![Block::Paragraph(vec![Inline::plain("B")])])),
                ],
            ],
            header_rows: 1,
            kind: TableKind::Data,
        };

        let ctx = RenderContext { asset_map: HashMap::new() };
        let mut out = String::new();
        render_adaptive_table(&table, &ctx, &mut out);

        assert!(out.contains("<table>"));
        assert!(out.contains("<thead>"));
        assert!(out.contains("<th colspan=\"2\">"));
        assert!(out.contains("Header Span"));
        assert!(out.contains("<tbody>"));
        assert!(out.contains("<td>A</td>"));
        assert!(out.contains("<td>B</td>"));
        assert!(out.contains("</table>"));
    }

    #[test]
    fn test_render_list_with_markers() {
        let bullet_list = List {
            marker: MarkerKind::Bullet,
            start: 1,
            items: vec![
                ListItem {
                    blocks: vec![Block::Paragraph(vec![Inline::plain("First")])],
                    marker_label: None,
                },
                ListItem {
                    blocks: vec![Block::Paragraph(vec![Inline::plain("Second")])],
                    marker_label: None,
                },
            ],
        };
        let ctx = RenderContext { asset_map: HashMap::new() };
        let mut out = String::new();
        render_list(&bullet_list, &ctx, 0, &mut out);
        assert!(out.contains("* First\n"));
        assert!(out.contains("* Second\n"));

        let ordered_list = List {
            marker: MarkerKind::Decimal,
            start: 1,
            items: vec![
                ListItem {
                    blocks: vec![Block::Paragraph(vec![Inline::plain("One")])],
                    marker_label: None,
                },
                ListItem {
                    blocks: vec![Block::Paragraph(vec![Inline::plain("Two")])],
                    marker_label: None,
                },
                ListItem {
                    blocks: vec![Block::Paragraph(vec![Inline::plain("Custom")])],
                    marker_label: Some("3-a)".into()),
                },
            ],
        };
        let mut out_ordered = String::new();
        render_list(&ordered_list, &ctx, 0, &mut out_ordered);
        assert!(out_ordered.contains("1. One\n"));
        assert!(out_ordered.contains("2. Two\n"));
        assert!(out_ordered.contains("3-a) Custom\n"));
    }

    #[test]
    fn test_asset_collection_and_export() {
        let temp_dir = std::env::temp_dir().join(format!("zlet_test_assets_{}", std::process::id()));
        let _ = fs::remove_dir_all(&temp_dir);
        fs::create_dir_all(&temp_dir).unwrap();

        let output_md = temp_dir.join("test.md");

        let doc = Document {
            blocks: vec![
                Block::Paragraph(vec![
                    Inline::plain("Before"),
                    Inline::Image {
                        alt: "Test Image".into(),
                        source: ImageSource::Asset(AssetId(10)),
                    },
                    Inline::plain("After"),
                ]),
            ],
            notes: Vec::new(),
            assets: vec![
                Asset {
                    id: AssetId(10),
                    bytes: vec![0x89, 0x50, 0x4E, 0x47], // png header
                    media_type: "image/png".into(),
                    origin_part: "word/media/image1.png".into(),
                },
                Asset {
                    id: AssetId(99),
                    bytes: vec![1, 2, 3], // unreferenced asset
                    media_type: "image/png".into(),
                    origin_part: "word/media/image99.png".into(),
                },
            ],
        };

        let referenced = collect_referenced_asset_ids(&doc);
        assert_eq!(referenced.len(), 1);
        assert_eq!(referenced[0].0, 10);

        let map = export_assets(&doc.assets, &doc, &output_md, None).unwrap();
        assert_eq!(map.len(), 1);
        assert_eq!(map.get(&10).unwrap(), "test_assets/image-001.png");

        let target_file = temp_dir.join("test_assets").join("image-001.png");
        assert!(target_file.exists());
        assert_eq!(fs::read(&target_file).unwrap(), vec![0x89, 0x50, 0x4E, 0x47]);

        let _ = fs::remove_dir_all(&temp_dir);
    }
}
