/*
 * The LDBC page (decision 110): the 41 read queries of the LDBC Social Network Benchmark,
 * each with how much of it the tool translates. The catalog is the server's (GET /ldbc) and
 * its claims are held by the suite (LdbcCatalogTest); the page shows them and lets the reader
 * run any query with a text through /convert - the entity units plus the query, from Dapper
 * into a chosen framework - so a claim can be watched, not only read.
 */

import {
  ORM,
  ORM_LABELS,
  ContentType,
  LdbcTranslation,
  LDBC_TRANSLATION_LABELS,
  getLdbc,
  convert,
} from "./api.js";
import { cloneTemplate, renderCode, renderArtifacts, renderRecords, renderCatalogState } from "./ui.js";

const QUERY_CONTENT_TYPES = new Set([
  ContentType.CSharpQuery,
  ContentType.SqlQuery,
  ContentType.HqlQuery,
  ContentType.JavaQuery,
  ContentType.JpqlQuery,
]);

// MyBatis keeps a statement in a mapper document beside the interface, and every entity gets a
// mapper document of its own with a result map; the one with the query is the one with a select.
const isQueryArtifact = (artifact) =>
  QUERY_CONTENT_TYPES.has(artifact.contentType) ||
  (artifact.contentType === ContentType.Xml && artifact.content.includes("<select"));

const TRANSLATION_CLASSES = Object.freeze({
  [LdbcTranslation.AsSpecified]: "badge-ldbc-as-specified",
  [LdbcTranslation.Simplified]: "badge-ldbc-simplified",
  [LdbcTranslation.NotTranslated]: "badge-ldbc-not-translated",
});

const WORKLOAD_PREFIXES = Object.freeze({ 10: "IS", 20: "IC", 30: "BI" });

const TARGETS = [ORM.EFCore, ORM.NHibernate, ORM.Hibernate, ORM.EclipseLink, ORM.MyBatis];

const plural = (count, word) => `${count} ${word}${count === 1 ? "" : "s"}`;

/*
 * The query as a script for sqlcmd or Management Studio: the parameters as declared
 * variables with their example values, then the text. A list parameter is Dapper's
 * (decision 106) and T-SQL has none, so its values are written into the IN list instead.
 */
function asScript(query) {
  const quote = (value) => `'${value.replaceAll("'", "''")}'`;
  const literal = (parameter) => {
    const type = parameter.sqlType.split("(")[0].toUpperCase();
    if (type === "BIGINT" || type === "INT") return parameter.example;
    return type === "NVARCHAR" ? `N${quote(parameter.example)}` : quote(parameter.example);
  };

  let text = query.sql;
  const declarations = [];
  for (const parameter of query.parameters) {
    if (parameter.isList) {
      const values = parameter.example.split(",").map(quote).join(", ");
      text = text.replaceAll(new RegExp(`IN\\s+@${parameter.name}\\b`, "g"), `IN (${values})`);
    } else {
      declarations.push(`DECLARE @${parameter.name} ${parameter.sqlType} = ${literal(parameter)};`);
    }
  }

  return [...declarations, "", text, ""].join("\n");
}

function renderParameters(article, query) {
  const body = article.querySelector(".query-parameters tbody");
  for (const parameter of query.parameters) {
    const row = document.createElement("tr");
    for (const value of [
      `@${parameter.name}`,
      parameter.isList ? `list of ${parameter.sqlType}` : parameter.sqlType,
      parameter.example,
    ]) {
      const cell = document.createElement("td");
      const code = document.createElement("code");
      code.textContent = value;
      cell.append(code);
      row.append(cell);
    }
    body.append(row);
  }
  article.querySelector(".query-parameter-count").textContent = plural(query.parameters.length, "parameter");
}

async function translate(catalog, query, article) {
  const target = Number(article.querySelector(".query-target").value);
  const status = article.querySelector(".query-status");
  const errorElement = article.querySelector(".query-error");
  const result = article.querySelector(".query-result");
  const unitName = `${query.key}.sql`;

  errorElement.hidden = true;
  status.hidden = false;
  status.setAttribute("aria-busy", "true");
  status.textContent = "Translating…";

  try {
    const response = await convert(catalog.sourceOrm, target, [
      ...catalog.entities,
      { name: unitName, contentType: ContentType.SqlQuery, content: query.sql },
    ]);

    const outputs = response.sources.filter(isQueryArtifact);
    const artifactIndex = renderArtifacts(article.querySelector(".query-outputs"), outputs, {
      idPrefix: `${query.key}-output`,
    });

    // The entities are in every run and their records would bury the query's: shown are the
    // records the reading of the query unit or the building of the query wrote.
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

  article.querySelector(".query-title").textContent =
    `${WORKLOAD_PREFIXES[query.workload] ?? ""} ${query.number} — ${query.title}`;
  const badge = article.querySelector(".query-translation");
  badge.textContent = LDBC_TRANSLATION_LABELS[query.translation] ?? "";
  badge.classList.add(TRANSLATION_CLASSES[query.translation] ?? "badge-incompleteness");
  article.querySelector(".query-note").textContent = query.note;

  if (query.refusedBy.length > 0) {
    const refusals = article.querySelector(".query-refusals");
    for (const refusal of query.refusedBy) {
      const strong = document.createElement("strong");
      strong.textContent = `Refused by ${ORM_LABELS[refusal.target]}: `;
      refusals.append(strong, refusal.reason, " ");
    }
    refusals.hidden = false;
  }

  if (query.sql) {
    const text = article.querySelector(".query-text");
    renderParameters(article, query);
    renderCode(text.querySelector("pre > code"), query.sql, ContentType.SqlQuery);
    const copy = text.querySelector(".query-copy");
    copy.addEventListener("click", async () => {
      try {
        await navigator.clipboard.writeText(asScript(query));
        copy.textContent = "Copied";
      } catch {
        copy.textContent = "Copy failed";
      }
      setTimeout(() => (copy.textContent = "Copy as a T-SQL script"), 1200);
    });
    text.hidden = false;

    // A query the tool does not translate can still be sent: the refusal and its record
    // are the point of showing it.
    const select = article.querySelector(".query-target");
    for (const target of TARGETS) {
      const option = document.createElement("option");
      option.value = String(target);
      option.textContent = ORM_LABELS[target];
      select.append(option);
    }
    article
      .querySelector(".query-translate")
      .addEventListener("click", () => translate(catalog, query, article));
    article.querySelector(".query-run").hidden = false;
  }

  return article;
}

function renderSummary(catalog) {
  for (const count of document.querySelectorAll(".ldbc-count")) {
    const translation = Number(count.dataset.translation);
    count.textContent = String(catalog.queries.filter((query) => query.translation === translation).length);
  }

  const refusedBy = (target) =>
    catalog.queries.filter((query) => query.refusedBy.some((refusal) => refusal.target === target)).length;

  for (const count of document.querySelectorAll(".ldbc-refusal-count")) {
    count.textContent = String(refusedBy(ORM.EFCore));
  }

  // NHibernate and EclipseLink refuse the same queries, the ones over the result of another
  // query (decision 112); the page counts one of them.
  for (const count of document.querySelectorAll(".ldbc-intermediate-refusal-count")) {
    count.textContent = String(refusedBy(ORM.NHibernate));
  }
}

async function init() {
  const status = document.querySelector(".ldbc-load-status");

  let catalog;
  try {
    catalog = await getLdbc();
  } catch (error) {
    status.hidden = true;
    const errorElement = document.querySelector(".ldbc-error");
    errorElement.textContent = `Could not load the LDBC catalog: ${error.message}`;
    errorElement.hidden = false;
    return;
  }

  renderSummary(catalog);

  renderArtifacts(document.querySelector(".ldbc-entities"), catalog.entities, { idPrefix: "ldbc-entity" });
  document.querySelector(".ldbc-entity-count").textContent = plural(catalog.entities.length, "file");

  for (const container of document.querySelectorAll(".ldbc-queries")) {
    const workload = Number(container.dataset.workload);
    for (const query of catalog.queries.filter((candidate) => candidate.workload === workload)) {
      container.append(renderQuery(catalog, query));
    }
  }

  status.hidden = true;

  // A link to one query (ldbc.html#bi7) lands on it once it exists.
  if (location.hash) {
    document.getElementById(location.hash.slice(1))?.scrollIntoView();
  }
}

init();
