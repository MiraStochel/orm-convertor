/*
 * The explanatory page (decisions 032e and 033): two live examples. Each loads its
 * sample through /samples, converts it through /convert and renders the real input,
 * output and records - an example produced by the tool cannot drift away from it.
 */

import {
  ORM,
  ORM_LABELS,
  ContentType,
  CONTENT_TYPE_LABELS,
  getSamples,
  convert,
} from "./api.js";
import {
  cloneTemplate,
  renderCode,
  renderRecords,
  renderCatalogState,
  artifactNames,
} from "./ui.js";

const EXAMPLES = [
  {
    key: "efcore-to-nhibernate",
    sourceOrm: ORM.EFCore,
    targetOrm: ORM.NHibernate,
    units: [
      { sampleId: 4, contentType: ContentType.CSharpEntity },
      { sampleId: 5, contentType: ContentType.CSharpQuery },
    ],
  },
  {
    key: "dapper-to-efcore",
    sourceOrm: ORM.Dapper,
    targetOrm: ORM.EFCore,
    units: [
      { sampleId: 1, contentType: ContentType.CSharpEntity },
      { sampleId: 8, contentType: ContentType.SqlQuery },
    ],
  },
];

function renderArtifacts(container, artifacts) {
  container.replaceChildren();
  const names = artifactNames(artifacts);
  artifacts.forEach((artifact, index) => {
    const article = cloneTemplate("artifact-template");
    article.querySelector(".artifact-name").textContent = names[index];
    article.querySelector(".artifact-type").textContent =
      CONTENT_TYPE_LABELS[artifact.contentType] ?? "";
    renderCode(article.querySelector("pre > code"), artifact.content, artifact.contentType);
    container.append(article);
  });
}

async function runExample(example, section, samples) {
  const status = section.querySelector(".example-status");
  const errorElement = section.querySelector(".example-error");
  errorElement.hidden = true;
  status.hidden = false;
  status.setAttribute("aria-busy", "true");
  status.textContent = "Running the conversion…";

  const sources = example.units.map((unit) => ({
    contentType: unit.contentType,
    content: samples[unit.sampleId] ?? "",
  }));
  renderArtifacts(section.querySelector(".example-inputs"), sources);

  try {
    const response = await convert(example.sourceOrm, example.targetOrm, sources);
    renderArtifacts(section.querySelector(".example-outputs"), response.sources);
    renderRecords(section.querySelector(".example-records"), response.records);

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

async function init() {
  let samples;
  try {
    samples = await getSamples();
  } catch (error) {
    for (const section of document.querySelectorAll("[data-example]")) {
      const status = section.querySelector(".example-status");
      status.hidden = true;
      const errorElement = section.querySelector(".example-error");
      errorElement.textContent = `Could not load the samples: ${error.message}`;
      errorElement.hidden = false;
    }
    return;
  }

  for (const example of EXAMPLES) {
    const section = document.querySelector(`[data-example="${example.key}"]`);
    section
      .querySelector(".example-run")
      .addEventListener("click", () => runExample(example, section, samples));
    runExample(example, section, samples);
  }
}

init();
