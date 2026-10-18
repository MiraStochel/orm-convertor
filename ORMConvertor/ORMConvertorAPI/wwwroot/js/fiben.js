/*
 * The FIBEN page (decision 149): the 237 distinct SQL queries of the FIBEN benchmark, each with
 * the natural-language question it answers, Kim's type of its nesting and how much of it the
 * tool translates. The catalog is the server's (GET /fiben); the on-demand run of both suites
 * holds what it claims, and this page lets the reader translate any query through /convert -
 * the entities of the tables it reads plus the query, from Dapper into a chosen framework - and
 * fill a column of the overview by translating every query into one target, so that the numbers
 * of the recorded run can be watched, not only read.
 */

import {
  ORM,
  ORM_LABELS,
  ContentType,
  FibenNesting,
  FIBEN_NESTING_LABELS,
  FibenTranslation,
  FIBEN_TRANSLATION_LABELS,
  getFiben,
  convert,
} from "./api.js";
import { cloneTemplate, renderCode, renderArtifacts, renderRecords, renderCatalogState } from "./ui.js";

const QUERY_CONTENT_TYPES = new Set([
  ContentType.CSharpQuery,
  ContentType.CSharpLinqQuery,
  ContentType.SqlQuery,
  ContentType.HqlQuery,
  ContentType.JavaQuery,
  ContentType.JpqlQuery,
]);

const isQueryArtifact = (artifact) =>
  QUERY_CONTENT_TYPES.has(artifact.contentType) ||
  (artifact.contentType === ContentType.Xml && artifact.content.includes("<select"));

const TRANSLATION_CLASSES = Object.freeze({
  [FibenTranslation.AsSpecified]: "badge-ldbc-as-specified",
  [FibenTranslation.Simplified]: "badge-ldbc-simplified",
  [FibenTranslation.NotTranslated]: "badge-ldbc-not-translated",
});

const NESTING_ORDER = [FibenNesting.NonNested, FibenNesting.TypeN, FibenNesting.TypeA, FibenNesting.TypeJ, FibenNesting.TypeJA];

const NESTING_TITLES = Object.freeze({
  [FibenNesting.NonNested]: "Single-block queries",
  [FibenNesting.TypeN]: "Type N - an uncorrelated inner block without an aggregate",
  [FibenNesting.TypeA]: "Type A - an uncorrelated inner block with an aggregate",
  [FibenNesting.TypeJ]: "Type J - a correlated inner block without an aggregate",
  [FibenNesting.TypeJA]: "Type JA - a correlated inner block with an aggregate",
});

const TARGETS = [ORM.EFCore, ORM.NHibernate, ORM.Hibernate, ORM.EclipseLink, ORM.MyBatis];

const TARGET_LANGUAGES = Object.freeze({
  [ORM.EFCore]: "LINQ",
  [ORM.NHibernate]: "HQL",
  [ORM.Hibernate]: "HQL",
  [ORM.EclipseLink]: "JPQL",
  [ORM.MyBatis]: "SQL",
});

const plural = (count, word) => `${count} ${word}${count === 1 ? "" : "s"}`;

/** The entity units a query is sent with: those of the tables it reads, as the run sends them. */
function unitsOf(catalog, query) {
  const tables = new Set(query.tables.map((table) => table.toUpperCase()));
  return catalog.entities.filter((entity) => tables.has(entity.name.replace(/\.cs$/i, "").toUpperCase()));
}

/** One translation through /convert, returned as the outcome the overview and the article show. */
async function translateQuery(catalog, query, target) {
  const unitName = `${query.key}.sql`;
  const response = await convert(catalog.sourceOrm, target, [
    ...unitsOf(catalog, query),
    { name: unitName, contentType: ContentType.SqlQuery, content: query.sql },
  ]);

  const outputs = response.sources.filter(isQueryArtifact);
  const refused = response.records.some((record) => record.kind === 1);
  const fellBack = response.records.some((record) => record.kind === 7);
  const reason = response.records
    .filter((record) => record.kind === 1 || record.kind === 7)
    .map((record) => record.reason)
    .join(" ");

  return { response, outputs, unitName, refused, fellBack, reason };
}

async function translate(catalog, query, article) {
  const target = Number(article.querySelector(".query-target").value);
  const status = article.querySelector(".query-status");
  const errorElement = article.querySelector(".query-error");
  const result = article.querySelector(".query-result");

  errorElement.hidden = true;
  status.hidden = false;
  status.setAttribute("aria-busy", "true");
  status.textContent = "Translating…";

  try {
    const { response, outputs, unitName } = await translateQuery(catalog, query, target);

    const artifactIndex = renderArtifacts(article.querySelector(".query-outputs"), outputs, {
      idPrefix: `${query.key}-output`,
    });

    const records = response.records.filter(
      (record) => record.unit === unitName || record.query != null || record.feature != null,
    );
    renderRecords(article.querySelector(".query-records"), records, { artifactIndex });

    status.removeAttribute("aria-busy");
    status.replaceChildren(
      `Dapper → ${ORM_LABELS[response.targetFramework]} ${response.targetFrameworkVersion}: ` +
        (outputs.length > 0 ? `${plural(outputs.length, "query artifact")}. ` : "no query artifact. "),
    );
    const catalogBadge = document.createElement("span");
    renderCatalogState(catalogBadge, response.catalogState, response.catalogReadMilliseconds);
    status.append(catalogBadge);
    result.hidden = false;
  } catch (error) {
    status.hidden = true;
    errorElement.textContent = error.message;
    errorElement.hidden = false;
  }
}

function renderQuery(catalog, query) {
  const article = cloneTemplate("query-template");
  article.id = query.key;

  article.querySelector(".query-title").textContent = `Query ${query.number} — ${query.questions[0]}`;
  const badge = article.querySelector(".query-translation");
  badge.textContent = FIBEN_TRANSLATION_LABELS[query.translation] ?? "";
  badge.classList.add(TRANSLATION_CLASSES[query.translation] ?? "badge-incompleteness");

  const nesting = article.querySelector(".query-nesting");
  nesting.textContent = FIBEN_NESTING_LABELS[query.nesting] ?? "";

  const facts = article.querySelector(".query-facts");
  const paraphrases = query.questions.slice(1);
  if (paraphrases.length > 0) {
    const item = document.createElement("li");
    item.append(document.createElement("strong"));
    item.firstChild.textContent = paraphrases.length === 1 ? "Also asked as: " : "Also asked as: ";
    item.append(paraphrases.map((question) => `“${question}”`).join(" · "));
    facts.append(item);
  }
  const tables = document.createElement("li");
  const tablesLabel = document.createElement("strong");
  tablesLabel.textContent = "Reads: ";
  tables.append(tablesLabel);
  query.tables.forEach((table, index) => {
    const code = document.createElement("code");
    code.textContent = table;
    tables.append(index === 0 ? code : ", ", index === 0 ? "" : code);
  });
  facts.append(tables);

  if (query.note) {
    article.querySelector(".query-note").textContent = query.note;
    article.querySelector(".query-note").hidden = false;
  }

  const text = article.querySelector(".query-text");
  renderCode(text.querySelector("pre > code"), query.sql, ContentType.SqlQuery);
  text.querySelector(".query-column-count").textContent = plural(query.columns.length, "column");
  const copy = text.querySelector(".query-copy");
  copy.addEventListener("click", async () => {
    try {
      await navigator.clipboard.writeText(query.sql + "\n");
      copy.textContent = "Copied";
    } catch {
      copy.textContent = "Copy failed";
    }
    setTimeout(() => (copy.textContent = "Copy the T-SQL"), 1200);
  });

  renderCode(article.querySelector(".query-benchmark pre > code"), query.benchmarkSql, ContentType.SqlQuery);

  const select = article.querySelector(".query-target");
  for (const target of TARGETS) {
    const option = document.createElement("option");
    option.value = String(target);
    option.textContent = ORM_LABELS[target];
    select.append(option);
  }
  article.querySelector(".query-translate").addEventListener("click", () => translate(catalog, query, article));

  return article;
}

function renderSummary(catalog) {
  for (const count of document.querySelectorAll(".fiben-count")) {
    const translation = Number(count.dataset.translation);
    count.textContent = String(catalog.queries.filter((query) => query.translation === translation).length);
  }

  for (const count of document.querySelectorAll(".fiben-nesting-count")) {
    const nesting = Number(count.dataset.nesting);
    count.textContent = String(catalog.queries.filter((query) => query.nesting === nesting).length);
  }

  for (const count of document.querySelectorAll(".fiben-question-count")) {
    count.textContent = String(catalog.queries.reduce((sum, query) => sum + query.questions.length, 0));
  }

  for (const count of document.querySelectorAll(".fiben-table-count")) {
    count.textContent = String(new Set(catalog.queries.flatMap((query) => query.tables)).size);
  }
}

const badge = (label, className, reason) => {
  const span = document.createElement("span");
  span.className = className ? `badge ${className}` : "ldbc-language";
  span.textContent = label;
  if (reason) {
    span.title = reason;
  }
  return span;
};

const cell = (tag, ...children) => {
  const element = document.createElement(tag);
  element.append(...children);
  return element;
};

/*
 * The overview: a row per query and a column per target. The status column comes from the
 * catalog; a target column is empty until the reader fills it by translating every query into
 * that target through the running instance - the first level of the run, watched live.
 */
function renderMatrix(catalog) {
  const table = document.querySelector(".fiben-matrix");
  const head = table.querySelector("thead tr");
  const columns = new Map();

  for (const target of TARGETS) {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "secondary outline";
    button.textContent = `Translate all into ${ORM_LABELS[target]}`;
    const th = cell("th", ORM_LABELS[target], document.createElement("br"), button);
    th.scope = "col";
    head.append(th);
    columns.set(target, { button, cells: new Map(), footer: null });
  }

  const body = table.querySelector("tbody");
  for (const nesting of NESTING_ORDER) {
    const queries = catalog.queries.filter((query) => query.nesting === nesting);
    if (queries.length === 0) continue;

    const group = cell("th", NESTING_TITLES[nesting]);
    group.colSpan = 2 + TARGETS.length;
    group.scope = "colgroup";
    const groupRow = cell("tr", group);
    groupRow.className = "ldbc-matrix-group";
    body.append(groupRow);

    for (const query of queries) {
      const link = document.createElement("a");
      link.href = `#${query.key}`;
      link.textContent = `Q ${query.number}`;
      const title = cell("small", query.questions[0]);
      const name = cell("th", link, " ", title);
      name.scope = "row";

      const status = cell("td", badge(
        query.translation === FibenTranslation.AsSpecified ? "as stated" : (FIBEN_TRANSLATION_LABELS[query.translation] ?? "").toLowerCase(),
        query.translation === FibenTranslation.AsSpecified ? null : TRANSLATION_CLASSES[query.translation] ?? "badge-incompleteness",
        query.note || null,
      ));

      const row = cell("tr", name, status);
      for (const target of TARGETS) {
        const target_cell = cell("td", badge("—", null, "not translated yet; use the button in the heading"));
        columns.get(target).cells.set(query.key, target_cell);
        row.append(target_cell);
      }
      body.append(row);
    }
  }

  const totals = cell("th", `${catalog.queries.length} queries`);
  totals.scope = "row";
  const translated = (translation) => catalog.queries.filter((query) => query.translation === translation).length;
  const summary = cell(
    "td",
    ...Object.values(FibenTranslation)
      .filter((translation) => translated(translation) > 0)
      .map((translation) => cell("div", `${translated(translation)} ${translation === FibenTranslation.AsSpecified ? "as stated" : (FIBEN_TRANSLATION_LABELS[translation] ?? "").toLowerCase()}`)),
  );
  const footer = cell("tr", totals, summary);
  for (const target of TARGETS) {
    const footerCell = cell("td", "—");
    columns.get(target).footer = footerCell;
    footer.append(footerCell);
  }
  table.querySelector("tfoot").append(footer);

  for (const [target, column] of columns) {
    column.button.addEventListener("click", () => fillColumn(catalog, target, column));
  }
}

/** Translates every query into one target, one after another, and writes each outcome into its cell. */
async function fillColumn(catalog, target, column) {
  column.button.disabled = true;
  const counts = new Map();
  let done = 0;
  const started = performance.now();

  for (const query of catalog.queries) {
    const target_cell = column.cells.get(query.key);
    let outcome;
    try {
      const { refused, fellBack, reason } = await translateQuery(catalog, query, target);
      outcome = refused
        ? { label: "refused", className: "badge-failure", reason }
        : fellBack
          ? { label: "native SQL", className: "badge-fallback", reason }
          : { label: TARGET_LANGUAGES[target], className: null, reason: null };
    } catch (error) {
      outcome = { label: "error", className: "badge-failure", reason: error.message };
    }

    target_cell.replaceChildren(badge(outcome.label, outcome.className, outcome.reason));
    counts.set(outcome.label, (counts.get(outcome.label) ?? 0) + 1);
    done++;
    column.button.textContent = `${done} of ${catalog.queries.length}…`;
  }

  const seconds = ((performance.now() - started) / 1000).toFixed(0);
  column.footer.replaceChildren(...[...counts].map(([label, count]) => cell("div", `${count} ${label}`)), cell("small", `${seconds} s`));
  column.button.textContent = `Translate all into ${ORM_LABELS[target]} again`;
  column.button.disabled = false;
}

async function init() {
  const status = document.querySelector(".fiben-load-status");

  let catalog;
  try {
    catalog = await getFiben();
  } catch (error) {
    status.hidden = true;
    const errorElement = document.querySelector(".fiben-error");
    errorElement.textContent = `Could not load the FIBEN catalog: ${error.message}`;
    errorElement.hidden = false;
    return;
  }

  renderSummary(catalog);
  renderMatrix(catalog);

  renderArtifacts(document.querySelector(".fiben-entities"), catalog.entities, { idPrefix: "fiben-entity" });
  document.querySelector(".fiben-entity-count").textContent = plural(catalog.entities.length, "file");
  document.querySelector(".fiben-data-set").textContent = catalog.dataSet;

  for (const container of document.querySelectorAll(".fiben-queries")) {
    const nesting = Number(container.dataset.nesting);
    for (const query of catalog.queries.filter((candidate) => candidate.nesting === nesting)) {
      container.append(renderQuery(catalog, query));
    }
  }

  status.hidden = true;

  if (location.hash) {
    document.getElementById(location.hash.slice(1))?.scrollIntoView();
  }
}

init();
