// Reader text editor helpers: scroll sync + selection/caret operations.

export function syncScroll(textarea, highlight, gutter) {
  if (!textarea) return;
  const top = textarea.scrollTop;
  const left = textarea.scrollLeft;
  if (highlight) {
    highlight.scrollTop = top;
    highlight.scrollLeft = left;
  }
  if (gutter) {
    gutter.scrollTop = top;
  }
}

export function getSelection(textarea) {
  if (!textarea) return { start: 0, end: 0, text: "" };
  const start = textarea.selectionStart || 0;
  const end = textarea.selectionEnd || 0;
  return {
    start,
    end,
    text: (textarea.value || "").substring(start, end),
  };
}

export function setSelection(textarea, start, end) {
  if (!textarea) return;
  const len = (textarea.value || "").length;
  const s = Math.max(0, Math.min(start, len));
  const e = Math.max(0, Math.min(end, len));
  textarea.focus();
  textarea.setSelectionRange(s, e);
}

export function selectAll(textarea) {
  if (!textarea) return;
  textarea.focus();
  textarea.setSelectionRange(0, (textarea.value || "").length);
}

export function goToStart(textarea) {
  if (!textarea) return;
  textarea.focus();
  textarea.setSelectionRange(0, 0);
  textarea.scrollTop = 0;
}

export function goToEnd(textarea) {
  if (!textarea) return;
  const len = (textarea.value || "").length;
  textarea.focus();
  textarea.setSelectionRange(len, len);
  textarea.scrollTop = textarea.scrollHeight;
}

export function getCurrentLineRange(textarea) {
  if (!textarea) return { start: 0, end: 0, text: "" };
  const value = textarea.value || "";
  const pos = textarea.selectionStart || 0;
  let start = pos;
  while (start > 0 && value[start - 1] !== "\n") start--;
  let end = pos;
  while (end < value.length && value[end] !== "\n") end++;
  return { start, end, text: value.substring(start, end) };
}

export function replaceRange(textarea, start, end, replacement) {
  if (!textarea) return textarea ? textarea.value : "";
  const value = textarea.value || "";
  const before = value.substring(0, start);
  const after = value.substring(end);
  const next = before + (replacement ?? "") + after;
  textarea.value = next;
  const caret = before.length + (replacement ? replacement.length : 0);
  textarea.focus();
  textarea.setSelectionRange(caret, caret);
  textarea.dispatchEvent(new Event("input", { bubbles: true }));
  return next;
}

export function insertText(textarea, text) {
  if (!textarea) return "";
  const start = textarea.selectionStart || 0;
  const end = textarea.selectionEnd || 0;
  return replaceRange(textarea, start, end, text ?? "");
}

export function setOverlay(highlight, gutter, html, lineNumbers) {
  if (highlight) highlight.innerHTML = html || "";
  if (gutter) gutter.textContent = lineNumbers || "1";
}

export function setTextareaValue(textarea, text) {
  if (!textarea) return;
  const next = text ?? "";
  if (textarea.value !== next) {
    textarea.value = next;
  }
}
