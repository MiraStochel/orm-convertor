/*
 * The tutorial (decision 213): one live conversion walked through the five steps of the
 * translation screen. The page names its example by the key the server gives it, shows the
 * example's real units, converts them when the reader presses Convert, and renders the result
 * with the translation screen's own functions - so everything the steps point at is the
 * server's statement, and only the prose and the labels of the screen's controls are the
 * page's.
 *
 * It neither reads nor writes the translation screen's stored input (decision 056): what the
 * reader has in the translator stays there whatever happens here.
 */

import {
  ORM_LABELS,
  CONTENT_TYPE_LABELS,
  SOURCE_DIALECT_LABELS,
  CatalogState,
  convert,
  getExamples,
  getRequiredContent,
} from "./api.js";
import {
  cloneTemplate,
  renderCode,
  renderArtifacts,
  renderRecords,
  renderCatalogState,
  revealPanel,
  recordCounts,
  kindBadge,
  artifactNames,
} from "./ui.js";
import { renderFacts } from "./facts.js";

const EXAMPLE_KEY = "dapper-to-efcore";

const plural = (count, word) => `${count} ${word}${count === 1 ? "" : "s"}`;
const direction = (example) => `${ORM_LABELS[example.sourceOrm]} → ${ORM_LABELS[example.targetOrm]}`;

let example = null;

function showPageError(message) {
  const element = document.getElementById("tutorial-error");
  element.textContent = message;
  element.hidden = false;
}

/* ---- steps 1 to 3: the example as the server hands it out ---------------- */

/*
 * The row the translator's list shows for a described example, spelled as translation.js
 * spells it, so that the reader looks for exactly this text.
 */
function renderExampleStep() {
  document.getElementById("example-option").textContent =
    `${example.title} (${direction(example)}, ${plural(example.units.length, "file")})`;
  for (const element of document.querySelectorAll(".example-direction")) {
    element.textContent = direction(example);
  }
  document.getElementById("open-example").href =
    `translation.html?example=${encodeURIComponent(example.key)}`;
}

/* The offer line the translator shows under Input files for the example's source. */
function renderDirectionStep(requiredContent) {
  const definition = requiredContent.find((r) => r.ormType === example.sourceOrm);
  if (!definition) return;
  const offered = [...new Set(definition.required.map((u) => u.contentType))];
  document.getElementById("offer-line").textContent =
    `${ORM_LABELS[example.sourceOrm]} input is read as: ` +
    offered.map((t) => CONTENT_TYPE_LABELS[t]).join(", ") +
    ".";
}

function renderInputStep() {
  const status = document.getElementById("input-status");
  status.removeAttribute("aria-busy");
  status.textContent = `${plural(example.units.length, "unit")}, source ${ORM_LABELS[example.sourceOrm]}:`;
  renderArtifacts(document.getElementById("tutorial-inputs"), example.units, {
    idPrefix: "tutorial-unit",
  });
  // The input panels are read-only here; the editor is the translator's.
  for (const actions of document.querySelectorAll("#tutorial-inputs .artifact-actions")) {
    actions.querySelector(".artifact-download")?.remove();
  }
}

/* ---- step 4: the reader converts ---------------------------------------- */

function renderSummary(response) {
  const strip = document.getElementById("tutorial-summary");
  strip.replaceChildren();
  strip.hidden = false;

  const artifacts = document.createElement("span");
  artifacts.textContent = plural(response.sources?.length ?? 0, "artifact");
  strip.append(artifacts);

  const records = response.records ?? [];
  if (records.length === 0) {
    const none = document.createElement("span");
    none.textContent = "no records";
    strip.append(none);
  }
  for (const [kind, count] of recordCounts(records)) {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "badge-button";
    button.title = "Show the records";
    button.append(kindBadge(kind, `x ${count}`));
    button.addEventListener("click", () =>
      document.getElementById("tutorial-records").scrollIntoView({ behavior: "smooth", block: "center" }),
    );
    strip.append(button);
  }

  const jump = document.createElement("button");
  jump.type = "button";
  jump.className = "link-button";
  jump.textContent = "go to the result";
  jump.addEventListener("click", () =>
    document.getElementById("step-result").scrollIntoView({ behavior: "smooth", block: "start" }),
  );
  strip.append(jump);
}

async function onConvert() {
  const button = document.getElementById("tutorial-convert");
  const status = document.getElementById("tutorial-convert-status");
  button.disabled = true;
  button.setAttribute("aria-busy", "true");
  status.textContent = "Converting…";
  document.getElementById("tutorial-summary").hidden = true;

  try {
    const response = await convert(example.sourceOrm, example.targetOrm, example.units);
    status.textContent =
      `Converted: ${plural(response.sources?.length ?? 0, "artifact")}, ` +
      `${plural(response.records?.length ?? 0, "record")}.`;
    renderSummary(response);
    renderResult(response);
  } catch (error) {
    status.textContent = `The conversion failed: ${error.message}`;
  } finally {
    button.disabled = false;
    button.removeAttribute("aria-busy");
    button.textContent = "Convert again";
  }
}

/* ---- step 5: the result, drawn as the translator draws it ---------------- */

// What the catalog badge means for this example: a Dapper source names no key, no table and
// no column type, so the state decides where those facts come from (UC1).
const CATALOG_EXPLANATIONS = Object.freeze({
  [CatalogState.Reached]:
    "This instance reads a database catalog, so the facts the Dapper source never stated - the key, " +
    "the table, the column types and nullability - were filled in from the database. Look for the " +
    "Supplied records below and for the facts supplied by the catalog.",
  [CatalogState.NotConfigured]:
    "This instance has no catalog connection, so the facts the Dapper source never stated stay " +
    "missing: the entity comes out on EF Core's conventions, and the records say what was missing " +
    "(Incompleteness) and what a convention filled in (Convention). A Dapper class states no mapping fact itself, so with nothing supplied either, the Facts band below stays empty. The containerized deployment, " +
    "docker compose up, configures the catalog.",
  [CatalogState.Unused]:
    "A catalog is configured but nothing had to be asked of it. The records say what conventions " +
    "filled in.",
  [CatalogState.Unreachable]:
    "A catalog is configured but could not be read, so the conversion ran on conventions; the " +
    "records say what was missing. With nothing supplied, the Facts band below stays empty: a Dapper class states no mapping fact itself.",
});

function renderResult(response) {
  document.getElementById("result-placeholder").hidden = true;
  document.getElementById("tutorial-result").hidden = false;

  document.getElementById("tutorial-frameworks").textContent =
    `${ORM_LABELS[response.sourceFramework]} ${response.sourceFrameworkVersion}` +
    ` → ${ORM_LABELS[response.targetFramework]} ${response.targetFrameworkVersion}`;
  document.getElementById("tutorial-dialect").textContent =
    response.declaredSourceDialect == null
      ? "source SQL dialect not declared"
      : `source declares ${SOURCE_DIALECT_LABELS[response.declaredSourceDialect] ?? response.declaredSourceDialect}`;
  renderCatalogState(
    document.getElementById("tutorial-catalog"),
    response.catalogState,
    response.catalogReadMilliseconds,
  );
  document.getElementById("tutorial-version").textContent = response.toolVersion;
  document.getElementById("tutorial-run-id").textContent = response.runId;
  document.getElementById("catalog-explanation").textContent =
    CATALOG_EXPLANATIONS[response.catalogState] ?? "";

  // Input beside output, paired by the server's word (decision 124), as translation.js does.
  const inputPanels = new Map();
  const sent = document.getElementById("tutorial-sent");
  sent.replaceChildren();
  example.units.forEach((unit, index) => {
    const reference = unit.name ?? `unit ${index + 1}`;
    const article = cloneTemplate("input-template");
    article.id = `tutorial-input-${index + 1}`;
    article.querySelector(".artifact-name").textContent = reference;
    article.querySelector(".artifact-type").textContent = CONTENT_TYPE_LABELS[unit.contentType] ?? "";
    renderCode(article.querySelector("pre > code"), unit.content, unit.contentType);
    sent.append(article);
    inputPanels.set(reference, article);
  });

  const artifactIndex = renderArtifacts(document.getElementById("tutorial-artifacts"), response.sources, {
    idPrefix: "tutorial-artifact",
    unitPanelId: (reference) => inputPanels.get(reference)?.id,
  });

  const names = artifactNames(response.sources);
  for (const [reference, article] of inputPanels) {
    const origin = article.querySelector(".artifact-origin");
    origin.hidden = false;
    origin.replaceChildren();
    const produced = [];
    response.sources.forEach((artifact, position) => {
      if (artifact.units?.includes(reference)) {
        produced.push({ name: names[position], id: `tutorial-artifact-${position + 1}` });
      }
    });
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

  renderFacts(document.getElementById("tutorial-facts"), response.facts, {
    inputPanels,
    artifactPanels: [...document.getElementById("tutorial-artifacts").querySelectorAll("article.artifact")],
  });
  renderRecords(document.getElementById("tutorial-records"), response.records, { artifactIndex });
}

/* ---- the step bar: which step the reader is at --------------------------- */

/*
 * The current step is read off the scroll position, not kept as state: the step whose
 * heading last passed the top third of the window is the one being read.
 */
function followSteps() {
  const links = new Map(
    [...document.querySelectorAll(".tutorial-steps a")].map((link) => [link.hash.slice(1), link]),
  );
  const sections = [...document.querySelectorAll(".tutorial-step")];

  const update = () => {
    const line = window.innerHeight / 3;
    let current = sections[0];
    for (const section of sections) {
      if (section.getBoundingClientRect().top <= line) current = section;
    }
    for (const [id, link] of links) {
      if (id === current.id) link.setAttribute("aria-current", "step");
      else link.removeAttribute("aria-current");
    }
  };

  let pending = false;
  window.addEventListener(
    "scroll",
    () => {
      if (pending) return;
      pending = true;
      requestAnimationFrame(() => {
        pending = false;
        update();
      });
    },
    { passive: true },
  );
  window.addEventListener("resize", update);
  update();
}

/* ---- init ---------------------------------------------------------------- */

async function init() {
  followSteps();
  document.getElementById("tutorial-convert").addEventListener("click", onConvert);

  let examples;
  let requiredContent;
  try {
    [examples, requiredContent] = await Promise.all([getExamples(), getRequiredContent()]);
  } catch (error) {
    showPageError(`Could not load the example from this instance: ${error.message}. The steps still read as written.`);
    document.getElementById("input-status").removeAttribute("aria-busy");
    document.getElementById("input-status").textContent = "The example's files could not be loaded.";
    return;
  }

  example = examples.find((e) => e.key === EXAMPLE_KEY) ?? null;
  if (!example) {
    showPageError(`The server has no example called "${EXAMPLE_KEY}".`);
    document.getElementById("input-status").removeAttribute("aria-busy");
    document.getElementById("input-status").textContent = "The example's files could not be loaded.";
    return;
  }

  renderExampleStep();
  renderDirectionStep(requiredContent);
  renderInputStep();
  document.getElementById("tutorial-convert").disabled = false;
}

init();
