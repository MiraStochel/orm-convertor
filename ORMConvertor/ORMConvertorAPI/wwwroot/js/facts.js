/*
 * The facts band of the translation screen (decisions 124 and 125): one row per mapping fact
 * the server's ledger carries - entity, property, category, value, where it came from and
 * what became of it - and, over the code panels, an underline wherever the fact stands.
 *
 * Two kinds of underline meet here and the page keeps them apart:
 *
 *   - a position the server stated (`source`, `targets`): the reader placed the fact in the
 *     unit, the builder marked it in the artifact. Solid underline, no caption - it is the
 *     tool's own word and needs no qualifier;
 *   - a place this page found by searching for the fact's literal value, where the server
 *     stated no position (a reader or builder that marks nothing - the layer is optional for
 *     a framework, decision 125). Dotted underline, and the title and the row say "text
 *     search", because it finds a value spelled the same on both sides and misses one the
 *     target respells.
 *
 * marking.js finds a literal value and slices the marked ranges into segments; what to mark
 * is decided here, from the facts the server states.
 */

import { occurrences, segmentsOf, markSegments } from "./marking.js";
import {
  FactOrigin,
  FACT_ORIGIN_LABELS,
  FactOutcome,
  FACT_OUTCOME_LABELS,
  MappingFactCategory,
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

const SEARCH_TITLE = "Found by text search for the fact's value - not a position the tool stated.";

/* ---- which fact gets which colour --------------------------------------- */

/*
 * Three words for what happened: a fact that travelled from the input is carried, one the
 * output states without the input having said it is added, one that did not reach the
 * output is dropped.
 */
function kindOf(fact) {
  if (fact.outcome !== FactOutcome.Carried) return "dropped";
  return fact.origin === FactOrigin.Source ? "carried" : "added";
}

/*
 * The literal texts to search for where the server stated no position. A value with several
 * parts - the columns of a foreign key, precision beside scale - is searched part by part; a
 * one-character value would underline every digit on the screen, so it is not searched at
 * all; nullability is a word of the ledger, not of either text.
 */
function tokensOf(fact) {
  if (fact.category === MappingFactCategory.Nullability) return [];
  const value = fact.value ?? fact.property;
  if (!value) return [];
  return value
    .split(", ")
    .map((part) => part.trim())
    .filter((part) => part.length >= 2);
}

/* ---- the underlines ----------------------------------------------------- */

/*
 * Collects the ranges to underline, per code element: the stated positions first, the text
 * search only where a side has none. Returns the map of code element to ranges, and the set
 * of fact indexes whose underlines came from searching.
 */
function rangesOf(facts, { inputPanels, artifactPanels }) {
  const ranges = new Map();
  const searched = new Set();

  const add = (panel, start, end, index, stated) => {
    const code = panel?.querySelector("pre > code");
    if (!code) return;
    if (end > code.textContent.length) return;
    if (!ranges.has(code)) ranges.set(code, []);
    ranges.get(code).push({ start, end, link: String(index), stated });
  };

  const search = (panel, index, tokens) => {
    const code = panel?.querySelector("pre > code");
    if (!code) return;
    for (const token of tokens) {
      for (const [start, end] of occurrences(code.textContent, token, { ignoreCase: true })) {
        add(panel, start, end, index, false);
        searched.add(String(index));
      }
    }
  };

  facts.forEach((fact, index) => {
    const tokens = tokensOf(fact);

    // The input side: the unit that stated the fact.
    if (fact.origin === FactOrigin.Source && fact.unit) {
      const panel = inputPanels.get(fact.unit);
      if (fact.source) {
        add(panel, fact.source.start, fact.source.start + fact.source.length, index, true);
      } else if (tokens.length > 0) {
        search(panel, index, tokens);
      }
    }

    // The output side: the artifacts of the fact's entity.
    const own = artifactPanels.filter((panel) => panel.dataset.entity === fact.entity);
    if (fact.targets && fact.targets.length > 0) {
      for (const target of fact.targets) {
        const panel = own.find((p) => Number(p.dataset.contentType) === target.artifact);
        add(panel, target.start, target.start + target.length, index, true);
      }
    } else if (fact.outcome === FactOutcome.Carried && tokens.length > 0) {
      for (const panel of own) search(panel, index, tokens);
    }
  });

  return { ranges, searched };
}

/*
 * Underlines every range in two passes per panel: the stated positions first, then the
 * search results that touch none of them - a search result inside a stated position would
 * only nest one mark in another and say less than the stated one already does. The second
 * pass's marks carry the search flag and its title.
 */
function markFacts(facts, panels) {
  const { ranges, searched } = rangesOf(facts, panels);

  for (const [code, found] of ranges) {
    const stated = found.filter((r) => r.stated);
    const fromSearch = found.filter(
      (r) => !r.stated && !stated.some((s) => r.start < s.end && r.end > s.start),
    );

    for (const mark of markSegments(code, segmentsOf(stated))) {
      mark.dataset.kind = kindOf(facts[Number(mark.dataset.link.split(" ")[0])]);
    }

    for (const mark of markSegments(code, segmentsOf(fromSearch))) {
      mark.dataset.kind = kindOf(facts[Number(mark.dataset.link.split(" ")[0])]);
      mark.dataset.search = "";
      mark.title = SEARCH_TITLE;
    }
  }

  return searched;
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
 * server generated it for in `data-entity` and its content type in `data-content-type`.
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

  const searched = markFacts(facts, { inputPanels, artifactPanels });

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

      // The value, and - only where this page searched for it instead of being told where it
      // stands - the word that says so. A stated position needs no caption (decision 125).
      const valueCell = document.createElement("td");
      if (fact.value != null) {
        const code = document.createElement("code");
        code.textContent = fact.value;
        valueCell.append(code);
      }
      if (searched.has(String(index))) {
        const note = document.createElement("small");
        note.className = "search-note";
        note.textContent = " text search";
        note.title = SEARCH_TITLE;
        valueCell.append(note);
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
