/*
 * Input beside output, each column a window of its own (css: .compare .pane-stack): pointing
 * at something in one window moves the other to what it is linked with. Two links exist and
 * both are the server's: an underlined fact and the same fact on the other side (the marks
 * of facts.js share an index in data-link), and a panel and the panel it came from or went
 * into (the names in its "from" / "went into" line carry the panel's id in data-panel).
 *
 * The module wires itself on import and reads only the document, so every page with a
 * .compare gets the behaviour. Where the columns stand under each other - a narrow screen -
 * the stacks are not windows and nothing moves.
 */

// Long enough that a pointer crossing a panel on its way elsewhere moves nothing.
const DELAY_MS = 120;

const isWindow = (stack) => getComputedStyle(stack).overflowY === "auto";

const scrollBehavior = () =>
  matchMedia("(prefers-reduced-motion: reduce)").matches ? "auto" : "smooth";

/*
 * What the pointed-at element is linked with in the other window: the fact under the pointer
 * first, the panel otherwise. `from` is what the pointer is on, `to` what the other window
 * should show.
 */
function linkOf(element) {
  const own = element.closest(".pane-stack");
  const compare = own?.closest(".compare");
  if (!compare) return null;
  const other = [...compare.querySelectorAll(".pane-stack")].find((stack) => stack !== own);
  if (!other || !isWindow(own) || !isWindow(other)) return null;

  const mark = element.closest("mark.tok");
  if (mark) {
    for (const index of mark.dataset.link.split(" ")) {
      const to = other.querySelector(`mark.tok[data-link~="${index}"]`);
      if (to) return { from: mark, to, own, other };
    }
  }

  const panel = element.closest("article.artifact");
  const id = panel?.querySelector(".artifact-origin [data-panel]")?.dataset.panel;
  const to = id ? document.getElementById(id) : null;
  return to && other.contains(to) ? { from: panel, to, own, other } : null;
}

/*
 * Moves the other window so that the linked place stands at the height of the pointed-at
 * one - the eye crosses over on one line. A panel taller than its window is anchored at the
 * window's top edge. The target is absolute, so a move begun while another still runs ends
 * where it should.
 */
function follow({ from, to, own, other }) {
  const behavior = scrollBehavior();
  const ownTop = own.getBoundingClientRect().top;
  const otherTop = other.getBoundingClientRect().top;
  const height = Math.max(from.getBoundingClientRect().top - ownTop, 0);
  const top = other.scrollTop + (to.getBoundingClientRect().top - otherTop) - height;
  other.scrollTo({ top, behavior });

  // A panel scrolls sideways by itself, and a fact far right in a long line would stay out
  // of sight.
  const pre = to.matches("mark") ? to.closest("pre") : null;
  if (!pre) return;
  const box = pre.getBoundingClientRect();
  const place = to.getBoundingClientRect();
  if (place.left < box.left || place.right > box.right) {
    pre.scrollTo({ left: pre.scrollLeft + (place.left - box.left) - 24, behavior });
  }
}

// What the other window last followed. Moving inside the same fact or the same panel moves
// nothing again, so a reader who then scrolls the other window by hand keeps their place.
let followed = null;
let pending = 0;

document.addEventListener("mouseover", (event) => {
  if (!(event.target instanceof Element)) return;
  const link = linkOf(event.target);
  // Off a fact and back on the panel's plain text: the window stays where the fact put it.
  if (!link || link.from === followed || link.from.contains(followed)) return;
  followed = link.from;
  clearTimeout(pending);
  pending = setTimeout(() => {
    if (link.from.isConnected && link.to.isConnected) follow(link);
  }, DELAY_MS);
});

// Leaving a window forgets what was followed: pointing at the same place again moves the
// other window again.
document.addEventListener("mouseout", (event) => {
  if (!(event.target instanceof Element)) return;
  const stack = event.target.closest(".compare .pane-stack");
  if (!stack || (event.relatedTarget instanceof Node && stack.contains(event.relatedTarget))) return;
  followed = null;
  clearTimeout(pending);
});
