// The question box on the timeline (D-101). A question is a plain form post, and the server may take up to a minute and
// a half when the question service has to prepare itself, so the page would sit unchanged. When the form is sent, say so in
// the status line (the texts are in its data attributes, from the resources), after a few seconds say why it takes long, and
// give up waiting after a little more than the server's own limit so that a stopped or dropped request does not leave the
// box stuck. The button is aria-disabled, not disabled, so it keeps the focus; a second submit is stopped. Without JavaScript
// the browser's own loading indicator is all there is. There is no automated test of this script (the repository has no
// JavaScript test setup); a page test checks the markup it needs.
(() => {
    const slowAfterMs = 8000;
    const giveUpAfterMs = 100000;
    const timers = new WeakMap();

    function statusOf(form) {
        return form.querySelector(".ask-status");
    }

    function clearTimers(form) {
        const own = timers.get(form);
        if (own) {
            own.forEach(clearTimeout);
            timers.delete(form);
        }
    }

    function setBusy(form, busy) {
        form.classList.toggle("busy", busy);
        if (busy) {
            form.setAttribute("aria-busy", "true");
        } else {
            form.removeAttribute("aria-busy");
        }

        // aria-disabled, never disabled: a disabled button that has the focus sends it to the page. A button the server
        // disabled (no key) is left as it is, since only this attribute is touched.
        form.querySelectorAll("button[type=submit]").forEach((button) => {
            if (busy) {
                button.setAttribute("aria-disabled", "true");
            } else {
                button.removeAttribute("aria-disabled");
            }
        });
    }

    function reset(form, failed) {
        clearTimers(form);
        setBusy(form, false);
        form.classList.toggle("failed", failed);
        const status = statusOf(form);
        if (status) {
            status.textContent = failed ? (status.dataset.timeout ?? "") : "";
        }
    }

    document.addEventListener("submit", (event) => {
        const form = event.target;
        if (!(form instanceof HTMLFormElement) || !form.classList.contains("ask")) {
            return;
        }

        if (form.classList.contains("busy")) {
            event.preventDefault();
            return;
        }

        const status = statusOf(form);
        form.classList.remove("failed");
        setBusy(form, true);
        if (status) {
            const thinking = status.dataset.thinking ?? "";
            status.textContent = thinking;
            timers.set(form, [
                setTimeout(() => { status.textContent = `${thinking} ${status.dataset.slow ?? ""}`.trim(); }, slowAfterMs),
                setTimeout(() => reset(form, true), giveUpAfterMs),
            ]);
        }
    });

    // Typing a new question after a stopped or failed one clears what was left over.
    document.addEventListener("input", (event) => {
        const form = event.target instanceof Element ? event.target.closest("form.ask") : null;
        if (form && (form.classList.contains("busy") || form.classList.contains("failed"))) {
            reset(form, false);
        }
    });

    // Back or forward may bring the page from the browser's cache in the state it was left in.
    window.addEventListener("pageshow", () => {
        document.querySelectorAll("form.ask").forEach((form) => reset(form, false));
    });
})();
