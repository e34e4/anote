// Reader text editor helpers: scroll sync, selection, overlays, find/goto.

export function syncScroll(textarea, highlight, gutter, activeLine) {
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
  if (activeLine) {
    updateActiveLine(textarea, activeLine);
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
  if (!textarea) return { start: 0, end: 0, text: "", lineNumber: 1 };
  const value = textarea.value || "";
  const pos = textarea.selectionStart || 0;
  let start = pos;
  while (start > 0 && value[start - 1] !== "\n") start--;
  let end = pos;
  while (end < value.length && value[end] !== "\n") end++;
  let lineNumber = 1;
  for (let i = 0; i < start; i++) {
    if (value[i] === "\n") lineNumber++;
  }
  return { start, end, text: value.substring(start, end), lineNumber };
}

export function replaceRange(textarea, start, end, replacement) {
  if (!textarea) return "";
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

export function getCaret(textarea) {
  if (!textarea) return { start: 0, end: 0, scrollTop: 0 };
  return {
    start: textarea.selectionStart || 0,
    end: textarea.selectionEnd || 0,
    scrollTop: textarea.scrollTop || 0,
  };
}

export function setCaret(textarea, start, end, scrollTop) {
  if (!textarea) return;
  const len = (textarea.value || "").length;
  const s = Math.max(0, Math.min(start || 0, len));
  const e = Math.max(0, Math.min(end == null ? s : end, len));
  textarea.focus();
  textarea.setSelectionRange(s, e);
  if (scrollTop != null && !Number.isNaN(scrollTop)) {
    textarea.scrollTop = scrollTop;
  }
  updateActiveLine(textarea, textarea._rteActiveLine || null);
}

const listenerState = new WeakMap();

export function attachEditorListeners(textarea, highlight, gutter, activeLine) {
  if (!textarea || listenerState.has(textarea)) return;
  textarea._rteActiveLine = activeLine;
  const onScroll = () => syncScroll(textarea, highlight, gutter, activeLine);
  const onCaret = () => updateActiveLine(textarea, activeLine);
  textarea.addEventListener("scroll", onScroll, { passive: true });
  textarea.addEventListener("keyup", onCaret);
  textarea.addEventListener("mouseup", onCaret);
  textarea.addEventListener("select", onCaret);
  textarea.addEventListener("click", onCaret);
  textarea.addEventListener("input", onCaret);
  listenerState.set(textarea, { onScroll, onCaret, highlight, gutter, activeLine });
}

export function moveCaret(textarea, direction) {
  if (!textarea) return;
  const value = textarea.value || "";
  let pos = textarea.selectionStart || 0;
  const line = getCurrentLineRange(textarea);
  const col = pos - line.start;

  if (direction === "left") {
    pos = Math.max(0, pos - 1);
  } else if (direction === "right") {
    pos = Math.min(value.length, pos + 1);
  } else if (direction === "up") {
    if (line.start === 0) {
      pos = 0;
    } else {
      let prevEnd = line.start - 1;
      let prevStart = prevEnd;
      while (prevStart > 0 && value[prevStart - 1] !== "\n") prevStart--;
      pos = prevStart + Math.min(col, Math.max(0, prevEnd - prevStart));
    }
  } else if (direction === "down") {
    let nextStart = line.end < value.length && value[line.end] === "\n" ? line.end + 1 : line.end;
    if (nextStart >= value.length) {
      pos = value.length;
    } else {
      let nextEnd = nextStart;
      while (nextEnd < value.length && value[nextEnd] !== "\n") nextEnd++;
      pos = nextStart + Math.min(col, Math.max(0, nextEnd - nextStart));
    }
  }

  textarea.focus();
  textarea.setSelectionRange(pos, pos);
  const style = window.getComputedStyle(textarea);
  const lineHeight = parseFloat(style.lineHeight) || parseFloat(style.fontSize) * 1.45;
  let lineIndex = 0;
  for (let i = 0; i < pos && i < value.length; i++) {
    if (value[i] === "\n") lineIndex++;
  }
  const targetTop = lineIndex * lineHeight;
  if (targetTop < textarea.scrollTop) {
    textarea.scrollTop = targetTop;
  } else if (targetTop + lineHeight > textarea.scrollTop + textarea.clientHeight) {
    textarea.scrollTop = targetTop + lineHeight - textarea.clientHeight;
  }
  updateActiveLine(textarea, textarea._rteActiveLine || null);
}

export function goToLineEnd(textarea) {
  if (!textarea) return;
  const line = getCurrentLineRange(textarea);
  textarea.focus();
  textarea.setSelectionRange(line.end, line.end);
  updateActiveLine(textarea, textarea._rteActiveLine || null);
}

export function updateActiveLine(textarea, activeLineEl) {
  if (!textarea || !activeLineEl) return;
  const style = window.getComputedStyle(textarea);
  const lineHeight = parseFloat(style.lineHeight) || parseFloat(style.fontSize) * 1.45;
  const paddingTop = parseFloat(style.paddingTop) || 0;
  const value = textarea.value || "";
  const pos = textarea.selectionStart || 0;
  let lineIndex = 0;
  for (let i = 0; i < pos && i < value.length; i++) {
    if (value[i] === "\n") lineIndex++;
  }
  const top = paddingTop + lineIndex * lineHeight - textarea.scrollTop;
  activeLineEl.style.height = lineHeight + "px";
  activeLineEl.style.transform = "translateY(" + top + "px)";
  activeLineEl.style.display = "block";
}

export function goToLine(textarea, lineNumber) {
  if (!textarea) return { start: 0, end: 0, lineNumber: 1 };
  const value = textarea.value || "";
  const target = Math.max(1, Math.floor(Number(lineNumber) || 1));
  let line = 1;
  let start = 0;
  for (let i = 0; i < value.length; i++) {
    if (line === target) {
      start = i;
      break;
    }
    if (value[i] === "\n") line++;
    if (i === value.length - 1 && line < target) {
      start = value.length;
      line = line + (value.endsWith("\n") ? 0 : 0);
    }
  }
  if (target === 1) start = 0;
  else {
    // Recompute start for target line.
    line = 1;
    start = 0;
    for (let i = 0; i < value.length; i++) {
      if (line === target) {
        start = i;
        break;
      }
      if (value[i] === "\n") {
        line++;
        if (line === target) {
          start = i + 1;
          break;
        }
      }
      if (i === value.length - 1) start = value.length;
    }
  }
  let end = start;
  while (end < value.length && value[end] !== "\n") end++;
  textarea.focus();
  textarea.setSelectionRange(start, start);
  const style = window.getComputedStyle(textarea);
  const lineHeight = parseFloat(style.lineHeight) || parseFloat(style.fontSize) * 1.45;
  textarea.scrollTop = Math.max(0, (target - 1) * lineHeight - textarea.clientHeight / 3);
  return { start, end, lineNumber: target, text: value.substring(start, end) };
}

export function findNext(textarea, query, useRegex, fromSelectionEnd) {
  if (!textarea || query == null || query === "") {
    return { found: false, start: 0, end: 0 };
  }
  const value = textarea.value || "";
  const from = fromSelectionEnd
    ? (textarea.selectionEnd || 0)
    : (textarea.selectionStart || 0);
  let matchStart = -1;
  let matchEnd = -1;

  if (useRegex) {
    let re;
    try {
      re = new RegExp(query, "gm");
    } catch {
      return { found: false, start: 0, end: 0, error: "invalid_regex" };
    }
    re.lastIndex = from;
    let m = re.exec(value);
    if (!m && from > 0) {
      re.lastIndex = 0;
      m = re.exec(value);
      if (m && m.index >= from) m = null;
    }
    if (m) {
      matchStart = m.index;
      matchEnd = m.index + m[0].length;
    }
  } else {
    let idx = value.indexOf(query, from);
    if (idx < 0 && from > 0) idx = value.indexOf(query, 0);
    if (idx >= 0) {
      matchStart = idx;
      matchEnd = idx + query.length;
    }
  }

  if (matchStart < 0) return { found: false, start: 0, end: 0 };
  textarea.focus();
  textarea.setSelectionRange(matchStart, matchEnd);
  const before = value.substring(0, matchStart);
  const line = before.split("\n").length;
  const style = window.getComputedStyle(textarea);
  const lineHeight = parseFloat(style.lineHeight) || parseFloat(style.fontSize) * 1.45;
  textarea.scrollTop = Math.max(0, (line - 1) * lineHeight - textarea.clientHeight / 3);
  return { found: true, start: matchStart, end: matchEnd };
}

export function replaceOne(textarea, query, replacement, useRegex) {
  if (!textarea || query == null || query === "") {
    return { changed: false, value: textarea ? textarea.value : "", count: 0 };
  }
  const value = textarea.value || "";
  const start = textarea.selectionStart || 0;
  const end = textarea.selectionEnd || 0;
  const selected = value.substring(start, end);
  let matchesSelection = false;

  if (useRegex) {
    try {
      const re = new RegExp(query, "g");
      matchesSelection = re.test(selected) && selected.match(new RegExp("^" + query + "$"));
      // Prefer replacing current selection if it matches the pattern fully.
      const full = new RegExp("^(?:" + query + ")$");
      matchesSelection = full.test(selected);
    } catch {
      return { changed: false, value, count: 0, error: "invalid_regex" };
    }
  } else {
    matchesSelection = selected === query;
  }

  if (matchesSelection) {
    let repl = replacement ?? "";
    if (useRegex) {
      try {
        repl = selected.replace(new RegExp(query), replacement ?? "");
      } catch {
        return { changed: false, value, count: 0, error: "invalid_regex" };
      }
    }
    const next = replaceRange(textarea, start, end, repl);
    return { changed: true, value: next, count: 1 };
  }

  const found = findNext(textarea, query, useRegex, false);
  if (!found.found) return { changed: false, value, count: 0 };
  let repl = replacement ?? "";
  if (useRegex) {
    try {
      const matched = value.substring(found.start, found.end);
      repl = matched.replace(new RegExp(query), replacement ?? "");
    } catch {
      return { changed: false, value, count: 0, error: "invalid_regex" };
    }
  }
  const next = replaceRange(textarea, found.start, found.end, repl);
  return { changed: true, value: next, count: 1 };
}

export function replaceAll(textarea, query, replacement, useRegex) {
  if (!textarea || query == null || query === "") {
    return { changed: false, value: textarea ? textarea.value : "", count: 0 };
  }
  const value = textarea.value || "";
  let next = value;
  let count = 0;
  if (useRegex) {
    try {
      const re = new RegExp(query, "g");
      next = value.replace(re, () => {
        count++;
        return replacement ?? "";
      });
    } catch {
      return { changed: false, value, count: 0, error: "invalid_regex" };
    }
  } else {
    if (query.length === 0) return { changed: false, value, count: 0 };
    let idx = 0;
    let out = "";
    while (idx < value.length) {
      const found = value.indexOf(query, idx);
      if (found < 0) {
        out += value.substring(idx);
        break;
      }
      out += value.substring(idx, found) + (replacement ?? "");
      idx = found + query.length;
      count++;
    }
    next = out;
  }
  if (count === 0) return { changed: false, value, count: 0 };
  textarea.value = next;
  textarea.focus();
  textarea.setSelectionRange(0, 0);
  textarea.dispatchEvent(new Event("input", { bubbles: true }));
  return { changed: true, value: next, count };
}

export function duplicateLine(textarea) {
  if (!textarea) return "";
  const line = getCurrentLineRange(textarea);
  const value = textarea.value || "";
  if (line.end >= value.length) {
    return replaceRange(textarea, line.end, line.end, "\n" + line.text);
  }
  const insertAt = value[line.end] === "\n" ? line.end + 1 : line.end;
  const insertion = line.text + "\n";
  return replaceRange(textarea, insertAt, insertAt, insertion);
}
