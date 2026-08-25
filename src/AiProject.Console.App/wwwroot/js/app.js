(function () {
    const saved = localStorage.getItem("aiConsole.theme");
    if (saved === "dark" || saved === "light")
        document.documentElement.setAttribute("data-theme", saved);
})();

window.aiConsole = {
    setTheme: function (theme) {
        const t = theme === "dark" ? "dark" : "light";
        document.documentElement.setAttribute("data-theme", t);
        localStorage.setItem("aiConsole.theme", t);
    },
    copyText: async function (text) {
        try {
            await navigator.clipboard.writeText(text);
        } catch (e) {
            const ta = document.createElement("textarea");
            ta.value = text;
            document.body.appendChild(ta);
            ta.select();
            document.execCommand("copy");
            document.body.removeChild(ta);
        }
    },
    scrollToEnd: function (id) {
        const el = document.getElementById(id);
        if (el) el.scrollTop = el.scrollHeight;
    },
    initSplitter: function (splitId) {
        const split = document.getElementById(splitId);
        const gutter = split?.querySelector(".split-gutter");
        if (!split || !gutter || split.dataset.splitReady) return;
        split.dataset.splitReady = "1";

        const key = "aiConsole.splitLeftPct";
        const min = 180;
        let lastPct = null;
        let dragging = false;

        function applyPct(pct) {
            const w = split.clientWidth;
            const gutterW = gutter.offsetWidth;
            const max = Math.max(min, w - gutterW - min);
            const left = Math.max(min, Math.min(max, (w * pct) / 100));
            split.style.setProperty("--split-left", left + "px");
            split.classList.add("is-sized");
            lastPct = (left / w) * 100;
        }

        const saved = parseFloat(localStorage.getItem(key));
        if (saved > 5 && saved < 95)
            applyPct(saved);

        gutter.addEventListener("pointerdown", (e) => {
            if (e.button !== 0) return;
            dragging = true;
            gutter.classList.add("dragging");
            split.classList.add("dragging");
            gutter.setPointerCapture(e.pointerId);
            e.preventDefault();
        });
        gutter.addEventListener("pointermove", (e) => {
            if (!dragging) return;
            const rect = split.getBoundingClientRect();
            const gutterW = gutter.offsetWidth;
            const max = Math.max(min, rect.width - gutterW - min);
            const left = Math.max(min, Math.min(max, e.clientX - rect.left));
            split.style.setProperty("--split-left", left + "px");
            split.classList.add("is-sized");
            lastPct = (left / rect.width) * 100;
        });
        function endDrag() {
            if (!dragging) return;
            dragging = false;
            gutter.classList.remove("dragging");
            split.classList.remove("dragging");
            if (lastPct > 0)
                localStorage.setItem(key, String(lastPct));
        }
        gutter.addEventListener("pointerup", endDrag);
        gutter.addEventListener("pointercancel", endDrag);
        gutter.addEventListener("dblclick", () => {
            split.style.removeProperty("--split-left");
            split.classList.remove("is-sized");
            lastPct = null;
            localStorage.removeItem(key);
        });
        window.addEventListener("resize", () => {
            if (lastPct != null)
                applyPct(lastPct);
        });
    }
};
