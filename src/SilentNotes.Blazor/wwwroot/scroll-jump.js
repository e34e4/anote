/**
 * Finds the most relevant scrollable element on the current aNote page and jumps
 * to its top or bottom. Prefers nested list/conversation panes over the window.
 */
(function () {
	const CANDIDATES = [
		".sms-messages",
		".tg-messages",
		".rss-overlay-body",
		".mail-overlay",
		".file-overlay",
		".rss-list",
		".clipboard-list",
		".booky-list",
		".contacts-list",
		".password-list",
		"#note-editor-scroll",
		"#content",
		".mud-main-content",
	];

	function isVisible(el) {
		if (!el)
			return false;
		const style = window.getComputedStyle(el);
		if (style.display === "none" || style.visibility === "hidden" || style.opacity === "0")
			return false;
		const rect = el.getBoundingClientRect();
		return rect.width > 0 && rect.height > 0;
	}

	function scrollableDelta(el) {
		if (!el)
			return 0;
		return Math.max(0, (el.scrollHeight || 0) - (el.clientHeight || 0));
	}

	function findTarget() {
		let best = null;
		let bestDelta = 0;
		for (const selector of CANDIDATES) {
			const nodes = document.querySelectorAll(selector);
			for (const el of nodes) {
				if (!isVisible(el))
					continue;
				const delta = scrollableDelta(el);
				if (delta > bestDelta) {
					best = el;
					bestDelta = delta;
				}
			}
		}

		if (best && bestDelta > 8)
			return { kind: "element", el: best };

		const scrollingElement = document.scrollingElement || document.documentElement || document.body;
		if (scrollingElement && scrollableDelta(scrollingElement) > 8)
			return { kind: "element", el: scrollingElement };

		return { kind: "window" };
	}

	function jump(direction) {
		const target = findTarget();
		const behavior = "smooth";
		if (target.kind === "element") {
			const top = direction === "top" ? 0 : target.el.scrollHeight;
			try {
				target.el.scrollTo({ top: top, behavior: behavior });
			} catch (_) {
				target.el.scrollTop = top;
			}
			return;
		}

		const y = direction === "top"
			? 0
			: Math.max(
				document.body ? document.body.scrollHeight : 0,
				document.documentElement ? document.documentElement.scrollHeight : 0);
		try {
			window.scrollTo({ top: y, behavior: behavior });
		} catch (_) {
			window.scrollTo(0, y);
		}
	}

	window.anoteScrollJump = {
		toTop: function () { jump("top"); },
		toBottom: function () { jump("bottom"); },
	};
})();
