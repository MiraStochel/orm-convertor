/*
 * The translation screen (decision 033): five steps on one page - source framework,
 * target framework, input files, convert, result. Screen state is one plain object;
 * rendering functions rebuild whole areas from <template> clones (decision 032).
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
  renderCode,
  renderRecords,
  renderCatalogState,
  artifactNames,
  saveBlob,
} from "./ui.js";

const EXTENSION_TYPES = {
  ".cs": ContentType.CSharpEntity,
  ".xml": ContentType.Xml,
  ".sql": ContentType.SqlQuery,
  ".hql": ContentType.HqlQuery,
};

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
      renderUnits();
    });

    article.querySelector(".unit-remove").addEventListener("click", () => {
      state.units = state.units.filter((u) => u.id !== unit.id);
      renderUnits();
    });

    const textarea = article.querySelector(".unit-content");
    textarea.value = unit.content;
    textarea.addEventListener("input", () => {
      unit.content = textarea.value;
    });

    container.append(article);
  }

  if (state.units.length === 0) {
    const empty = document.createElement("p");
    empty.textContent = "No input files yet - add files, an empty unit, or the sample set.";
    container.append(empty);
  }
}

function renderResult() {
  const section = document.getElementById("result");
  const response = state.result;
  if (!response) {
    section.hidden = true;
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

  const names = artifactNames(response.sources);
  const container = document.getElementById("artifacts");
  container.replaceChildren();
  response.sources.forEach((artifact, index) => {
    const article = cloneTemplate("artifact-template");
    article.querySelector(".artifact-name").textContent = names[index];
    article.querySelector(".artifact-type").textContent =
      CONTENT_TYPE_LABELS[artifact.contentType] ?? "";
    article.querySelector(".artifact-copy").addEventListener("click", () => {
      navigator.clipboard?.writeText(artifact.content);
    });
    renderCode(article.querySelector("pre > code"), artifact.content, artifact.contentType);
    container.append(article);
  });

  renderRecords(document.getElementById("records"), response.records);
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
    problems.push("Add at least one input file.");
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
      problems.push(`${label}: ${unitProblem}.`);
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

function showValidation(problems) {
  const box = document.getElementById("validation");
  const list = document.getElementById("validation-list");
  list.replaceChildren();
  for (const problem of problems) {
    const item = document.createElement("li");
    item.textContent = problem;
    list.append(item);
  }
  box.hidden = problems.length === 0;
}

/* ---- actions ----------------------------------------------------------- */

async function onConvert() {
  const errorElement = document.getElementById("convert-error");
  errorElement.hidden = true;

  const problems = validate();
  showValidation(problems);
  if (problems.length > 0) return;

  const button = document.getElementById("convert");
  button.disabled = true;
  button.setAttribute("aria-busy", "true");
  state.result = null;
  renderResult();

  try {
    const sources = state.units.map((unit) => ({
      contentType: unit.contentType,
      content: unit.content,
    }));
    state.result = await convert(state.sourceOrm, state.targetOrm, sources);
    renderResult();
  } catch (error) {
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
    renderUnits();
  });
}

function onLoadSamples() {
  const definition = state.requiredContent.find((r) => r.ormType === state.sourceOrm);
  if (!definition) return;
  const units = definition.required
    .filter((required) => state.samples[required.id] !== undefined)
    .map((required) => ({
      contentType: required.contentType,
      content: state.samples[required.id],
    }));
  const names = artifactNames(units);
  state.units = units.map((unit, index) => newUnit(names[index], unit.contentType, unit.content));
  renderUnits();
}

/* ---- init --------------------------------------------------------------- */

async function init() {
  renderFrameworkSelects();
  renderUnits();

  document.getElementById("source-orm").addEventListener("change", (event) => {
    state.sourceOrm = Number(event.target.value);
    renderOfferNote();
    renderUnits();
  });
  document.getElementById("target-orm").addEventListener("change", (event) => {
    state.targetOrm = Number(event.target.value);
  });
  document.getElementById("add-unit").addEventListener("click", () => {
    state.units.push(newUnit("", offeredTypes(state.sourceOrm)[0], ""));
    renderUnits();
  });
  const fileInput = document.getElementById("file-input");
  document.getElementById("add-files").addEventListener("click", () => fileInput.click());
  fileInput.addEventListener("change", () => {
    onAddFiles(fileInput.files);
    fileInput.value = "";
  });
  document.getElementById("load-samples").addEventListener("click", onLoadSamples);
  document.getElementById("convert").addEventListener("click", onConvert);
  document.getElementById("download").addEventListener("click", onDownload);

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
