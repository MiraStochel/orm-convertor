/*
 * The facts band of the translation screen (decision 124): one row per mapping fact the
 * server's ledger carries - entity, property, category, value, where it came from and what
 * became of it - and, over the code panels, an underline wherever the fact's value shows
 * itself. Two kinds of statement meet here and the page keeps them apart:
 *
 *   - the row is the server's: which fact, which unit stated it, whether the artifact carries
 *     it; nothing in it is read out of a name or a text;
 *   - the underline is this page's: a search for the fact's literal value in the unit the
 *     server named and in the artifacts of the fact's entity. It finds the fact where the
 *     value is spelled the same on both sides and misses it where a framework respells it,
 *     and the band says so. The search is the one the mockup uses (comparison-marking.js),
 *     run over server facts instead of authored spans.
 */

import { occurrences, segmentsOf, markSegments } from "./comparison-marking.js";
import {
  FactOrigin,
  FACT_ORIGIN_LABELS,
  FactOutcome,
  FACT_OUTCOME_LABELS,
  MAPPING_FACT_CATEGORY_LABELS,
} from "./api.js";

const OUTCOME_CLASSES = Object.freeze({
  [FactOutcome.Carried]: "badge-link-carried",
  [FactOutcome.Dropped]: "badge-link-dropped",
  [FactOutcome.NotGenerated]: "badge-failure",
});

const OUTCOME_EXPLANATIONS = Object.freeze({
  [FactOutcome.Carried]:
    "The target expresses the fact and no record says it was dropped, so the artifact states it.",
  [FactOutcome.Dropped]:
    "The fact is not in the output: the target cannot record it, or a Loss record names it.",
  [FactOutcome.NotGenerated]:
    "The entity's artifacts were not generated, so there is nothing to carry the fact into; the Failure records say why.",
});

const ORIGIN_CLASSES = Object.freeze({
  [FactOrigin.Source]: "badge-origin-source",
  [FactOrigin.Catalog]: "badge-supplied",
  [FactOrigin.Derived]: "badge-convention",
});

/* ---- which fact gets which colour --------------------------------------- */

/*
 * The mockup's three words (decision 100), now assigned by the server's two fields: a fact
 * that travelled from the input is carried, one the output states without the input having
 * said it is added, and one that did not reach the output is dropped.
 */
function kindOf(fact) {
  if (fact.outcome !== FactOutcome.Carried) return "dropped";
  return fact.origin === FactOrigin.Source ? "carried" : "added";
}

/*
 * The literal texts to look for. A value with several parts - the columns of a foreign key,
 * precision beside scale - is searched part by part; a one-character value would underline
 * every digit on the screen, so it is not searched at all; nullability is a word of the
 * ledger, not of either text.
 */
function tokensOf(fact) {
  if (fact.category === 7 /* nullability */) return [];
  const value = fact.value ?? fact.property;
  if (!value) return [];
  return value
    .split(", ")
    .map((part) => part.trim())
    .filter((part) => part.length >= 2);
}

/* ---- the underlines ----------------------------------------------------- */

/*
 * Underlines every fact's value in the panels it concerns: the unit the server says stated
 * it, and the artifacts of its entity where the server says it was carried. Returns nothing;
 * the marks carry the indexes of their facts in `data-link`, which is how the rows and the
 * marks find each other afterwards.
 */
function markFacts(facts, { inputPanels, artifactPanels }) {
  const ranges = new Map();

  const add = (panel, token, index) => {
    const code = panel?.querySelector("pre > code");
    if (!code) return;
    if (!ranges.has(code)) ranges.set(code, []);
    for (const [start, end] of occurrences(code.textContent, token, { ignoreCase: true })) {
      ranges.get(code).push({ start, end, link: String(index) });
    }
  };

  facts.forEach((fact, index) => {
    const tokens = tokensOf(fact);
    if (tokens.length === 0) return;

    const panels = [];
    if (fact.origin === FactOrigin.Source && fact.unit) {
      const panel = inputPanels.get(fact.unit);
      if (panel) panels.push(panel);
    }
    if (fact.outcome === FactOutcome.Carried) {
      panels.push(...artifactPanels.filter((panel) => panel.dataset.entity === fact.entity));
    }
    for (const panel of panels) {
      for (const token of tokens) add(panel, token, index);
    }
  });

  for (const [code, found] of ranges) {
    const marks = markSegments(code, segmentsOf(found));
    for (const mark of marks) {
      const first = Number(mark.dataset.link.split(" ")[0]);
      mark.dataset.kind = kindOf(facts[first]);
    }
  }
}

/* ---- the band ------------------------------------------------------------- */

function badge(className, text, title) {
  const element = document.createElement("span");
  element.className = `badge ${className}`;
  element.textContent = text;
  if (title) element.title = title;
  return element;
}

/**
 * Renders the facts band into `container` and underlines the facts in the panels.
 * `inputPanels` maps a unit reference - the name the client sent, or "unit N" - to the panel
 * showing that unit; `artifactPanels` are the artifact panels, each carrying the entity the
 * server generated it for in `data-entity`.
 */
export function renderFacts(container, facts, { inputPanels, artifactPanels }) {
  container.replaceChildren();

  if (!facts || facts.length === 0) {
    const note = document.createElement("p");
    note.textContent =
      "No facts - the representation held no mapping fact to carry, so nothing was generated from one.";
    container.append(note);
    return;
  }

  markFacts(facts, { inputPanels, artifactPanels });

  /* The per-outcome summary, doubling as a filter like the records band's. */
  const summary = document.createElement("div");
  summary.className = "record-summary";
  const counts = new Map();
  for (const fact of facts) counts.set(fact.outcome, (counts.get(fact.outcome) ?? 0) + 1);
  const buttons = new Map();
  let activeOutcome = null;

  for (const [outcome, count] of [...counts.entries()].sort((a, b) => a[0] - b[0])) {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "badge-button";
    button.setAttribute("aria-pressed", "false");
    button.title = `${OUTCOME_EXPLANATIONS[outcome] ?? ""} Click to show only these.`;
    button.append(
      badge(OUTCOME_CLASSES[outcome] ?? "", `${FACT_OUTCOME_LABELS[outcome] ?? outcome} x ${count}`),
    );
    button.addEventListener("click", () => {
      activeOutcome = activeOutcome === outcome ? null : outcome;
      for (const [other, otherButton] of buttons) {
        otherButton.setAttribute("aria-pressed", String(other === activeOutcome));
      }
      renderRows();
    });
    buttons.set(outcome, button);
    summary.append(button);
  }
  container.append(summary);

  const figure = document.createElement("figure");
  figure.className = "table-scroll";
  const table = document.createElement("table");
  table.className = "records-table facts-table";

  const head = document.createElement("thead");
  const headRow = document.createElement("tr");
  for (const title of ["Entity", "Property", "Fact", "Value", "Origin", "Outcome"]) {
    const cell = document.createElement("th");
    cell.textContent = title;
    headRow.append(cell);
  }
  head.append(headRow);
  table.append(head);

  const body = document.createElement("tbody");
  table.append(body);
  figure.append(table);
  container.append(figure);

  const sorted = facts
    .map((fact, index) => ({ fact, index }))
    .sort(
      (a, b) =>
        a.fact.entity.localeCompare(b.fact.entity) ||
        (a.fact.property ?? "").localeCompare(b.fact.property ?? "") ||
        a.fact.category - b.fact.category,
    );

  function renderRows() {
    body.replaceChildren();
    const shown =
      activeOutcome == null ? sorted : sorted.filter(({ fact }) => fact.outcome === activeOutcome);
    for (const { fact, index } of shown) {
      const row = document.createElement("tr");
      row.dataset.fact = String(index);

      for (const value of [
        fact.entity,
        fact.property ?? "",
        MAPPING_FACT_CATEGORY_LABELS[fact.category] ?? String(fact.category),
      ]) {
        const cell = document.createElement("td");
        cell.textContent = value;
        row.append(cell);
      }

      const valueCell = document.createElement("td");
      if (fact.value != null) {
        const code = document.createElement("code");
        code.textContent = fact.value;
        valueCell.append(code);
      }
      row.append(valueCell);

      // The origin names the unit where there is one (decision 066): the ledger's statement,
      // not a reading of the panel's title.
      const originCell = document.createElement("td");
      originCell.append(
        badge(
          ORIGIN_CLASSES[fact.origin] ?? "",
          fact.origin === FactOrigin.Source && fact.unit
            ? fact.unit
            : (FACT_ORIGIN_LABELS[fact.origin] ?? String(fact.origin)),
          FACT_ORIGIN_LABELS[fact.origin] ?? "",
        ),
      );
      row.append(originCell);

      const outcomeCell = document.createElement("td");
      outcomeCell.append(
        badge(
          OUTCOME_CLASSES[fact.outcome] ?? "",
          FACT_OUTCOME_LABELS[fact.outcome] ?? String(fact.outcome),
          OUTCOME_EXPLANATIONS[fact.outcome] ?? "",
        ),
      );
      row.append(outcomeCell);

      body.append(row);
    }
  }

  renderRows();

  /* ---- hover: a row lights its underlines, an underline lights its rows ------------ */

  const marksOf = (index) =>
    document.querySelectorAll(`#result mark.tok[data-link~="${index}"]`);

  let lit = [];
  light = (indexes) => {
    for (const element of lit) element.classList.remove("tok-on", "fact-on");
    lit = [];
    for (const index of indexes) {
      for (const mark of marksOf(index)) {
        mark.classList.add("tok-on");
        lit.push(mark);
      }
      const row = body.querySelector(`tr[data-fact="${index}"]`);
      if (row) {
        row.classList.add("fact-on");
        lit.push(row);
      }
    }
  };

  body.addEventListener("mouseover", (event) => {
    const row = event.target.closest("tr[data-fact]");
    if (row) light([row.dataset.fact]);
  });
  body.addEventListener("mouseleave", () => light([]));

  wireMarks();
}

/*
 * The lighting function of the band currently on the screen. The result section outlives
 * the band - every conversion renders a new one - so the section's own listeners are attached
 * once and call whatever band is current.
 */
let light = () => {};
let marksWired = false;

function wireMarks() {
  if (marksWired) return;
  marksWired = true;
  const result = document.getElementById("result");
  result.addEventListener("mouseover", (event) => {
    const mark = event.target.closest("mark.tok");
    if (mark) light(mark.dataset.link.split(" "));
  });
  result.addEventListener("mouseout", (event) => {
    if (event.target.closest("mark.tok")) light([]);
  });
}
