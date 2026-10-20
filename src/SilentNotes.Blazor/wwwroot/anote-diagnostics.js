/* Captures JS / Blazor errors for logcat and auto-reloads on known WebView desync. */
(function () {
	var TAG = "[aNoteDiag]";
	var RENDER_BATCH_MARKER = "unexpected acknowledgement for render batch";
	var RELOAD_COUNT_KEY = "anote_rb_autoreload_count";
	var RELOAD_TS_KEY = "anote_rb_autoreload_ts";
	var MAX_AUTO_RELOADS = 2;
	var AUTO_RELOAD_WINDOW_MS = 5 * 60 * 1000;
	var lastError = null;
	var enriched = false;
	var reloadScheduled = false;

	function stringifyArg(arg) {
		if (arg == null)
			return String(arg);
		if (typeof arg === "string")
			return arg;
		if (arg instanceof Error)
			return (arg.message || "") + (arg.stack ? "\n" + arg.stack : "");
		try {
			return JSON.stringify(arg);
		} catch (e) {
			return String(arg);
		}
	}

	function isRenderBatchDesync(text) {
		return !!text && text.toLowerCase().indexOf(RENDER_BATCH_MARKER) !== -1;
	}

	function isErrorUiVisible() {
		var el = document.getElementById("blazor-error-ui");
		if (!el)
			return false;
		try {
			var style = window.getComputedStyle(el);
			return style && style.display !== "none";
		} catch (e) {
			return el.style && el.style.display !== "none";
		}
	}

	function capture(kind, detail) {
		var text = kind + ": " + detail;
		if (text.length > 4000)
			text = text.substring(0, 4000) + "…";
		lastError = text;
		try {
			console.error(TAG, text);
		} catch (e) { }
		enrichErrorUi();
		tryAutoReload();
	}

	function enrichErrorUi() {
		var el = document.getElementById("blazor-error-ui");
		if (!el || !lastError || !isErrorUiVisible())
			return;
		if (enriched)
			return;
		enriched = true;
		var detail = document.createElement("div");
		detail.setAttribute("data-anote-diag", "1");
		detail.style.cssText = "font-size:12px;margin-top:6px;opacity:0.95;word-break:break-word;max-height:7em;overflow:auto;white-space:pre-wrap;";
		detail.textContent = lastError;
		el.appendChild(detail);
	}

	function tryAutoReload() {
		if (reloadScheduled)
			return;
		if (!isRenderBatchDesync(lastError))
			return;

		var now = Date.now();
		var lastTs = parseInt(sessionStorage.getItem(RELOAD_TS_KEY) || "0", 10) || 0;
		var count = parseInt(sessionStorage.getItem(RELOAD_COUNT_KEY) || "0", 10) || 0;
		if (now - lastTs > AUTO_RELOAD_WINDOW_MS)
			count = 0;

		if (count >= MAX_AUTO_RELOADS) {
			try {
				console.warn(TAG, "Render batch desync: auto-reload limit reached, leaving error UI");
			} catch (e) { }
			enrichErrorUi();
			return;
		}

		reloadScheduled = true;
		sessionStorage.setItem(RELOAD_COUNT_KEY, String(count + 1));
		sessionStorage.setItem(RELOAD_TS_KEY, String(now));

		var attempt = count + 1;
		try {
			console.warn(TAG, "Render batch desync: auto-reloading (attempt " + attempt + "/" + MAX_AUTO_RELOADS + ")");
		} catch (e) { }

		var el = document.getElementById("blazor-error-ui");
		if (el && isErrorUiVisible()) {
			var status = document.createElement("div");
			status.setAttribute("data-anote-diag-status", "1");
			status.style.cssText = "font-size:12px;margin-top:4px;opacity:0.9;";
			status.textContent = "Recovering automatically… (" + attempt + "/" + MAX_AUTO_RELOADS + ")";
			el.appendChild(status);
		}

		setTimeout(function () {
			var link = document.querySelector("#blazor-error-ui a.reload");
			if (link)
				link.click();
			else
				location.reload();
		}, 300);
	}

	var origError = console.error;
	console.error = function () {
		try {
			var msg = Array.prototype.map.call(arguments, stringifyArg).join(" ");
			if (msg.indexOf(TAG) === -1) {
				lastError = msg.length > 4000 ? msg.substring(0, 4000) + "…" : msg;
				enrichErrorUi();
				tryAutoReload();
			}
		} catch (e) { }
		return origError.apply(console, arguments);
	};

	window.addEventListener("error", function (e) {
		var stack = e.error && e.error.stack ? e.error.stack : "";
		capture("window.error", (e.message || "") + " @ " + (e.filename || "") + ":" + (e.lineno || 0) + (stack ? "\n" + stack : ""));
	});

	window.addEventListener("unhandledrejection", function (e) {
		var reason = e.reason;
		capture("unhandledrejection", reason && reason.stack ? reason.stack : stringifyArg(reason));
	});

	var el = document.getElementById("blazor-error-ui");
	if (el && typeof MutationObserver !== "undefined") {
		new MutationObserver(function () {
			enrichErrorUi();
			tryAutoReload();
		}).observe(el, { attributes: true, attributeFilter: ["style", "class"], childList: true });
	}

	try {
		console.info(TAG, "diagnostics script loaded (auto-reload on render-batch desync)");
	} catch (e) { }
})();
