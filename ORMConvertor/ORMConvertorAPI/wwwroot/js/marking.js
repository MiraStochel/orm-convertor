/*
 * How ranges of a code window are turned into markable pieces, and how a literal text is
 * found in it, for the facts the translation screen shows (facts.js, decision 125): the
 * stated positions are moved to where the window shows them and become segments and marks,
 * and where a framework states no position the value is searched for. It lives apart from the rendering because it is the one part of the
 * page with an answer that can be right or wrong: everything here is a function of its
 * arguments.
 *
 * `markSegments` is the one function that touches the DOM, and only through `document`,
 * which a test can hand it.
 */

const WORD = /[A-Za-z0-9_]/;
const escapeForRegExp = (text) => text.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");

/**
 * Every occurrence of the literal `find` in `content`, as [start, end] pairs. Whole words
 * only where the text begins or ends with a word character, so `BookId` does not match
 * inside `BookIds` and `getBookId` is not the property. `ignoreCase` is for a value whose
 * spelling the target changes - `identity` in one framework, `Identity` in another.
 */
export function occurrences(content, find, { ignoreCase = false } = {}) {
  const prefix = WORD.test(find[0]) ? "(?<![A-Za-z0-9_])" : "";
  const suffix = WORD.test(find[find.length - 1]) ? "(?![A-Za-z0-9_])" : "";
  const pattern = new RegExp(prefix + escapeForRegExp(find) + suffix, ignoreCase ? "gi" : "g");
  const found = [];
  for (const match of content.matchAll(pattern)) {
    found.push([match.index, match.index + find.length]);
  }
  return found;
}

/**
 * Maps an offset of `content` to the same character of the code window that shows it. The
 * server counts its positions in the text as sent or generated, Windows line endings
 * included (decision 125); the window shows the text with LF line endings, because the
 * highlighter writes it through innerHTML and the HTML parser turns every CR LF into LF. Each
 * CR LF before an offset therefore puts it one character further on than it stands in the
 * window. A lone CR becomes LF and keeps its length.
 */
export function windowOffsets(content) {
  const dropped = [];
  for (let at = content.indexOf("\r\n"); at >= 0; at = content.indexOf("\r\n", at + 2)) {
    dropped.push(at);
  }
  if (dropped.length === 0) return (offset) => offset;
  return (offset) => {
    // The number of dropped CRs before the offset, by bisection over their ascending offsets.
    let low = 0;
    let high = dropped.length;
    while (low < high) {
      const middle = (low + high) >> 1;
      if (dropped[middle] < offset) low = middle + 1;
      else high = middle;
    }
    return offset - low;
  };
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
