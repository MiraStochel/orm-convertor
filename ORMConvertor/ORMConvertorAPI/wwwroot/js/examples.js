/*
 * The explanatory page (decisions 032e and 033): two live examples. Each loads its
 * sample through /samples, converts it through /convert and renders the real input,
 * output and records - an example produced by the tool cannot drift away from it.
 *
 * Which units an example sends is the server's answer too: /required-content already says
 * what each source framework is asked for, and /samples keys the samples by the same ids.
 * Reading both is what keeps the page honest - a hardcoded id list would convert empty
 * units the day the ids move, and nothing would say so.
 */

import {
  ORM,
  ORM_LABELS,
  getRequiredContent,
  getSamples,
  convert,
} from "./api.js";
import {
  renderArtifacts,
  renderRecords,
  renderCatalogState,
} from "./ui.js";

const EXAMPLES = [
  { key: "efcore-to-nhibernate", sourceOrm: ORM.EFCore, targetOrm: ORM.NHibernate },
  { key: "dapper-to-efcore", sourceOrm: ORM.Dapper, targetOrm: ORM.EFCore },
];

function unitsFor(example, requiredContent, samples) {
  const definition = requiredContent.find((r) => r.ormType === example.sourceOrm);
  return (definition?.required ?? [])
    .filter((unit) => samples[unit.id] !== undefined)
    .map((unit) => ({ contentType: unit.contentType, content: samples[unit.id] }));
}

async function runExample(example, section, sources) {
  const status = section.querySelector(".example-status");
  const errorElement = section.querySelector(".example-error");
  errorElement.hidden = true;
  status.hidden = false;
  status.setAttribute("aria-busy", "true");
  status.textContent = "Running the conversion…";

  renderArtifacts(section.querySelector(".example-inputs"), sources, {
    idPrefix: `${example.key}-input`,
  });

  try {
    const response = await convert(example.sourceOrm, example.targetOrm, sources);
    const artifactIndex = renderArtifacts(
      section.querySelector(".example-outputs"),
      response.sources,
      { idPrefix: `${example.key}-output` },
    );
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
    status.hidden = true;
    errorElement.textContent = error.message;
    errorElement.hidden = false;
  }
}

function failEverySection(message) {
  for (const section of document.querySelectorAll("[data-example]")) {
    section.querySelector(".example-status").hidden = true;
    const errorElement = section.querySelector(".example-error");
    errorElement.textContent = message;
    errorElement.hidden = false;
  }
}

async function init() {
  let requiredContent;
  let samples;
  try {
    [requiredContent, samples] = await Promise.all([getRequiredContent(), getSamples()]);
  } catch (error) {
    failEverySection(`Could not load the samples: ${error.message}`);
    return;
  }

  for (const example of EXAMPLES) {
    const section = document.querySelector(`[data-example="${example.key}"]`);
    const sources = unitsFor(example, requiredContent, samples);
    if (sources.length === 0) {
      section.querySelector(".example-status").hidden = true;
      const errorElement = section.querySelector(".example-error");
      errorElement.textContent =
        `The server declares no sample input for ${ORM_LABELS[example.sourceOrm]}.`;
      errorElement.hidden = false;
      continue;
    }
    section
      .querySelector(".example-run")
      .addEventListener("click", () => runExample(example, section, sources));
    runExample(example, section, sources);
  }
}

init();
