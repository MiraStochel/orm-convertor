/*
 * The interactive comparison (decision 100). A mockup: it calls nothing, converts nothing
 * and infers nothing. It reads two frozen files - the recorded run in `comparison-run.js`
 * and the authored links in `comparison-links.js` - renders the windows of an example side
 * by side, and lights up every place one fact shows itself when the reader points at any of
 * them. Finding those places is `comparison-marking.js`; this module is the screen.
 *
 * The marks are inserted *after* highlighting, into the text nodes highlight.js left behind,
 * which is why the recording normalizes its line endings: the offsets have to be counted
 * against the same text the browser ends up with.
 *
 * Nothing here is a claim about the tool: the page states what it displays and where the
 * displayed thing came from, and that is the whole of it.
 */

import { ORM_LABELS, CONTENT_TYPE_LABELS } from "./api.js";
import { renderCode, kindBadge, renderCatalogState } from "./ui.js";
import { markSegments, rangesIn, segmentsOf } from "./comparison-marking.js";
import { RUN } from "./comparison-run.js";
import { LINKS } from "./comparison-links.js";

/*
 * The page's own three-value vocabulary, deliberately not the tool's six. The tool records
 * what went wrong or what had to be filled in; it has no word for a fact that simply
 * travelled, because a translation that works records nothing. A comparison needs that
 * word, so the page has one - and where the tool did record something, its own record is
 * quoted beside it rather than translated into this vocabulary.
 */
const KINDS = Object.freeze({
  carried: {
    label: "carried",
    explanation:
      "Both sides state it. The output may spell it differently - an attribute becomes an element, a method call becomes a clause - but the fact is the same one.",
  },
  added: {
    label: "added",
    explanation:
      "Only the output states it: a convention of the target framework, something the target requires, or a fact the database catalog supplied.",
  },
  dropped: {
    label: "dropped",
    explanation:
      "Only the input states it. The target has no place for the fact, so nothing on the other side answers to it.",
  },
});

/* ---- rendering ---------------------------------------------------------- */

const plural = (count, word) => `${count} ${word}${count === 1 ? "" : "s"}`;

function kindTag(kind) {
  const view = KINDS[kind] ?? KINDS.carried;
  const tag = document.createElement("span");
  tag.className = `badge badge-link-${kind}`;
  tag.textContent = view.label;
  tag.title = view.explanation;
  return tag;
}

function renderLegend() {
  const legend = document.getElementById("kind-legend");
  for (const [kind, view] of Object.entries(KINDS)) {
    const term = document.createElement("dt");
    term.append(kindTag(kind));
    const description = document.createElement("dd");
    description.textContent = view.explanation;
    legend.append(term, description);
  }
}

function renderWindow(container, file, fileId, comparison, idOf, kindOf) {
  const template = document.getElementById("window-template");
  const element = template.content.firstElementChild.cloneNode(true);
  element.querySelector(".window-name").textContent = file.name;
  element.querySelector(".window-type").textContent = CONTENT_TYPE_LABELS[file.contentType] ?? "";
  element.querySelector(".window-side").textContent =
    file.side === "source" ? "source" : "generated";
  element.dataset.side = file.side;

  const code = element.querySelector("pre > code");
  renderCode(code, file.content, file.contentType);
  const ranges = rangesIn(comparison, fileId, file.content, idOf, (span) =>
    console.warn(`comparison: no match for ${JSON.stringify(span.find)} in ${fileId}`),
  );
  const marks = markSegments(code, segmentsOf(ranges));
  // The kind of the first link on the mark, so the underline of "show every connected word"
  // can be coloured before anything is pointed at.
  for (const mark of marks) mark.dataset.kind = kindOf(mark.dataset.link.split(" ")[0]);
  container.append(element);
  return marks;
}

/** One caption for the whole example, sticky above its comparisons. */
function renderCaption(caption, links, marksByLink) {
  caption.replaceChildren();
  if (links.length === 0) {
    caption.classList.remove("caption-active");
    const hint = document.createElement("span");
    hint.className = "caption-hint";
    hint.textContent =
      "Point at a word in any window - or at a line of a connections list - to see where it went.";
    caption.append(hint);
    return;
  }

  caption.classList.add("caption-active");
  for (const { link, id } of links) {
    const line = document.createElement("div");
    line.className = "caption-line";

    const head = document.createElement("div");
    head.className = "caption-head";
    head.append(kindTag(link.kind));
    const label = document.createElement("strong");
    label.textContent = link.label;
    head.append(label);

    const places = (marksByLink.get(id) ?? []).length;
    const files = new Set(
      (marksByLink.get(id) ?? []).map((mark) => mark.closest("article.window")),
    ).size;
    const count = document.createElement("small");
    count.className = "caption-count";
    count.textContent = `${plural(places, "place")} in ${plural(files, "window")}`;
    head.append(count);
    line.append(head);

    if (link.note) {
      const note = document.createElement("p");
      note.className = "caption-note";
      note.textContent = link.note;
      line.append(note);
    }

    if (link.record != null) {
      const record = link.record;
      const quote = document.createElement("p");
      quote.className = "caption-record";
      quote.append(kindBadge(record.kind));
      const reason = document.createElement("span");
      reason.textContent = record.reason ?? "";
      quote.append(" ", reason);
      line.append(quote);
    }

    caption.append(line);
  }
}

/** Scrolls each window that holds a highlighted mark so that the first one is visible. */
function revealMarks(marks) {
  const seen = new Set();
  for (const mark of marks) {
    const pre = mark.closest("pre");
    if (!pre || seen.has(pre)) continue;
    seen.add(pre);
    const box = pre.getBoundingClientRect();
    const spot = mark.getBoundingClientRect();
    if (spot.top < box.top || spot.bottom > box.bottom) {
      pre.scrollTop += spot.top - box.top - box.height / 3;
    }
  }
}

function renderExample(key) {
  const container = document.getElementById("example");
  container.replaceChildren();

  const run = RUN[key];
  const example = LINKS[key];
  if (!run || !example) {
    const problem = document.createElement("p");
    problem.className = "problem";
    problem.textContent = `Nothing was recorded for the example "${key}".`;
    container.append(problem);
    return;
  }

  const section = document.createElement("section");
  section.className = "example";
  container.append(section);

  const heading = document.createElement("h3");
  heading.textContent = example.title ?? key;
  section.append(heading);

  const facts = document.createElement("div");
  facts.className = "run-facts";
  const direction = document.createElement("span");
  direction.textContent =
    `${ORM_LABELS[run.sourceOrm]} ${run.sourceFrameworkVersion}` +
    ` → ${ORM_LABELS[run.targetOrm]} ${run.targetFrameworkVersion}`;
  facts.append(direction);
  const catalog = document.createElement("span");
  renderCatalogState(catalog, run.catalogState, run.catalogReadMilliseconds);
  facts.append(catalog);
  const recorded = document.createElement("span");
  const runId = document.createElement("code");
  runId.textContent = run.runId;
  recorded.append("recorded run ", runId);
  facts.append(recorded);
  section.append(facts);

  if (example.lede) {
    const lede = document.createElement("p");
    lede.className = "example-lede";
    lede.textContent = example.lede.replace(/\s+/g, " ").trim();
    section.append(lede);
  }

  const caption = document.createElement("div");
  caption.className = "caption";
  caption.setAttribute("role", "status");
  section.append(caption);

  const marksByLink = new Map();
  const linkById = new Map();

  example.comparisons.forEach((comparison, comparisonIndex) => {
    const idOf = (linkIndex) => `c${comparisonIndex}-l${linkIndex}`;
    comparison.links.forEach((link, linkIndex) => {
      const resolved = { ...link };
      if (link.record != null) resolved.record = run.records[link.record] ?? null;
      linkById.set(idOf(linkIndex), resolved);
    });

    const block = document.createElement("section");
    block.className = "comparison";

    const title = document.createElement("h4");
    title.textContent = `${comparisonIndex + 1}. ${comparison.title}`;
    block.append(title);

    if (comparison.note) {
      const note = document.createElement("p");
      note.className = "comparison-note";
      note.textContent = comparison.note.replace(/\s+/g, " ").trim();
      block.append(note);
    }

    const grid = document.createElement("div");
    grid.className = "comparison-grid";
    const left = document.createElement("div");
    left.className = "comparison-side";
    const right = document.createElement("div");
    right.className = "comparison-side";
    grid.append(left, right);
    block.append(grid);

    const collect = (marks) => {
      for (const mark of marks) {
        for (const id of mark.dataset.link.split(" ")) {
          if (!marksByLink.has(id)) marksByLink.set(id, []);
          marksByLink.get(id).push(mark);
        }
      }
    };
    const kindOf = (id) => linkById.get(id)?.kind ?? "carried";
    for (const fileId of comparison.source) {
      collect(renderWindow(left, run.files[fileId], fileId, comparison, idOf, kindOf));
    }
    for (const fileId of comparison.target) {
      collect(renderWindow(right, run.files[fileId], fileId, comparison, idOf, kindOf));
    }

    const list = document.createElement("details");
    list.className = "connections";
    const summary = document.createElement("summary");
    summary.textContent = `${plural(comparison.links.length, "connection")} in this comparison`;
    list.append(summary);
    const items = document.createElement("ul");
    comparison.links.forEach((link, linkIndex) => {
      const id = idOf(linkIndex);
      const item = document.createElement("li");
      const button = document.createElement("button");
      button.type = "button";
      button.className = "connection";
      button.dataset.link = id;
      button.append(kindTag(link.kind));
      const label = document.createElement("span");
      label.textContent = link.label;
      button.append(label);
      item.append(button);
      items.append(item);
    });
    list.append(items);
    block.append(list);

    section.append(block);
  });

  /* ---- what lights up what ---------------------------------------------- */

  let shown = [];
  let pinned = null;

  function show(ids) {
    for (const mark of shown) mark.classList.remove("tok-on");
    shown = [];
    const links = [];
    for (const id of ids) {
      const link = linkById.get(id);
      if (!link) continue;
      links.push({ link, id });
      // The colour of a mark stays the one it was given when it was made: a mark shared by
      // two links would otherwise keep the colour of whichever was pointed at last.
      for (const mark of marksByLink.get(id) ?? []) {
        mark.classList.add("tok-on");
        shown.push(mark);
      }
    }
    for (const button of section.querySelectorAll("button.connection")) {
      button.classList.toggle("connection-on", ids.includes(button.dataset.link));
    }
    renderCaption(caption, links, marksByLink);
    return shown;
  }

  const restore = () => show(pinned ?? []);
  const idsOf = (element) => (element.dataset.link ?? "").split(" ").filter(Boolean);

  section.addEventListener("mouseover", (event) => {
    const target = event.target.closest?.("mark.tok, button.connection");
    if (target) show(idsOf(target));
  });
  section.addEventListener("mouseout", (event) => {
    if (event.target.closest?.("mark.tok, button.connection")) restore();
  });
  section.addEventListener("focusin", (event) => {
    const target = event.target.closest?.("button.connection");
    if (target) show(idsOf(target));
  });
  section.addEventListener("click", (event) => {
    const target = event.target.closest?.("mark.tok, button.connection");
    if (!target) return;
    const ids = idsOf(target);
    if (pinned && pinned.join(" ") === ids.join(" ")) {
      pinned = null;
      restore();
      return;
    }
    pinned = ids;
    revealMarks(show(ids));
  });
  // One listener for the page, re-pointed at the example currently rendered; adding it per
  // example would leave a listener behind on every switch.
  unpin = () => {
    if (!pinned) return;
    pinned = null;
    restore();
  };

  show([]);
  document.getElementById("link-count").textContent =
    `${plural(linkById.size, "connection")} on this example, all of them written by hand.`;
}

/* ---- the picker --------------------------------------------------------- */

/** Unpins whatever the example on screen has pinned; replaced on every switch. */
let unpin = () => {};

function renderPicker() {
  const picker = document.getElementById("example-picker");
  const keys = Object.keys(LINKS);
  const buttons = new Map();

  const select = (key) => {
    for (const [otherKey, button] of buttons) {
      button.setAttribute("aria-pressed", String(otherKey === key));
    }
    renderExample(key);
    // The key is the anchor the explanatory page uses for the same example.
    history.replaceState(null, "", `#${key}`);
  };

  for (const key of keys) {
    const run = RUN[key];
    const button = document.createElement("button");
    button.type = "button";
    button.className = "example-button secondary";
    button.setAttribute("aria-pressed", "false");
    const title = document.createElement("strong");
    title.textContent = LINKS[key].title ?? key;
    const direction = document.createElement("small");
    direction.textContent = run
      ? `${ORM_LABELS[run.sourceOrm]} → ${ORM_LABELS[run.targetOrm]}`
      : key;
    button.append(title, direction);
    button.addEventListener("click", () => select(key));
    buttons.set(key, button);
    picker.append(button);
  }

  const wanted = decodeURIComponent(location.hash.replace("#", ""));
  select(keys.includes(wanted) ? wanted : keys[0]);
}

/*
 * Every connection points at literal text inside a frozen file, so a typo in the authored
 * data lights up nothing and looks exactly like a word nobody connected. The page therefore
 * resolves all of them on load - not only those of the example on screen - and says in the
 * console how many there are and how many found nothing. It costs a few milliseconds over
 * ninety-odd files and is the only check this page has; the frontend has no tests at all
 * (decision 032), and a mockup that claims nothing does not earn its first one.
 */
function selfCheck() {
  let connections = 0;
  let unresolved = 0;
  for (const [key, example] of Object.entries(LINKS)) {
    const run = RUN[key];
    if (!run) {
      console.warn(`comparison: no recorded run for "${key}"`);
      continue;
    }
    for (const comparison of example.comparisons) {
      connections += comparison.links.length;
      for (const fileId of [...comparison.source, ...comparison.target]) {
        const file = run.files[fileId];
        if (!file) {
          console.warn(`comparison: ${key} shows "${fileId}", which the run does not carry`);
          continue;
        }
        rangesIn(comparison, fileId, file.content, (index) => String(index), (span) => {
          unresolved++;
          console.warn(
            `comparison: ${key} / ${fileId} has no match for ${JSON.stringify(span.find)}`,
          );
        });
      }
    }
  }
  const summary =
    `interactive comparison: ${Object.keys(LINKS).length} examples,` +
    ` ${connections} connections, ${unresolved} unresolved span(s).`;
  if (unresolved === 0) console.info(summary);
  else console.warn(summary);
}

function init() {
  selfCheck();
  renderLegend();
  renderPicker();
  const toggle = document.getElementById("show-all");
  const apply = () => document.body.classList.toggle("show-all-links", toggle.checked);
  toggle.addEventListener("change", apply);
  apply();
  document.addEventListener("keydown", (event) => {
    if (event.key === "Escape") unpin();
  });
}

init();
