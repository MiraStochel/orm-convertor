/*
 * The explanatory page (decisions 032e and 099): live examples. Each is a whole
 * conversion input the server hands out through /examples - the direction and the named
 * units - which the page sends to /convert unchanged and renders as the real input, output
 * and records. An example produced by the tool cannot drift away from it.
 *
 * The prose is the page's and the data is the server's: every section names its example by
 * the key the server gives it, so the page states no unit, no sample id and no direction of
 * its own. A key the server does not know says so in its section instead of converting
 * nothing, and the suite asserts that every example the server knows converts
 * (ExampleCatalogTest).
 */

import { ORM_LABELS, getExamples, convert } from "./api.js";
import { renderArtifacts, renderRecords, renderCatalogState } from "./ui.js";

const plural = (count, word) => `${count} ${word}${count === 1 ? "" : "s"}`;

function showError(section, message) {
  section.querySelector(".example-status").hidden = true;
  const errorElement = section.querySelector(".example-error");
  errorElement.textContent = message;
  errorElement.hidden = false;
}

async function runExample(example, section) {
  const status = section.querySelector(".example-status");
  const errorElement = section.querySelector(".example-error");
  errorElement.hidden = true;
  status.hidden = false;
  status.setAttribute("aria-busy", "true");
  status.textContent = "Running the conversion…";

  // The units carry the names of the files they stand for, so the input panels show those
  // names and the records band's Unit column points at them (decision 066).
  renderArtifacts(section.querySelector(".example-inputs"), example.units, {
    idPrefix: `${example.key}-input`,
  });
  section.querySelector(".example-input-count").textContent = plural(example.units.length, "file");

  try {
    const response = await convert(example.sourceOrm, example.targetOrm, example.units);
    const artifactIndex = renderArtifacts(
      section.querySelector(".example-outputs"),
      response.sources,
      { idPrefix: `${example.key}-output` },
    );
    section.querySelector(".example-output-count").textContent =
      plural(response.sources.length, "artifact");
    renderRecords(section.querySelector(".example-records"), response.records, { artifactIndex });

    status.removeAttribute("aria-busy");
    status.replaceChildren();
    status.append(
      `${ORM_LABELS[response.sourceFramework]} ${response.sourceFrameworkVersion}` +
        ` → ${ORM_LABELS[response.targetFramework]} ${response.targetFrameworkVersion} `,
    );
    const catalogBadge = document.createElement("span");
    renderCatalogState(catalogBadge, response.catalogState, response.catalogReadMilliseconds);
    status.append(catalogBadge);
  } catch (error) {
    showError(section, error.message);
  }
}

async function init() {
  const sections = [...document.querySelectorAll("[data-example]")];

  let examples;
  try {
    examples = await getExamples();
  } catch (error) {
    for (const section of sections) {
      showError(section, `Could not load the examples: ${error.message}`);
    }
    return;
  }

  const byKey = new Map(examples.map((example) => [example.key, example]));
  for (const section of sections) {
    const example = byKey.get(section.dataset.example);
    if (!example) {
      showError(section, `The server has no example called "${section.dataset.example}".`);
      continue;
    }
    section
      .querySelector(".example-run")
      .addEventListener("click", () => runExample(example, section));
    runExample(example, section);
  }
}

init();
