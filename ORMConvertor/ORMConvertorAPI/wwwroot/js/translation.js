/*
 * The translation screen (decision 033): five steps on one page - source framework,
 * target framework, input files, convert, result. Screen state is one plain object;
 * rendering functions rebuild whole areas from <template> clones (decision 032).
 *
 * The state is also mirrored into localStorage after every change and restored on load
 * (decision 056), so a reload, a stray back-navigation or a crashed tab does not take a
 * session's worth of pasted units with it.
 */

import {
  ORM,
  ORM_LABELS,
  ContentType,
  CONTENT_TYPE_LABELS,
  convert,
  getRequiredContent,
  getSamples,
  archive,
} from "./api.js";
import {
  cloneTemplate,
  renderArtifacts,
  renderRecords,
  renderCatalogState,
  recordCounts,
  kindBadge,
  artifactNames,
  saveBlob,
} from "./ui.js";

const EXTENSION_TYPES = {
  ".cs": ContentType.CSharpEntity,
  ".xml": ContentType.Xml,
  ".sql": ContentType.SqlQuery,
  ".hql": ContentType.HqlQuery,
};

const STORAGE_KEY = "ormconvertor.translation";

const state = {
  sourceOrm: ORM.EFCore,
  targetOrm: ORM.NHibernate,
  units: [],
  requiredContent: [],
  samples: {},
  result: null,
};

let unitCounter = 0;

function newUnit(name, contentType, content) {
  return { id: ++unitCounter, name, contentType, content };
}

function offeredTypes(sourceOrm) {
  const definition = state.requiredContent.find((r) => r.ormType === sourceOrm);
  if (!definition) return Object.values(ContentType);
  return [...new Set(definition.required.map((u) => u.contentType))];
}

function typeForExtension(fileName) {
  const dot = fileName.lastIndexOf(".");
  const extension = dot >= 0 ? fileName.slice(dot).toLowerCase() : "";
  return EXTENSION_TYPES[extension] ?? ContentType.CSharpEntity;
}

/* ---- remembering the input (decision 056) ------------------------------- */

/*
 * Storage is a convenience, never a gate: a private window, blocked site data or an
 * exceeded quota all throw, and none of them is an error of the tool. The first failure
 * says so once and the screen carries on without remembering anything.
 */
let storageWorks = true;

function noteStorageFailure() {
  if (!storageWorks) return;
  storageWorks = false;
  const note = document.getElementById("storage-note");
  if (note) note.hidden = false;
}

function saveState() {
  if (!storageWorks) return;
  try {
    localStorage.setItem(
      STORAGE_KEY,
      JSON.stringify({
        sourceOrm: state.sourceOrm,
        targetOrm: state.targetOrm,
        units: state.units.map(({ name, contentType, content }) => ({
          name,
          contentType,
          content,
        })),
      }),
    );
  } catch {
    noteStorageFailure();
  }
}

/*
 * Reading is defensive on purpose (decision 056): the stored object is a detail of this
 * screen, not a contract, so anything that does not fit is dropped and the screen starts
 * empty rather than half-restored.
 */
function restoreState() {
  let stored;
  try {
    stored = JSON.parse(localStorage.getItem(STORAGE_KEY) ?? "null");
  } catch {
    noteStorageFailure();
    return;
  }
  if (!stored || typeof stored !== "object") return;

  const frameworks = Object.values(ORM);
  if (frameworks.includes(stored.sourceOrm)) state.sourceOrm = stored.sourceOrm;
  if (frameworks.includes(stored.targetOrm)) state.targetOrm = stored.targetOrm;

  if (!Array.isArray(stored.units)) return;
  const types = Object.values(ContentType);
  state.units = stored.units
    .filter((unit) => unit && typeof unit.content === "string" && types.includes(unit.contentType))
    .map((unit) => newUnit(String(unit.name ?? ""), unit.contentType, unit.content));
}

function forgetState() {
  try {
    localStorage.removeItem(STORAGE_KEY);
  } catch {
    noteStorageFailure();
  }
}

/* ---- rendering -------------------------------------------------------- */

function renderFrameworkSelects() {
  for (const [element, selected] of [
    [document.getElementById("source-orm"), state.sourceOrm],
    [document.getElementById("target-orm"), state.targetOrm],
  ]) {
    element.replaceChildren();
    for (const value of Object.values(ORM)) {
      const option = document.createElement("option");
      option.value = String(value);
      option.textContent = ORM_LABELS[value];
      option.selected = value === selected;
      element.append(option);
    }
  }
}

function renderOfferNote() {
  const offered = offeredTypes(state.sourceOrm);
  document.getElementById("offer-note").textContent =
    `${ORM_LABELS[state.sourceOrm]} input is read as: ` +
    offered.map((t) => CONTENT_TYPE_LABELS[t]).join(", ") +
    ".";
}

function renderUnits() {
  const container = document.getElementById("units");
  container.replaceChildren();
  const offered = offeredTypes(state.sourceOrm);

  for (const unit of state.units) {
    const article = cloneTemplate("unit-template");
    article.dataset.unitId = String(unit.id);

    const nameInput = article.querySelector(".unit-name");
    nameInput.value = unit.name;
    nameInput.addEventListener("input", () => {
      unit.name = nameInput.value;
      saveState();
    });

    const typeSelect = article.querySelector(".unit-type");
    const options = offered.includes(unit.contentType)
      ? offered
      : [...offered, unit.contentType];
    for (const type of options) {
      const option = document.createElement("option");
      option.value = String(type);
      option.textContent = offered.includes(type)
        ? CONTENT_TYPE_LABELS[type]
        : `${CONTENT_TYPE_LABELS[type]} (not read by ${ORM_LABELS[state.sourceOrm]})`;
      option.selected = type === unit.contentType;
      typeSelect.append(option);
    }
    typeSelect.addEventListener("change", () => {
      unit.contentType = Number(typeSelect.value);
      saveState();
      renderUnits();
    });

    article.querySelector(".unit-remove").addEventListener("click", () => {
      state.units = state.units.filter((u) => u.id !== unit.id);
      saveState();
      renderUnits();
    });

    const textarea = article.querySelector(".unit-content");
    textarea.value = unit.content;
    textarea.addEventListener("input", () => {
      unit.content = textarea.value;
      saveState();
    });

    container.append(article);
  }

  if (state.units.length === 0) {
    const empty = document.createElement("p");
    empty.textContent = "No input files yet - add files, an empty unit, or the sample set.";
    container.append(empty);
  }

  document.getElementById("clear").disabled = state.units.length === 0;
}

/*
 * The summary strip under the Convert button. The result section is a screenful or two
 * further down with a batch of units above it, so pressing Convert has to say what came
 * back where the button is - and the badges jump to the band that carries the detail.
 */
function renderSummary(response) {
  const strip = document.getElementById("convert-summary");
  strip.replaceChildren();
  if (!response) {
    strip.hidden = true;
    return;
  }
  strip.hidden = false;

  const count = response.sources?.length ?? 0;
  const artifacts = document.createElement("span");
  artifacts.textContent = `${count} artifact${count === 1 ? "" : "s"}`;
  strip.append(artifacts);

  const records = response.records ?? [];
  if (records.length === 0) {
    const none = document.createElement("span");
    none.textContent = "no records";
    strip.append(none);
  } else {
    for (const [kind, kindCount] of recordCounts(records)) {
      const button = document.createElement("button");
      button.type = "button";
      button.className = "badge-button";
      button.title = "Show the records band";
      button.append(kindBadge(kind, `x ${kindCount}`));
      button.addEventListener("click", () =>
        document.getElementById("records").scrollIntoView({ behavior: "smooth", block: "center" }),
      );
      strip.append(button);
    }
  }

  const jump = document.createElement("button");
  jump.type = "button";
  jump.className = "link-button";
  jump.textContent = "go to the result";
  jump.addEventListener("click", () =>
    document.getElementById("result").scrollIntoView({ behavior: "smooth", block: "start" }),
  );
  strip.append(jump);
}

function renderResult() {
  const section = document.getElementById("result");
  const response = state.result;
  if (!response) {
    section.hidden = true;
    renderSummary(null);
    return;
  }
  section.hidden = false;

  document.getElementById("run-frameworks").textContent =
    `${ORM_LABELS[response.sourceFramework]} ${response.sourceFrameworkVersion}` +
    ` → ${ORM_LABELS[response.targetFramework]} ${response.targetFrameworkVersion}`;
  document.getElementById("tool-version").textContent = response.toolVersion;
  document.getElementById("run-id").textContent = response.runId;
  renderCatalogState(
    document.getElementById("catalog-state"),
    response.catalogState,
    response.catalogReadMilliseconds,
  );

  const artifactIndex = renderArtifacts(document.getElementById("artifacts"), response.sources);
  renderRecords(document.getElementById("records"), response.records, { artifactIndex });
  renderSummary(response);
}

/* ---- validation (S7): a helper, not the gate - the server stays authoritative */

function xmlProblem(content) {
  const parsed = new DOMParser().parseFromString(content, "text/xml");
  const error = parsed.querySelector("parsererror");
  if (!error) return null;
  const text = error.textContent ?? "";
  const location = text.match(/line[\s:]+(\d+)/i);
  return location ? `not well-formed XML (line ${location[1]})` : "not well-formed XML";
}

function validate() {
  const problems = [];
  const container = document.getElementById("units");
  for (const errorElement of container.querySelectorAll(".unit-error")) {
    errorElement.hidden = true;
    errorElement.textContent = "";
  }

  if (state.units.length === 0) {
    problems.push({ text: "Add at least one input file." });
    return problems;
  }

  const offered = offeredTypes(state.sourceOrm);
  for (const unit of state.units) {
    const label = unit.name || `unit ${unit.id}`;
    let unitProblem = null;
    if (unit.content.trim() === "") {
      unitProblem = "the content is empty";
    } else if (!offered.includes(unit.contentType)) {
      unitProblem =
        `${ORM_LABELS[state.sourceOrm]} does not read ` +
        `${CONTENT_TYPE_LABELS[unit.contentType]}`;
    } else if (unit.contentType === ContentType.Xml) {
      unitProblem = xmlProblem(unit.content);
    }
    if (unitProblem) {
      problems.push({ text: `${label}: ${unitProblem}.`, unitId: unit.id });
      const article = container.querySelector(`[data-unit-id="${unit.id}"]`);
      const errorElement = article?.querySelector(".unit-error");
      if (errorElement) {
        errorElement.textContent = unitProblem;
        errorElement.hidden = false;
      }
    }
  }
  return problems;
}

/** Every problem that names a unit becomes a button that goes there and focuses it. */
function showValidation(problems) {
  const box = document.getElementById("validation");
  const list = document.getElementById("validation-list");
  list.replaceChildren();
  for (const problem of problems) {
    const item = document.createElement("li");
    if (problem.unitId == null) {
      item.textContent = problem.text;
    } else {
      const button = document.createElement("button");
      button.type = "button";
      button.className = "link-button";
      button.textContent = problem.text;
      button.title = "Go to this unit";
      button.addEventListener("click", () => {
        const article = document
          .getElementById("units")
          .querySelector(`[data-unit-id="${problem.unitId}"]`);
        if (!article) return;
        article.scrollIntoView({ behavior: "smooth", block: "center" });
        article.querySelector(".unit-content")?.focus({ preventScroll: true });
      });
      item.append(button);
    }
    list.append(item);
  }
  box.hidden = problems.length === 0;
}

/* ---- actions ----------------------------------------------------------- */

async function onConvert() {
  const errorElement = document.getElementById("convert-error");
  const status = document.getElementById("convert-status");
  errorElement.hidden = true;

  const problems = validate();
  showValidation(problems);
  if (problems.length > 0) {
    status.textContent = "The input is not ready to send.";
    return;
  }

  const button = document.getElementById("convert");
  button.disabled = true;
  button.setAttribute("aria-busy", "true");
  status.textContent = "Converting…";
  state.result = null;
  renderResult();

  try {
    const sources = state.units.map((unit) => ({
      contentType: unit.contentType,
      content: unit.content,
    }));
    state.result = await convert(state.sourceOrm, state.targetOrm, sources);
    renderResult();

    const artifacts = state.result.sources?.length ?? 0;
    const records = state.result.records?.length ?? 0;
    status.textContent =
      `Converted: ${artifacts} artifact${artifacts === 1 ? "" : "s"}, ` +
      `${records} record${records === 1 ? "" : "s"}.`;
    document.getElementById("result").scrollIntoView({ behavior: "smooth", block: "start" });
  } catch (error) {
    status.textContent = "The conversion failed.";
    errorElement.textContent = error.message;
    errorElement.hidden = false;
  } finally {
    button.disabled = false;
    button.removeAttribute("aria-busy");
  }
}

async function onDownload() {
  const response = state.result;
  if (!response) return;
  const names = artifactNames(response.sources);
  const files = response.sources.map((artifact, index) => ({
    name: names[index],
    content: artifact.content,
  }));
  const button = document.getElementById("download");
  button.setAttribute("aria-busy", "true");
  try {
    saveBlob(await archive(files), "conversion.zip");
  } catch (error) {
    const errorElement = document.getElementById("convert-error");
    errorElement.textContent = error.message;
    errorElement.hidden = false;
  } finally {
    button.removeAttribute("aria-busy");
  }
}

function onAddFiles(fileList) {
  const readers = [...fileList].map(
    (file) =>
      new Promise((resolve) => {
        const reader = new FileReader();
        reader.onload = () =>
          resolve(newUnit(file.name, typeForExtension(file.name), String(reader.result)));
        reader.readAsText(file);
      }),
  );
  Promise.all(readers).then((units) => {
    state.units.push(...units);
    saveState();
    renderUnits();
  });
}

function onLoadSamples() {
  const definition = state.requiredContent.find((r) => r.ormType === state.sourceOrm);
  if (!definition) return;
  // Replacing the screen wholesale is what this button does; with work on it, it asks.
  if (
    state.units.length > 0 &&
    !confirm("Loading the sample set replaces every unit on the screen. Continue?")
  ) {
    return;
  }
  const units = definition.required
    .filter((required) => state.samples[required.id] !== undefined)
    .map((required) => ({
      contentType: required.contentType,
      content: state.samples[required.id],
    }));
  const names = artifactNames(units);
  state.units = units.map((unit, index) => newUnit(names[index], unit.contentType, unit.content));
  saveState();
  renderUnits();
}

/** Clears the screen and the remembered copy together (decision 056). */
function onClear() {
  if (state.units.length > 0 && !confirm("Remove every unit from the screen?")) return;
  state.units = [];
  state.result = null;
  forgetState();
  showValidation([]);
  document.getElementById("convert-status").textContent = "";
  document.getElementById("convert-error").hidden = true;
  renderUnits();
  renderResult();
}

/* ---- init --------------------------------------------------------------- */

async function init() {
  restoreState();
  renderFrameworkSelects();
  renderUnits();

  document.getElementById("source-orm").addEventListener("change", (event) => {
    state.sourceOrm = Number(event.target.value);
    saveState();
    renderOfferNote();
    renderUnits();
  });
  document.getElementById("target-orm").addEventListener("change", (event) => {
    state.targetOrm = Number(event.target.value);
    saveState();
  });
  document.getElementById("add-unit").addEventListener("click", () => {
    state.units.push(newUnit("", offeredTypes(state.sourceOrm)[0], ""));
    saveState();
    renderUnits();
  });
  const fileInput = document.getElementById("file-input");
  document.getElementById("add-files").addEventListener("click", () => fileInput.click());
  fileInput.addEventListener("change", () => {
    onAddFiles(fileInput.files);
    fileInput.value = "";
  });
  document.getElementById("load-samples").addEventListener("click", onLoadSamples);
  document.getElementById("clear").addEventListener("click", onClear);
  document.getElementById("convert").addEventListener("click", onConvert);
  document.getElementById("download").addEventListener("click", onDownload);

  // Ctrl/Cmd+Enter converts from anywhere, including from inside a unit's textarea -
  // where the plain Enter key belongs to the code being pasted.
  document.addEventListener("keydown", (event) => {
    if (event.key !== "Enter" || !(event.ctrlKey || event.metaKey)) return;
    event.preventDefault();
    if (!document.getElementById("convert").disabled) onConvert();
  });

  try {
    [state.requiredContent, state.samples] = await Promise.all([
      getRequiredContent(),
      getSamples(),
    ]);
  } catch (error) {
    const errorElement = document.getElementById("convert-error");
    errorElement.textContent = `Could not load the input catalog: ${error.message}`;
    errorElement.hidden = false;
  }
  renderOfferNote();
  renderUnits();
}

init();
