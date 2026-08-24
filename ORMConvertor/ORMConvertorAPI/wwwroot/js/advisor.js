/*
 * The advisor screen: same scope as before the rewrite (decision 033) - source
 * framework, entity units, weighted queries, constraints, then the ILP result with
 * measurements and the artifacts of the selected frameworks.
 *
 * The result table shows every candidate's measurement per query with the assigned one
 * marked. The server already sends them all (`measurements` is query -> framework ->
 * measurement), and the comparison is what the choice is made of; the winner's number
 * alone says what was chosen but never why.
 *
 * The artifacts come from the run response itself (`translations`, decision 059): they
 * are the very sources the run compiled and measured, so the panels and the numbers
 * talk about the same code and no extra /convert is issued after the run.
 */

import {
  ORM,
  ORM_LABELS,
  ADVISOR_FRAMEWORKS,
  ContentType,
  CONTENT_TYPE_LABELS,
  getRequiredContentAdvisor,
  getAdvisorSamples,
  runAdvisor,
} from "./api.js";
import { cloneTemplate, renderArtifacts } from "./ui.js";

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

/*
 * Only the frameworks the advisor can actually measure are offerable. Checking one it
 * cannot ends the run with "no supported target frameworks resolved" after the request
 * has already gone out, which is the loader's sentence, not ours.
 */
function renderTargetCheckboxes() {
  const fieldset = document.getElementById("target-frameworks");
  for (const value of Object.values(ORM)) {
    const supported = ADVISOR_FRAMEWORKS.includes(value);
    const label = document.createElement("label");
    const checkbox = document.createElement("input");
    checkbox.type = "checkbox";
    checkbox.value = String(value);
    checkbox.disabled = !supported;
    label.append(
      checkbox,
      supported ? ` ${ORM_LABELS[value]}` : ` ${ORM_LABELS[value]} (the advisor does not measure it)`,
    );
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

/*
 * Dictionary keys of an enum type can arrive as a number, as a numeric string or as the
 * member name depending on how the server serializes them; asking for all three keeps the
 * lookup independent of that.
 */
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

/** One framework's translations from the run response, tolerant of the key shape. */
function translationFor(translations, framework) {
  if (!translations) return null;
  return (
    translations[framework] ??
    translations[String(framework)] ??
    translations[ORM_NAMES[framework]] ??
    null
  );
}

/** The measured artifacts of one framework: entities once, then each query's in order. */
function artifactsFor(translations, framework, queryIds) {
  const translation = translationFor(translations, framework);
  if (!translation) return [];
  const queryArtifacts = queryIds.flatMap((id) => translation.queries?.[id] ?? []);
  return [...(translation.entities ?? []), ...queryArtifacts];
}

/** Every candidate measured for one query, fastest first. */
function candidatesFor(measurements, queryId) {
  return Object.values(ORM)
    .map((framework) => ({ framework, measurement: measurementFor(measurements, queryId, framework) }))
    .filter((candidate) => candidate.measurement)
    .sort(
      (a, b) =>
        a.measurement.meanDurationMilliseconds - b.measurement.meanDurationMilliseconds,
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

  for (const [queryId, assigned] of assignments) {
    const candidates = candidatesFor(result.measurements, queryId);
    const rows = candidates.length > 0 ? candidates : [{ framework: assigned, measurement: null }];
    const best = rows[0]?.measurement?.meanDurationMilliseconds;

    rows.forEach((candidate, index) => {
      const row = document.createElement("tr");
      const isAssigned = candidate.framework === assigned;
      if (isAssigned) row.className = "assigned";

      if (index === 0) {
        const queryCell = document.createElement("th");
        queryCell.scope = "row";
        queryCell.rowSpan = rows.length;
        queryCell.textContent = labels.get(queryId) ?? `Query ${queryId}`;
        row.append(queryCell);
      }

      const time = candidate.measurement
        ? candidate.measurement.meanDurationMilliseconds
        : null;
      const memory = candidate.measurement
        ? Math.round(candidate.measurement.allocatedBytes / 1024)
        : null;
      if (isAssigned) {
        totalTime += time ?? 0;
        totalMemory += memory ?? 0;
      }

      const relative =
        time != null && best ? (time / best).toFixed(2) + "×" : time != null ? "1.00×" : "—";

      for (const value of [
        ORM_LABELS[candidate.framework] ?? String(candidate.framework),
        time != null ? time.toFixed(1) : "—",
        memory != null ? String(memory) : "—",
        relative,
        isAssigned ? "assigned" : "",
      ]) {
        const cell = document.createElement("td");
        cell.textContent = value;
        row.append(cell);
      }
      body.append(row);
    });
  }
  document.getElementById("total-time").textContent = totalTime.toFixed(1);
  document.getElementById("total-memory").textContent = String(totalMemory);

  const container = document.getElementById("conversions");
  container.replaceChildren();
  conversions.forEach((conversion, index) => {
    const details = cloneTemplate("conversion-template");
    details.querySelector("summary").textContent =
      `Artifacts for ${ORM_LABELS[conversion.framework] ?? conversion.framework}`;
    renderArtifacts(details.querySelector(".conversion-artifacts"), conversion.sources, {
      idPrefix: `conversion-${index + 1}-artifact`,
    });
    container.append(details);
  });
}

/* ---- run status: the one operation here that legitimately takes minutes -- */

let elapsedTimer = null;

function startElapsed() {
  const started = performance.now();
  const elapsed = document.getElementById("run-elapsed");
  document.getElementById("run-status").textContent =
    "Running: translating, compiling, benchmarking and solving. This takes minutes.";
  const tick = () => {
    const seconds = Math.floor((performance.now() - started) / 1000);
    elapsed.textContent =
      `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, "0")} elapsed`;
  };
  tick();
  elapsedTimer = setInterval(tick, 1000);
  return started;
}

function stopElapsed(started, outcome) {
  if (elapsedTimer !== null) clearInterval(elapsedTimer);
  elapsedTimer = null;
  const seconds = Math.round((performance.now() - started) / 1000);
  document.getElementById("run-elapsed").textContent = "";
  document.getElementById("run-status").textContent = `${outcome} after ${seconds} s.`;
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
  const started = startElapsed();

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
    const queryIds = queries.map((q) => q.id);
    const conversions = selected.map((framework) => ({
      framework,
      sources: artifactsFor(state.result.translations, framework, queryIds),
    }));
    renderResult(labels, conversions);
    stopElapsed(started, "Finished");
    document.getElementById("result").scrollIntoView({ behavior: "smooth", block: "start" });
  } catch (error) {
    stopElapsed(started, "Failed");
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
