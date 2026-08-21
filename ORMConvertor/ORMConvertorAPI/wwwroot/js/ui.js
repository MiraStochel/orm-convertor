/*
 * Shared rendering helpers: syntax highlighting, artifact naming, record tables and
 * the catalog-state line. Screen-specific rendering stays in the page modules.
 */

import hljs from "../vendor/highlightjs-11.12.0/core.min.js";
import csharp from "../vendor/highlightjs-11.12.0/languages/csharp.min.js";
import xml from "../vendor/highlightjs-11.12.0/languages/xml.min.js";
import sql from "../vendor/highlightjs-11.12.0/languages/sql.min.js";
import {
  ContentType,
  CONTENT_TYPE_EXTENSIONS,
  CONTENT_TYPE_HIGHLIGHT,
  CONTENT_TYPE_LABELS,
  RecordKind,
  RECORD_KIND_LABELS,
  CatalogState,
  MAPPING_FACT_CATEGORY_LABELS,
  QUERY_FEATURE_LABELS,
} from "./api.js";

hljs.registerLanguage("csharp", csharp);
hljs.registerLanguage("xml", xml);
hljs.registerLanguage("sql", sql);

export function cloneTemplate(id) {
  return document.getElementById(id).content.firstElementChild.cloneNode(true);
}

/** Fills a <code> element with content and applies syntax highlighting. */
export function renderCode(codeElement, content, contentType) {
  codeElement.textContent = content;
  const language = CONTENT_TYPE_HIGHLIGHT[contentType];
  if (language) {
    codeElement.className = `language-${language}`;
    hljs.highlightElement(codeElement);
  }
}

/*
 * Display names for artifacts: the server sends no names (units are nameless facts of
 * the conversion), so the class or mapped entity name is read out of the content.
 * A display heuristic, not a contract (decision 033).
 */
function artifactBaseName(artifact) {
  if (artifact.contentType === ContentType.Xml) {
    const match = artifact.content.match(/<class\s[^>]*name="([^"]+)"/);
    if (match) return match[1].split(",")[0].trim().split(".").pop();
    return null;
  }
  if (artifact.contentType === ContentType.CSharpEntity) {
    const match = artifact.content.match(/\bclass\s+([A-Za-z_]\w*)/);
    return match ? match[1] : null;
  }
  if (artifact.contentType === ContentType.CSharpQuery) {
    // Query artifacts are bare methods (decision 027), so a class name is rare.
    const match = artifact.content.match(/\bclass\s+([A-Za-z_]\w*)/);
    return match ? match[1] : "query";
  }
  return "query";
}

/** Unique display file names for a list of artifacts. */
export function artifactNames(artifacts) {
  const used = new Map();
  return artifacts.map((artifact, index) => {
    const extension = CONTENT_TYPE_EXTENSIONS[artifact.contentType] ?? ".txt";
    const base = artifactBaseName(artifact) ?? `artifact-${index + 1}`;
    const count = used.get(base + extension) ?? 0;
    used.set(base + extension, count + 1);
    return count === 0 ? `${base}${extension}` : `${base}-${count + 1}${extension}`;
  });
}

const RECORD_KIND_CLASSES = Object.freeze({
  [RecordKind.Failure]: "badge-failure",
  [RecordKind.Loss]: "badge-loss",
  [RecordKind.Convention]: "badge-convention",
  [RecordKind.Incompleteness]: "badge-incompleteness",
  [RecordKind.Supplied]: "badge-supplied",
  [RecordKind.Conflict]: "badge-conflict",
});

function kindBadge(kind, suffix) {
  const badge = document.createElement("span");
  badge.className = `badge ${RECORD_KIND_CLASSES[kind] ?? "badge-incompleteness"}`;
  badge.textContent = suffix
    ? `${RECORD_KIND_LABELS[kind] ?? kind} ${suffix}`
    : RECORD_KIND_LABELS[kind] ?? String(kind);
  return badge;
}

function recordSubject(record) {
  if (record.category != null) return MAPPING_FACT_CATEGORY_LABELS[record.category] ?? "";
  if (record.feature != null) return QUERY_FEATURE_LABELS[record.feature] ?? "";
  return "";
}

/*
 * Renders the diagnostic records band: a per-kind summary, then a table grouped by
 * entity with failures first - a Failure means the entity's artifacts are missing
 * (decision 033).
 */
export function renderRecords(container, records) {
  container.replaceChildren();

  if (!records || records.length === 0) {
    const note = document.createElement("p");
    note.textContent =
      "No records - the translation states nothing beyond the artifacts.";
    container.append(note);
    return;
  }

  const summary = document.createElement("div");
  summary.className = "record-summary";
  const counts = new Map();
  for (const record of records) {
    counts.set(record.kind, (counts.get(record.kind) ?? 0) + 1);
  }
  for (const kind of [...counts.keys()].sort((a, b) => a - b)) {
    summary.append(kindBadge(kind, `× ${counts.get(kind)}`));
  }
  container.append(summary);

  const sorted = [...records].sort((a, b) => {
    const failureFirst =
      (a.kind === RecordKind.Failure ? 0 : 1) - (b.kind === RecordKind.Failure ? 0 : 1);
    if (failureFirst !== 0) return failureFirst;
    return (a.entity ?? "").localeCompare(b.entity ?? "") || a.kind - b.kind;
  });

  const figure = document.createElement("figure");
  figure.className = "table-scroll";
  const table = document.createElement("table");
  table.className = "records-table";

  const head = document.createElement("thead");
  const headRow = document.createElement("tr");
  for (const title of ["Kind", "Entity", "Property", "Artifact", "Subject", "Reason"]) {
    const cell = document.createElement("th");
    cell.textContent = title;
    headRow.append(cell);
  }
  head.append(headRow);
  table.append(head);

  const body = document.createElement("tbody");
  for (const record of sorted) {
    const row = document.createElement("tr");

    const kindCell = document.createElement("td");
    kindCell.append(kindBadge(record.kind));
    row.append(kindCell);

    for (const value of [
      record.entity ?? "",
      record.property ?? "",
      record.artifact != null ? CONTENT_TYPE_LABELS[record.artifact] ?? "" : "",
      recordSubject(record),
    ]) {
      const cell = document.createElement("td");
      cell.textContent = value;
      row.append(cell);
    }

    const reasonCell = document.createElement("td");
    reasonCell.className = "reason";
    reasonCell.textContent = record.reason ?? "";
    row.append(reasonCell);

    body.append(row);
  }
  table.append(body);
  figure.append(table);
  container.append(figure);
}

const CATALOG_STATE_VIEWS = Object.freeze({
  [CatalogState.NotConfigured]: {
    label: "catalog not configured",
    className: "badge-catalog-notconfigured",
    explanation:
      "No catalog connection is configured on the server; the translation ran on conventions.",
  },
  [CatalogState.Unused]: {
    label: "catalog unused",
    className: "badge-catalog-unused",
    explanation:
      "A connection is configured, but the completion phase had nothing to ask.",
  },
  [CatalogState.Reached]: {
    label: "catalog read",
    className: "badge-catalog-reached",
    explanation: "Missing mapping facts were completed from the database catalog.",
  },
  [CatalogState.Unreachable]: {
    label: "catalog unreachable",
    className: "badge-catalog-unreachable",
    explanation:
      "A connection is configured but the read failed; the translation continued on conventions.",
  },
});

/** Renders the catalog-state badge plus its one-line explanation into a container. */
export function renderCatalogState(container, state, readMilliseconds) {
  container.replaceChildren();
  const view = CATALOG_STATE_VIEWS[state];
  if (!view) return;

  const badge = document.createElement("span");
  badge.className = `badge ${view.className}`;
  badge.textContent =
    readMilliseconds != null
      ? `${view.label} (${Math.round(readMilliseconds)} ms)`
      : view.label;
  badge.title = view.explanation;
  container.append(badge);
}

/** Offers a blob as a browser download. */
export function saveBlob(blob, fileName) {
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = fileName;
  document.body.append(link);
  link.click();
  link.remove();
  // The browser starts reading the blob URL after this turn of the event loop, so
  // revoking it synchronously can cancel the download it was created for.
  setTimeout(() => URL.revokeObjectURL(url), 0);
}
