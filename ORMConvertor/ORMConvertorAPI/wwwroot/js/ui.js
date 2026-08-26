/*
 * Shared rendering helpers: syntax highlighting, artifact panels, artifact naming, the
 * records band and the catalog-state line. Screen-specific rendering stays in the page
 * modules.
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

/** Saves one artifact as a file. No server call, unlike the whole-output ZIP. */
export function saveText(content, fileName) {
  saveBlob(new Blob([content], { type: "text/plain;charset=utf-8" }), fileName);
}

/* ---- artifact panels ---------------------------------------------------- */

/*
 * Display names for artifacts: generated artifacts carry no names (input units do since
 * decision 066, output naming is a separate open item), so the class or mapped entity
 * name is read out of the content. A display heuristic, not a contract (decision 033).
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

/** The key a record is looked up by when the records band points at an artifact panel. */
const artifactKey = (entity, contentType) =>
  `${String(entity ?? "").toLowerCase()}|${contentType}`;

/*
 * Renders code panels for a list of artifacts and returns an index from entity name plus
 * content type to the id of the panel carrying it; the records band uses it to point at
 * the artifact a record is about. Both sides are the same display heuristic (decision
 * 033): a match means the two derived names agree, not that the server paired them.
 */
export function renderArtifacts(container, artifacts, options = {}) {
  const { idPrefix = "artifact" } = options;
  container.replaceChildren();
  const names = artifactNames(artifacts);
  const index = new Map();

  artifacts.forEach((artifact, position) => {
    const article = cloneTemplate("artifact-template");
    const id = `${idPrefix}-${position + 1}`;
    article.id = id;
    article.querySelector(".artifact-name").textContent = names[position];
    article.querySelector(".artifact-type").textContent =
      CONTENT_TYPE_LABELS[artifact.contentType] ?? "";

    const base = artifactBaseName(artifact);
    if (base) index.set(artifactKey(base, artifact.contentType), id);

    const buttons = article.querySelector(".artifact-actions");
    if (buttons) {
      const copy = buttons.querySelector(".artifact-copy");
      copy?.addEventListener("click", async () => {
        try {
          await navigator.clipboard.writeText(artifact.content);
          copy.textContent = "Copied";
        } catch {
          // Clipboard access can be refused; Download still works.
          copy.textContent = "Copy failed";
        }
        setTimeout(() => (copy.textContent = "Copy"), 1200);
      });
      buttons
        .querySelector(".artifact-download")
        ?.addEventListener("click", () => saveText(artifact.content, names[position]));
    }

    renderCode(article.querySelector("pre > code"), artifact.content, artifact.contentType);
    container.append(article);
  });

  return index;
}

/** Scrolls an artifact panel into view and marks it briefly, so the eye finds it. */
function revealArtifact(id) {
  const panel = document.getElementById(id);
  if (!panel) return;
  panel.scrollIntoView({ behavior: "smooth", block: "start" });
  panel.classList.remove("artifact-flash");
  // Reading a layout property restarts the animation when the same panel is picked twice.
  void panel.offsetWidth;
  panel.classList.add("artifact-flash");
  setTimeout(() => panel.classList.remove("artifact-flash"), 2000);
}

/* ---- records ------------------------------------------------------------ */

const RECORD_KIND_CLASSES = Object.freeze({
  [RecordKind.Failure]: "badge-failure",
  [RecordKind.Loss]: "badge-loss",
  [RecordKind.Convention]: "badge-convention",
  [RecordKind.Incompleteness]: "badge-incompleteness",
  [RecordKind.Supplied]: "badge-supplied",
  [RecordKind.Conflict]: "badge-conflict",
});

/*
 * One sentence per kind, from decision 010 and architecture.md 5.1. They sit on every
 * badge as a title and, for the kinds a result actually contains, in the legend above the
 * table: six bare words do not tell a first-time reader what separates Loss from
 * Incompleteness, and the records are the part of the output most worth reading.
 */
const RECORD_KIND_EXPLANATIONS = Object.freeze({
  [RecordKind.Failure]:
    "The artifact was not generated: a fact the target requires is missing, or emitting it would have changed what the source means.",
  [RecordKind.Loss]:
    "The source stated it and the target cannot express it. The artifact is valid, just poorer than the input.",
  [RecordKind.Convention]:
    "The output states something the source never did, filled in by the target framework's convention.",
  [RecordKind.Incompleteness]:
    "A fact the output would need is missing from the intermediate representation; it was generated from what there is.",
  [RecordKind.Supplied]:
    "The source did not state it and the database catalog did. The record is the fact's origin.",
  [RecordKind.Conflict]:
    "Two sources disagree. The translation continued with the earlier one; the record says what the later one claimed.",
});

/** A badge for one record kind, always carrying its explanation. */
export function kindBadge(kind, suffix) {
  const badge = document.createElement("span");
  badge.className = `badge ${RECORD_KIND_CLASSES[kind] ?? "badge-incompleteness"}`;
  const label = RECORD_KIND_LABELS[kind] ?? String(kind);
  badge.textContent = suffix ? `${label} ${suffix}` : label;
  badge.title = RECORD_KIND_EXPLANATIONS[kind] ?? "";
  return badge;
}

/** Counts per kind, ordered by the kind's own value. */
export function recordCounts(records) {
  const counts = new Map();
  for (const record of records ?? []) {
    counts.set(record.kind, (counts.get(record.kind) ?? 0) + 1);
  }
  return new Map([...counts.entries()].sort((a, b) => a[0] - b[0]));
}

function recordSubject(record) {
  if (record.category != null) return MAPPING_FACT_CATEGORY_LABELS[record.category] ?? "";
  if (record.feature != null) return QUERY_FEATURE_LABELS[record.feature] ?? "";
  return "";
}

/*
 * Renders the diagnostic records band: a per-kind summary that doubles as a filter, a
 * legend for the kinds this result contains, then a table grouped by entity with failures
 * first - a Failure means the entity's artifacts are missing (decision 033).
 *
 * `options.artifactIndex` is what renderArtifacts returned. Where a record's entity and
 * artifact type match a panel, the Artifact cell becomes a button that jumps to it.
 */
export function renderRecords(container, records, options = {}) {
  const { artifactIndex } = options;
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
  const legend = document.createElement("dl");
  legend.className = "record-legend";
  const buttons = new Map();
  let activeKind = null;

  for (const [kind, count] of recordCounts(records)) {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "badge-button";
    button.setAttribute("aria-pressed", "false");
    button.title = `${RECORD_KIND_EXPLANATIONS[kind] ?? ""} Click to show only these.`;
    button.append(kindBadge(kind, `x ${count}`));
    button.addEventListener("click", () => {
      activeKind = activeKind === kind ? null : kind;
      for (const [otherKind, otherButton] of buttons) {
        otherButton.setAttribute("aria-pressed", String(otherKind === activeKind));
      }
      renderRows();
    });
    buttons.set(kind, button);
    summary.append(button);

    const term = document.createElement("dt");
    term.append(kindBadge(kind));
    const description = document.createElement("dd");
    description.textContent = RECORD_KIND_EXPLANATIONS[kind] ?? "";
    legend.append(term, description);
  }
  container.append(summary, legend);

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
  for (const title of ["Kind", "Entity", "Property", "Unit", "Artifact", "Subject", "Reason"]) {
    const cell = document.createElement("th");
    cell.textContent = title;
    headRow.append(cell);
  }
  head.append(headRow);
  table.append(head);

  const body = document.createElement("tbody");
  table.append(body);
  figure.append(table);
  container.append(figure);

  function artifactCell(record) {
    const cell = document.createElement("td");
    if (record.artifact == null) return cell;
    const label = CONTENT_TYPE_LABELS[record.artifact] ?? "";
    const id = artifactIndex?.get(artifactKey(record.entity, record.artifact));
    if (!id) {
      cell.textContent = label;
      return cell;
    }
    const link = document.createElement("button");
    link.type = "button";
    link.className = "link-button";
    link.textContent = label;
    link.title = "Show the artifact this record is about";
    link.addEventListener("click", () => revealArtifact(id));
    cell.append(link);
    return cell;
  }

  function renderRows() {
    body.replaceChildren();
    const shown = activeKind == null ? sorted : sorted.filter((r) => r.kind === activeKind);
    for (const record of shown) {
      const row = document.createElement("tr");

      const kindCell = document.createElement("td");
      kindCell.append(kindBadge(record.kind));
      row.append(kindCell);

      // The unit is where the record came from (decision 066): the name the client sent
      // with the unit, or "unit N" by position where none was.
      for (const value of [record.entity ?? "", record.property ?? "", record.unit ?? ""]) {
        const cell = document.createElement("td");
        cell.textContent = value;
        row.append(cell);
      }

      row.append(artifactCell(record));

      const subjectCell = document.createElement("td");
      subjectCell.textContent = recordSubject(record);
      row.append(subjectCell);

      const reasonCell = document.createElement("td");
      reasonCell.className = "reason";
      reasonCell.textContent = record.reason ?? "";
      row.append(reasonCell);

      body.append(row);
    }
  }

  renderRows();
}

/* ---- catalog state ------------------------------------------------------ */

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
