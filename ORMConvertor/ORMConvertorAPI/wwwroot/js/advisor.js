/*
 * The advisor screen: same scope as before the rewrite (decision 033) - source
 * framework, entity units, weighted queries, constraints, then the ILP result with
 * measurements and the artifacts converted for the selected frameworks.
 */

import {
  ORM,
  ORM_LABELS,
  ContentType,
  CONTENT_TYPE_LABELS,
  getRequiredContentAdvisor,
  getAdvisorSamples,
  runAdvisor,
  convert,
} from "./api.js";
import { cloneTemplate, renderCode, artifactNames } from "./ui.js";

// Enum member names, used when dictionary keys serialize as names rather than numbers.
const ORM_NAMES = Object.freeze({
  [ORM.Dapper]: "Dapper",
  [ORM.NHibernate]: "NHibernate",
  [ORM.EFCore]: "EFCore",
});

const state = {
  sourceOrm: ORM.EFCore,
  requiredContent: [],
  samples: {},
  entityUnits: [],
  queryUnits: [],
  result: null,
};

let queryCounter = 0;

function requiredUnits() {
  const definition = state.requiredContent.find((r) => r.ormType === state.sourceOrm);
  return definition?.required ?? [];
}

function resetUnitsForSource() {
  state.entityUnits = requiredUnits()
    .filter((unit) => unit.contentType !== ContentType.CSharpQuery)
    .map((unit) => ({
      description: unit.description,
      contentType: unit.contentType,
      sampleId: unit.id,
      content: "",
    }));

  const queryTemplates = requiredUnits().filter(
    (unit) => unit.contentType === ContentType.CSharpQuery,
  );
  queryCounter = 0;
  state.queryUnits =
    queryTemplates.length > 0
      ? queryTemplates.map((template) => newQueryUnit(template))
      : [newQueryUnit()];
}

function newQueryUnit(template) {
  return {
    id: ++queryCounter,
    description: template?.description ?? "Query",
    contentType: template?.contentType ?? ContentType.CSharpQuery,
    sampleId: template?.id,
    content: "",
    weight: 1,
  };
}

/* ---- rendering -------------------------------------------------------- */

function renderSourceSelect() {
  const select = document.getElementById("source-orm");
  select.replaceChildren();
  for (const value of Object.values(ORM)) {
    const option = document.createElement("option");
    option.value = String(value);
    option.textContent = ORM_LABELS[value];
    option.selected = value === state.sourceOrm;
    select.append(option);
  }
}

function renderTargetCheckboxes() {
  const fieldset = document.getElementById("target-frameworks");
  for (const value of Object.values(ORM)) {
    const label = document.createElement("label");
    const checkbox = document.createElement("input");
    checkbox.type = "checkbox";
    checkbox.value = String(value);
    label.append(checkbox, ` ${ORM_LABELS[value]}`);
    fieldset.append(label);
  }
}

function renderEntityUnits() {
  const container = document.getElementById("entity-units");
  container.replaceChildren();
  for (const unit of state.entityUnits) {
    const article = cloneTemplate("entity-unit-template");
    article.querySelector(".unit-label").textContent =
      `${unit.description} (${CONTENT_TYPE_LABELS[unit.contentType]})`;
    const textarea = article.querySelector(".unit-content");
    textarea.value = unit.content;
    textarea.addEventListener("input", () => {
      unit.content = textarea.value;
    });
    container.append(article);
  }
}

function renderQueryUnits() {
  const container = document.getElementById("query-units");
  container.replaceChildren();
  state.queryUnits.forEach((unit, index) => {
    const article = cloneTemplate("query-unit-template");
    article.querySelector(".unit-label").textContent =
      `${unit.description} ${index + 1} (${CONTENT_TYPE_LABELS[unit.contentType]})`;

    const weight = article.querySelector(".unit-weight");
    weight.value = String(unit.weight);
    weight.addEventListener("input", () => {
      unit.weight = Math.max(1, Math.trunc(Number(weight.value) || 1));
    });

    const removeButton = article.querySelector(".unit-remove");
    removeButton.disabled = state.queryUnits.length <= 1;
    removeButton.addEventListener("click", () => {
      state.queryUnits = state.queryUnits.filter((q) => q.id !== unit.id);
      renderQueryUnits();
    });

    const textarea = article.querySelector(".unit-content");
    textarea.value = unit.content;
    textarea.addEventListener("input", () => {
      unit.content = textarea.value;
    });

    container.append(article);
  });
}

function measurementFor(measurements, queryId, framework) {
  const perFramework = measurements?.[queryId];
  if (!perFramework) return null;
  return (
    perFramework[framework] ??
    perFramework[String(framework)] ??
    perFramework[ORM_NAMES[framework]] ??
    null
  );
}

function renderResult(labels, conversions) {
  const section = document.getElementById("result");
  const result = state.result;
  if (!result) {
    section.hidden = true;
    return;
  }
  section.hidden = false;

  const selected = [...new Set(result.selectedFrameworks ?? [])];
  document.getElementById("selected-frameworks").textContent =
    selected.length > 0
      ? `Selected framework${selected.length > 1 ? "s" : ""}: ` +
        selected.map((f) => ORM_LABELS[f] ?? f).join(", ")
      : "No framework was selected.";

  const body = document.getElementById("assignments");
  body.replaceChildren();
  let totalTime = 0;
  let totalMemory = 0;
  const assignments = Object.entries(result.queryAssignments ?? {}).sort(
    (a, b) => Number(a[0]) - Number(b[0]),
  );
  for (const [queryId, framework] of assignments) {
    const row = document.createElement("tr");
    const measurement = measurementFor(result.measurements, queryId, framework);
    const time = measurement ? Math.round(measurement.meanDurationMilliseconds) : null;
    const memory = measurement ? Math.round(measurement.allocatedBytes / 1024) : null;
    totalTime += time ?? 0;
    totalMemory += memory ?? 0;
    for (const value of [
      labels.get(queryId) ?? `Query ${queryId}`,
      ORM_LABELS[framework] ?? String(framework),
      time != null ? String(time) : "—",
      memory != null ? String(memory) : "—",
    ]) {
      const cell = document.createElement("td");
      cell.textContent = value;
      row.append(cell);
    }
    body.append(row);
  }
  document.getElementById("total-time").textContent = String(totalTime);
  document.getElementById("total-memory").textContent = String(totalMemory);

  const container = document.getElementById("conversions");
  container.replaceChildren();
  for (const conversion of conversions) {
    const details = cloneTemplate("conversion-template");
    details.querySelector("summary").textContent =
      `Artifacts for ${ORM_LABELS[conversion.framework] ?? conversion.framework}`;
    const grid = details.querySelector(".conversion-artifacts");
    const names = artifactNames(conversion.sources);
    conversion.sources.forEach((artifact, index) => {
      const article = cloneTemplate("artifact-template");
      article.querySelector(".artifact-name").textContent = names[index];
      article.querySelector(".artifact-type").textContent =
        CONTENT_TYPE_LABELS[artifact.contentType] ?? "";
      renderCode(article.querySelector("pre > code"), artifact.content, artifact.contentType);
      grid.append(article);
    });
    container.append(details);
  }
}

/* ---- actions ----------------------------------------------------------- */

async function onRun() {
  const errorElement = document.getElementById("run-error");
  errorElement.hidden = true;

  const entities = state.entityUnits.map((unit) => ({
    contentType: unit.contentType,
    content: unit.content,
  }));
  const queries = state.queryUnits.map((unit, index) => ({
    id: String(index + 1),
    query: { contentType: unit.contentType, content: unit.content },
    weight: unit.weight,
  }));
  const labels = new Map(
    state.queryUnits.map((unit, index) => [String(index + 1), `${unit.description} ${index + 1}`]),
  );
  const combinedSources = [...entities, ...queries.map((q) => q.query)];

  const memoryKb = Math.max(0, Math.trunc(Number(document.getElementById("memory-limit").value) || 0));
  const maxFrameworks = Math.max(1, Math.trunc(Number(document.getElementById("max-frameworks").value) || 1));
  const targets = [...document.querySelectorAll("#target-frameworks input:checked")].map((c) =>
    Number(c.value),
  );

  const button = document.getElementById("run");
  button.disabled = true;
  button.setAttribute("aria-busy", "true");
  state.result = null;
  renderResult(labels, []);

  try {
    state.result = await runAdvisor({
      sourceOrm: state.sourceOrm,
      entities,
      queries,
      maxMemoryBytes: memoryKb * 1024,
      maxFrameworksToSelect: maxFrameworks,
      targetFrameworks: targets.length > 0 ? targets : undefined,
    });

    const selected = [...new Set(state.result.selectedFrameworks ?? [])];
    const conversions = await Promise.all(
      selected.map(async (framework) => ({
        framework,
        sources: (await convert(state.sourceOrm, framework, combinedSources)).sources,
      })),
    );
    renderResult(labels, conversions);
  } catch (error) {
    errorElement.textContent = error.message;
    errorElement.hidden = false;
  } finally {
    button.disabled = false;
    button.removeAttribute("aria-busy");
  }
}

function onLoadSamples() {
  for (const unit of [...state.entityUnits, ...state.queryUnits]) {
    if (unit.sampleId !== undefined && state.samples[unit.sampleId] !== undefined) {
      unit.content = state.samples[unit.sampleId];
    }
  }
  renderEntityUnits();
  renderQueryUnits();
}

/* ---- init --------------------------------------------------------------- */

async function init() {
  renderSourceSelect();
  renderTargetCheckboxes();

  document.getElementById("source-orm").addEventListener("change", (event) => {
    state.sourceOrm = Number(event.target.value);
    resetUnitsForSource();
    renderEntityUnits();
    renderQueryUnits();
  });
  document.getElementById("add-query").addEventListener("click", () => {
    state.queryUnits.push(newQueryUnit());
    renderQueryUnits();
  });
  document.getElementById("load-samples").addEventListener("click", onLoadSamples);
  document.getElementById("run").addEventListener("click", onRun);

  try {
    [state.requiredContent, state.samples] = await Promise.all([
      getRequiredContentAdvisor(),
      getAdvisorSamples(),
    ]);
  } catch (error) {
    const errorElement = document.getElementById("run-error");
    errorElement.textContent = `Could not load the input catalog: ${error.message}`;
    errorElement.hidden = false;
  }
  resetUnitsForSource();
  renderEntityUnits();
  renderQueryUnits();
}

init();
