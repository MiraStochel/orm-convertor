/*
 * How the interactive comparison finds the authored spans in a file and turns them into
 * markable pieces (decision 100). It is the only part of that page with an answer that can
 * be right or wrong, so it lives apart from the rendering: everything here is a function of
 * its arguments, and the offline check of the authored data runs exactly this code rather
 * than a second copy of it.
 *
 * `markSegments` is the one function that touches the DOM, and only through `document`,
 * which a test can hand it.
 */

const WORD = /[A-Za-z0-9_]/;
const escapeForRegExp = (text) => text.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");

/**
 * Every occurrence of the literal `find` in `content`, as [start, end] pairs. Whole words
 * only where the text begins or ends with a word character, so `BookId` does not match
 * inside `BookIds` and `getBookId` is not the property.
 */
export function occurrences(content, find) {
  const prefix = WORD.test(find[0]) ? "(?<![A-Za-z0-9_])" : "";
  const suffix = WORD.test(find[find.length - 1]) ? "(?![A-Za-z0-9_])" : "";
  const pattern = new RegExp(prefix + escapeForRegExp(find) + suffix, "g");
  const found = [];
  for (const match of content.matchAll(pattern)) {
    found.push([match.index, match.index + find.length]);
  }
  return found;
}

/** The spans of one link, with the token shorthand expanded into one span per file. */
export function spansOf(link) {
  const spans = [];
  if (link.token) {
    for (const file of link.files ?? []) spans.push({ file, find: link.token, all: true });
  }
  spans.push(...(link.spans ?? []));
  return spans;
}

/**
 * The ranges of one file of one comparison, each tagged with the id of the link that put it
 * there. `onMiss` is called for a span that matches nothing - authored data against a frozen
 * file, so that is a mistake in the data rather than a state to show the reader.
 */
export function rangesIn(comparison, fileId, content, idOf, onMiss) {
  const ranges = [];
  comparison.links.forEach((link, index) => {
    for (const span of spansOf(link)) {
      if (span.file !== fileId) continue;
      const found = occurrences(content, span.find);
      if (found.length === 0) {
        onMiss?.(span, fileId);
        continue;
      }
      const chosen = span.all ? found : [found[(span.nth ?? 1) - 1]];
      for (const range of chosen) {
        if (range) ranges.push({ start: range[0], end: range[1], link: idOf(index) });
      }
    }
  });
  return ranges;
}

/**
 * Slices overlapping ranges into disjoint segments, each keeping every link that covers it.
 * Two facts may share a word - the column name inside the key element of a collection, say -
 * and the reader should get both, not whichever was authored first.
 */
export function segmentsOf(ranges) {
  const boundaries = [...new Set(ranges.flatMap((range) => [range.start, range.end]))].sort(
    (a, b) => a - b,
  );
  const segments = [];
  for (let index = 0; index < boundaries.length - 1; index++) {
    const start = boundaries[index];
    const end = boundaries[index + 1];
    const links = [
      ...new Set(
        ranges.filter((range) => range.start <= start && range.end >= end).map((r) => r.link),
      ),
    ];
    if (links.length > 0) segments.push({ start, end, links });
  }
  return segments;
}

/**
 * Wraps each segment in a <mark>, walking the text nodes the syntax highlighter left behind.
 * A segment may straddle the boundary between two of them - a highlighted keyword next to
 * plain text - and then becomes one mark per piece, all carrying the same links.
 *
 * Both loops run backwards: splitting a text node at a later offset leaves the earlier
 * offsets of that same node untouched, so no position ever has to be recomputed.
 */
export function markSegments(codeElement, segments, view = globalThis.document) {
  const nodes = [];
  const walker = view.createTreeWalker(codeElement, 4 /* NodeFilter.SHOW_TEXT */);
  let offset = 0;
  for (let node = walker.nextNode(); node; node = walker.nextNode()) {
    nodes.push({ node, start: offset, end: offset + node.data.length });
    offset += node.data.length;
  }

  const marks = [];
  for (let index = nodes.length - 1; index >= 0; index--) {
    const { node, start, end } = nodes[index];
    const hits = segments
      .filter((segment) => segment.start < end && segment.end > start)
      .sort((a, b) => a.start - b.start);
    for (let hit = hits.length - 1; hit >= 0; hit--) {
      const segment = hits[hit];
      const from = Math.max(segment.start, start) - start;
      const to = Math.min(segment.end, end) - start;
      if (to <= from) continue;
      node.splitText(to);
      const middle = node.splitText(from);
      const mark = view.createElement("mark");
      mark.className = "tok";
      mark.dataset.link = segment.links.join(" ");
      middle.replaceWith(mark);
      mark.append(middle);
      marks.push(mark);
    }
  }
  return marks;
}
