/*
 * The translation screen (decision 135, shape inherited from 033 through 124): unnumbered
 * sections on one page - an optional example, direction, input files, convert, result - and
 * the result stands input beside output, with the pairing the server states and the facts
 * ledger under both. Screen state is one plain object; rendering functions rebuild whole
 * areas from <template> clones (decision 032).
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
  SourceDialect,
  SOURCE_DIALECT_LABELS,
  convert,
  getRequiredContent,
  getSamples,
  getExamples,
  archive,
} from "./api.js";
import {
  cloneTemplate,
  attachLineNumbers,
  positionIn,
  markLine,
  clearLineMarks,
  goToLine,
  renderCode,
  renderArtifacts,
  renderRecords,
  renderCatalogState,
  revealPanel,
  recordCounts,
  kindBadge,
  artifactNames,
  saveBlob,
} from "./ui.js";
import { renderFacts } from "./facts.js";
import { readZip } from "./zip.js";

// One extension, one language (decision 111): a unit declares the language of its file and
// nothing else, so .cs is C# whether the file holds an entity, the code that queries it, or
// both - the source framework tells them apart on the server.
const EXTENSION_TYPES = {
  ".cs": ContentType.CSharp,
  ".xml": ContentType.Xml,
  ".sql": ContentType.SqlQuery,
  ".hql": ContentType.HqlQuery,
  ".java": ContentType.Java,
  ".jpql": ContentType.JpqlQuery,
};

const STORAGE_KEY = "ormconvertor.translation";

const state = {
  sourceOrm: ORM.EFCore,
  targetOrm: ORM.NHibernate,
  // null is the third state and the default: the source declares nothing about the dialect
  // of its literal SQL, and that SQL is read as it always was (decision 088).
  sourceDialect: null,
  units: [],
  requiredContent: [],
  // The sample of every unit /required-content asks for: together, one input per framework
  // (decision 135).
  samples: {},
  // The described examples from /examples (decision 099): the direction and named units.
  examples: [],
  // The units exactly as the last conversion sent them, kept apart from `units` so that the
  // result shows what the server read even after the editor above has moved on.
  sent: null,
  result: null,
  // The files of the last archive added that did not become units, each with its reason
  // (decision 136): { archives: [name], files: [{ archive, path, reason }] }. A report on how
  // the input came about, not input, so it is not remembered with the units.
  unused: null,
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

/*
 * The language an extension names, or null where it names none: the client does not guess a
 * language from the content any more than the server does (decisions 025 and 111), so the
 * unit waits for the user to choose and the validation asks for it.
 */
function typeForExtension(fileName) {
  const dot = fileName.lastIndexOf(".");
  const extension = dot >= 0 ? fileName.slice(dot).toLowerCase() : "";
  return EXTENSION_TYPES[extension] ?? null;
}

/*
 * What the records will call a unit: its name, or "unit N" with its position among the units
 * sent - every unit on the screen is sent, a blank one too, and the server counts the same way
 * (decision 066). A client counter would name the unit differently from the records about it.
 */
function unitLabel(unit) {
  return unit.name.trim() || `unit ${state.units.indexOf(unit) + 1}`;
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
        sourceDialect: state.sourceDialect,
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
  if (Object.values(SourceDialect).includes(stored.sourceDialect)) {
    state.sourceDialect = stored.sourceDialect;
  }

  if (!Array.isArray(stored.units)) return;
  // A unit restored with a value the source no longer reads - a role value stored before
  // decision 111 - keeps it: the screen marks it as not read and the validation asks for the
  // language. A unit whose language was never chosen stays without one.
  const types = Object.values(ContentType);
  state.units = stored.units
    .filter(
      (unit) =>
        unit &&
        typeof unit.content === "string" &&
        (unit.contentType === null || types.includes(unit.contentType)),
    )
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

/*
 * The declaration of decision 088, with its undeclared state first because that is what a
 * source says until somebody says otherwise. The empty option carries no value, so an
 * undeclared conversion sends no field at all.
 */
function renderSourceDialectSelect() {
  const element = document.getElementById("source-dialect");
  element.replaceChildren();

  for (const [value, label] of [
    [null, "Source SQL dialect: not declared"],
    [SourceDialect.SqlServer2022, `Source SQL is ${SOURCE_DIALECT_LABELS[SourceDialect.SqlServer2022]}`],
    [SourceDialect.AnotherSystem, `Source SQL is ${SOURCE_DIALECT_LABELS[SourceDialect.AnotherSystem]}`],
  ]) {
    const option = document.createElement("option");
    option.value = value === null ? "" : String(value);
    option.textContent = label;
    option.selected = value === state.sourceDialect;
    element.append(option);
  }
}

/* ---- the two kinds of example (decision 135) ----------------------------- */

// The value of an input's row in the list; a described example's row carries its key.
const INPUT_PREFIX = "input:";

const files = (count) => `${count} file${count === 1 ? "" : "s"}`;
const inputTitle = (orm) => `Customer in ${ORM_LABELS[orm]}`;

/*
 * The inputs: for every framework, the samples of the units /required-content asks for - the
 * same customer in each. No direction explains them, so they carry none; a framework the
 * samples do not fill is left out.
 */
function inputSets() {
  return state.requiredContent
    .map((definition) => ({
      orm: definition.ormType,
      units: definition.required
        .filter((required) => state.samples[required.id] !== undefined)
        .map((required) => ({ contentType: required.contentType, content: state.samples[required.id] })),
    }))
    .filter((set) => set.units.length > 0);
}

/*
 * The list in two groups: first the inputs, one row per framework, then the described
 * examples in the order the server lists them, each under its title and the direction it is
 * described for. The list stays disabled until the catalog has arrived, and says so when the
 * server has nothing to offer.
 */
function renderExampleSelect() {
  const select = document.getElementById("example-select");
  const button = document.getElementById("load-example");
  select.replaceChildren();

  const inputs = inputSets();
  const offered = inputs.length + state.examples.length;

  const prompt = document.createElement("option");
  prompt.value = "";
  prompt.textContent = offered === 0 ? "No examples available" : "Start from an example…";
  prompt.disabled = true;
  prompt.selected = true;
  select.append(prompt);

  if (inputs.length > 0) {
    const group = document.createElement("optgroup");
    group.label = "Inputs - any target";
    for (const set of inputs) {
      const option = document.createElement("option");
      option.value = `${INPUT_PREFIX}${set.orm}`;
      option.textContent = `${inputTitle(set.orm)} (${files(set.units.length)})`;
      group.append(option);
    }
    select.append(group);
  }

  if (state.examples.length > 0) {
    const group = document.createElement("optgroup");
    group.label = "Described on the Examples page";
    for (const example of state.examples) {
      const option = document.createElement("option");
      option.value = example.key;
      option.textContent =
        `${example.title} (${ORM_LABELS[example.sourceOrm]} → ${ORM_LABELS[example.targetOrm]}, ` +
        `${files(example.units.length)})`;
      group.append(option);
    }
    select.append(group);
  }

  select.disabled = offered === 0;
  button.disabled = true;
}

/*
 * What a row of the list loads, in the one shape loading needs: a title, the direction and the
 * units. An input takes its own framework as the target too - the one target that recommends
 * nothing - and its units are named from their content, as a file usually is.
 */
function exampleFor(value) {
  if (value.startsWith(INPUT_PREFIX)) {
    const orm = Number(value.slice(INPUT_PREFIX.length));
    const set = inputSets().find((s) => s.orm === orm);
    if (!set) return null;
    const names = artifactNames(set.units);
    return {
      title: inputTitle(orm),
      sourceOrm: orm,
      targetOrm: orm,
      units: set.units.map((unit, index) => ({ ...unit, name: names[index] })),
    };
  }
  return state.examples.find((e) => e.key === value) ?? null;
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

    // The name is optional (decisions 066 and 111); an empty field shows the label the records
    // will use for the unit instead.
    const nameInput = article.querySelector(".unit-name");
    nameInput.value = unit.name;
    nameInput.placeholder = `unit ${state.units.indexOf(unit) + 1} (file name optional)`;
    nameInput.addEventListener("input", () => {
      unit.name = nameInput.value;
      saveState();
    });

    const typeSelect = article.querySelector(".unit-type");
    if (unit.contentType === null) {
      const choose = document.createElement("option");
      choose.value = "";
      choose.textContent = "Choose the language";
      choose.disabled = true;
      choose.selected = true;
      typeSelect.append(choose);
    }
    const options =
      unit.contentType === null || offered.includes(unit.contentType)
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
    attachLineNumbers(textarea);

    container.append(article);
  }

  if (state.units.length === 0) {
    const empty = document.createElement("p");
    empty.textContent = "No input files yet - start from an example, add files, or add an empty unit.";
    container.append(empty);
  }

  document.getElementById("clear").disabled = state.units.length === 0;
}

/*
 * The files an archive brought that did not become units (decision 136): a folded list above
 * the units, one line per file with its reason, so nothing in the archive goes unmentioned.
 */
function renderUnused() {
  const details = document.getElementById("unused-files");
  const list = details.querySelector("ul");
  list.replaceChildren();
  const unused = state.unused;
  if (!unused || unused.files.length === 0) {
    details.hidden = true;
    return;
  }
  details.hidden = false;

  const several = unused.archives.length > 1;
  details.querySelector("summary").textContent =
    `${files(unused.files.length)} from ` +
    `${several ? `${unused.archives.length} archives` : unused.archives[0]} not used`;
  for (const file of unused.files) {
    const item = document.createElement("li");
    const path = document.createElement("code");
    path.textContent = several ? `${file.archive}: ${file.path}` : file.path;
    item.append(path, ` - ${file.reason}`);
    list.append(item);
  }
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
  // Stated and unstated are different facts about the run, so the strip shows both rather
  // than only the stated one (S6, decision 088).
  document.getElementById("source-dialect-fact").textContent =
    response.declaredSourceDialect == null
      ? "source SQL dialect not declared"
      : `source declares ${SOURCE_DIALECT_LABELS[response.declaredSourceDialect] ?? response.declaredSourceDialect}`;
  document.getElementById("tool-version").textContent = response.toolVersion;
  document.getElementById("run-id").textContent = response.runId;
  renderCatalogState(
    document.getElementById("catalog-state"),
    response.catalogState,
    response.catalogReadMilliseconds,
  );

  // Input beside output (decision 124). The left column shows the units as they were sent,
  // under the reference the server uses for them; the right one the artifacts, each naming
  // its units - and the unit panel names the artifacts that came of it, read off the same
  // server field, so neither side pairs anything by name.
  const sent = state.sent ?? [];
  const unitReference = (unit, index) => unit.name ?? `unit ${index + 1}`;
  const inputPanels = new Map();
  const inputs = document.getElementById("inputs");
  inputs.replaceChildren();
  sent.forEach((unit, index) => {
    const reference = unitReference(unit, index);
    const article = cloneTemplate("input-template");
    article.id = `input-${index + 1}`;
    article.querySelector(".artifact-name").textContent = reference;
    article.querySelector(".artifact-type").textContent = CONTENT_TYPE_LABELS[unit.contentType] ?? "";
    renderCode(article.querySelector("pre > code"), unit.content, unit.contentType);
    inputs.append(article);
    inputPanels.set(reference, article);
  });

  const unitPanelId = (reference) => inputPanels.get(reference)?.id;
  const artifactIndex = renderArtifacts(document.getElementById("artifacts"), response.sources, {
    unitPanelId,
  });

  const names = artifactNames(response.sources);
  for (const [reference, article] of inputPanels) {
    const produced = [];
    response.sources.forEach((artifact, position) => {
      if (artifact.units?.includes(reference)) produced.push({ name: names[position], id: `artifact-${position + 1}` });
    });
    const origin = article.querySelector(".artifact-origin");
    origin.hidden = false;
    origin.replaceChildren();
    if (produced.length === 0) {
      origin.textContent = "no artifact came of this unit alone - the records say what became of it";
      continue;
    }
    origin.append("went into ");
    produced.forEach((artifact, position) => {
      if (position > 0) origin.append(", ");
      const link = document.createElement("button");
      link.type = "button";
      link.className = "link-button";
      link.textContent = artifact.name;
      link.title = "Show this artifact";
      link.addEventListener("click", () => revealPanel(artifact.id));
      origin.append(link);
    });
  }

  renderFacts(document.getElementById("facts"), response.facts, {
    inputPanels,
    artifactPanels: [...document.getElementById("artifacts").querySelectorAll("article.artifact")],
  });
  renderRecords(document.getElementById("records"), response.records, { artifactIndex });
  markFailures(response.records);
  renderSummary(response);
}

/*
 * A record that names its unit and a position in its reason (decision 093) marks that line
 * in the unit's editor, under the reference the server used (decision 066): the name sent,
 * or "unit N" by position. The units are matched as the editor holds them now, which is
 * what was just sent.
 */
function markFailures(records) {
  clearUnitProblems();
  const container = document.getElementById("units");
  for (const record of records ?? []) {
    if (!record.unit || !positionIn(record.reason)) continue;
    const unit = state.units.find((u) => unitLabel(u) === record.unit);
    const article = unit && container.querySelector(`[data-unit-id="${unit.id}"]`);
    if (article) showUnitProblem(article, record.reason);
  }
}

/* ---- validation (S7): a helper, not the gate - the server stays authoritative */

function xmlProblem(content) {
  const parsed = new DOMParser().parseFromString(content, "text/xml");
  const error = parsed.querySelector("parsererror");
  if (!error) return null;
  const position = positionIn(error.textContent);
  if (!position) return "not well-formed XML";
  return position.column == null
    ? `not well-formed XML (line ${position.line})`
    : `not well-formed XML (line ${position.line}, column ${position.column})`;
}

/*
 * The sentence under a unit; where it names a line, that line is marked in the editor's
 * column and the sentence gets a button that goes there (decision 126).
 */
function showUnitProblem(article, text) {
  const errorElement = article.querySelector(".unit-error");
  const textarea = article.querySelector(".unit-content");
  if (!errorElement.hidden) errorElement.append(document.createElement("br"));
  errorElement.append(text);
  const position = positionIn(text);
  if (position && textarea) {
    markLine(textarea, position.line, text);
    const jump = document.createElement("button");
    jump.type = "button";
    jump.className = "link-button";
    jump.textContent = `go to line ${position.line}`;
    jump.addEventListener("click", () => goToLine(textarea, position.line));
    errorElement.append(" ", jump);
  }
  errorElement.hidden = false;
}

function clearUnitProblems() {
  for (const article of document.getElementById("units").querySelectorAll("article.unit")) {
    const errorElement = article.querySelector(".unit-error");
    errorElement.hidden = true;
    errorElement.replaceChildren();
    const textarea = article.querySelector(".unit-content");
    if (textarea) clearLineMarks(textarea);
  }
}

function validate() {
  const problems = [];
  const container = document.getElementById("units");
  clearUnitProblems();

  if (state.units.length === 0) {
    problems.push({ text: "Add at least one input file." });
    return problems;
  }

  const offered = offeredTypes(state.sourceOrm);
  for (const unit of state.units) {
    const label = unitLabel(unit);
    let unitProblem = null;
    if (unit.content.trim() === "") {
      unitProblem = "the content is empty";
    } else if (unit.contentType === null) {
      unitProblem = "choose the language of the unit - its file extension names none";
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
      if (article) showUnitProblem(article, unitProblem);
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
    // The name goes along so records can point back at the unit (decision 066); a unit
    // the user left unnamed sends none and the server references it by position.
    const sources = state.units.map((unit) => ({
      contentType: unit.contentType,
      content: unit.content,
      name: unit.name.trim() === "" ? null : unit.name.trim(),
    }));
    state.result = await convert(state.sourceOrm, state.targetOrm, sources, state.sourceDialect);
    state.sent = sources;
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

/* One way to read a file's text, for a picked file and a file from an archive alike. */
function readText(blob) {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result));
    reader.onerror = () => reject(reader.error);
    reader.readAsText(blob);
  });
}

const isArchive = (fileName) => fileName.toLowerCase().endsWith(".zip");

/*
 * Why a file of an archive does not become a unit, or null where it does (decision 136): it
 * must name, by its extension, a language the source reads - the rule the screen applies to a
 * picked file, applied before the unit exists rather than by the validation after.
 * __MACOSX/ is not the project's but the macOS archiver's, with binary files under the very
 * extensions of the source.
 */
function unusedReason(entry, offered) {
  if (entry.path.startsWith("__MACOSX/")) return "macOS archive metadata, not a file of the project";
  const type = typeForExtension(entry.path);
  if (type === null) return "its extension names no language";
  if (!offered.includes(type)) {
    return `${ORM_LABELS[state.sourceOrm]} does not read ${CONTENT_TYPE_LABELS[type]}`;
  }
  return entry.problem;
}

/*
 * A ZIP archive is unpacked here, in the browser (decision 136): every file in a language the
 * source reads becomes a unit named by its path in the archive, every other file goes to the
 * list of the unused with its reason, and an archive that cannot be read at all is one line
 * of that list.
 */
async function unpackArchive(file, units, unused) {
  const skip = (path, reason) => unused.push({ archive: file.name, path, reason });
  let entries;
  try {
    entries = await readZip(file);
  } catch (error) {
    skip(file.name, `the archive cannot be read: ${error.message}`);
    return;
  }
  const offered = offeredTypes(state.sourceOrm);
  for (const entry of entries) {
    if (entry.directory) continue;
    const reason = unusedReason(entry, offered);
    if (reason) {
      skip(entry.path, reason);
      continue;
    }
    let content;
    try {
      content = await readText(await entry.read());
    } catch (error) {
      skip(entry.path, `it could not be unpacked: ${error.message}`);
      continue;
    }
    // An empty unit would only stop the validation, so an empty file is reported instead.
    if (content.trim() === "") {
      skip(entry.path, "it is empty");
      continue;
    }
    units.push(newUnit(entry.path, typeForExtension(entry.path), content));
  }
}

async function onAddFiles(fileList) {
  // Copied before the first await: the caller empties the file input right after the call,
  // and with it the list.
  const picked = [...fileList];
  const units = [];
  const unused = [];
  const archives = [];
  try {
    for (const file of picked) {
      if (isArchive(file.name)) {
        archives.push(file.name);
        await unpackArchive(file, units, unused);
      } else {
        units.push(newUnit(file.name, typeForExtension(file.name), await readText(file)));
      }
    }
  } catch (error) {
    const errorElement = document.getElementById("convert-error");
    errorElement.textContent = `Could not read the files: ${error.message}`;
    errorElement.hidden = false;
  }

  state.units.push(...units);
  saveState();
  renderUnits();
  if (archives.length === 0) return;
  state.unused = { archives, files: unused };
  renderUnused();
  document.getElementById("convert-status").textContent =
    `Added ${files(units.length)}` +
    (unused.length === 0
      ? "."
      : `; ${files(unused.length)} from the archive${archives.length > 1 ? "s" : ""} not used, ` +
        "listed above the units.");
}

/*
 * An example is a whole conversion input (decisions 099 and 135), so loading one sets
 * everything a conversion sends: the direction, the units under their names, and no declared
 * dialect - the example's SQL is read as it always is, exactly as the Examples page sends it.
 * It replaces the screen wholesale, and with work on it, it asks.
 */
function onLoadExample() {
  const select = document.getElementById("example-select");
  const example = exampleFor(select.value);
  if (!example) return;
  if (
    state.units.length > 0 &&
    !confirm(
      `Loading "${example.title}" replaces every unit on the screen and sets the direction to ` +
        `${ORM_LABELS[example.sourceOrm]} → ${ORM_LABELS[example.targetOrm]}. Continue?`,
    )
  ) {
    return;
  }

  state.sourceOrm = example.sourceOrm;
  state.targetOrm = example.targetOrm;
  state.sourceDialect = null;
  state.units = example.units.map((unit) => newUnit(unit.name ?? "", unit.contentType, unit.content));
  state.unused = null;
  saveState();
  showValidation([]);
  renderFrameworkSelects();
  renderSourceDialectSelect();
  renderOfferNote();
  renderUnits();
  renderUnused();
  document.getElementById("convert-status").textContent =
    `Loaded "${example.title}": ${example.units.length} file${example.units.length === 1 ? "" : "s"}.`;
}

/** Clears the screen and the remembered copy together (decision 056). */
function onClear() {
  if (state.units.length > 0 && !confirm("Remove every unit from the screen?")) return;
  state.units = [];
  state.sent = null;
  state.result = null;
  state.unused = null;
  forgetState();
  showValidation([]);
  document.getElementById("convert-status").textContent = "";
  document.getElementById("convert-error").hidden = true;
  renderUnits();
  renderUnused();
  renderResult();
}

/* ---- init --------------------------------------------------------------- */

async function init() {
  restoreState();
  renderFrameworkSelects();
  renderSourceDialectSelect();
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
  // The direction is one thing (decision 124): swapping it is one click, and the input list
  // follows the new source - a unit the new source does not read is marked as such, not
  // dropped, exactly as a change of the source select treats it.
  document.getElementById("swap").addEventListener("click", () => {
    [state.sourceOrm, state.targetOrm] = [state.targetOrm, state.sourceOrm];
    saveState();
    renderFrameworkSelects();
    renderOfferNote();
    renderUnits();
  });
  document.getElementById("source-dialect").addEventListener("change", (event) => {
    state.sourceDialect = event.target.value === "" ? null : Number(event.target.value);
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
  document.getElementById("example-select").addEventListener("change", (event) => {
    document.getElementById("load-example").disabled = event.target.value === "";
  });
  document.getElementById("load-example").addEventListener("click", onLoadExample);
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
    [state.requiredContent, state.samples, state.examples] = await Promise.all([
      getRequiredContent(),
      getSamples(),
      getExamples(),
    ]);
  } catch (error) {
    const errorElement = document.getElementById("convert-error");
    errorElement.textContent = `Could not load the input catalog: ${error.message}`;
    errorElement.hidden = false;
  }
  renderExampleSelect();
  renderOfferNote();
  renderUnits();
}

init();
