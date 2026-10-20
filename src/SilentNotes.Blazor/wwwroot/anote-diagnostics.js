/* Captures JS / Blazor errors for logcat (via WebView console) and enriches #blazor-error-ui. */
(function () {
	var TAG = "[aNoteDiag]";
	var lastError = null;
	var enriched = false;

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

	function capture(kind, detail) {
		var text = kind + ": " + detail;
		if (text.length > 4000)
			text = text.substring(0, 4000) + "…";
		lastError = text;
		try {
			console.error(TAG, text);
		} catch (e) { }
		enrichErrorUi();
	}

	function enrichErrorUi() {
		var el = document.getElementById("blazor-error-ui");
		if (!el || !lastError)
			return;
		var visible = false;
		try {
			var style = window.getComputedStyle(el);
			visible = style && style.display !== "none";
		} catch (e) {
			visible = el.style && el.style.display !== "none";
		}
		if (!visible)
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

	var origError = console.error;
	console.error = function () {
		try {
			var msg = Array.prototype.map.call(arguments, stringifyArg).join(" ");
			if (msg.indexOf(TAG) === -1)
				lastError = msg.length > 4000 ? msg.substring(0, 4000) + "…" : msg;
			enrichErrorUi();
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
		}).observe(el, { attributes: true, attributeFilter: ["style", "class"] });
	}

	try {
		console.info(TAG, "diagnostics script loaded");
	} catch (e) { }
})();
