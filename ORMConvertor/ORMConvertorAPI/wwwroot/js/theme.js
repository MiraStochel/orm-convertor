/*
 * The colour-theme control shared by all four documents: system (the default), light or
 * dark, written to Pico's `data-theme` on <html> and remembered per browser.
 *
 * Two halves, and the split is the point. The stored theme is applied by a small inline
 * script in each <head>, because a module script is deferred and restoring the theme here
 * would repaint a page the reader can already see. This module owns the rest: the control
 * in the header, the write, and keeping highlight.js's dark stylesheet in step - it is
 * media-switched on `prefers-color-scheme`, which an explicit choice has to override.
 */

const STORAGE_KEY = "ormconvertor.theme";
const THEMES = ["system", "light", "dark"];

function readTheme() {
  try {
    const stored = localStorage.getItem(STORAGE_KEY);
    return THEMES.includes(stored) ? stored : "system";
  } catch {
    // Private windows and blocked site data throw on access, not on write.
    return "system";
  }
}

function applyTheme(theme) {
  const root = document.documentElement;
  if (theme === "system") delete root.dataset.theme;
  else root.dataset.theme = theme;

  const darkHighlighting = document.getElementById("hljs-dark");
  if (darkHighlighting) {
    darkHighlighting.media =
      theme === "dark" ? "all" : theme === "light" ? "not all" : "(prefers-color-scheme: dark)";
  }
}

const select = document.getElementById("theme");
const theme = readTheme();
applyTheme(theme);

if (select) {
  select.value = theme;
  select.addEventListener("change", () => {
    applyTheme(select.value);
    try {
      localStorage.setItem(STORAGE_KEY, select.value);
    } catch {
      /* Remembering the choice is a convenience; the page works without it. */
    }
  });
}
