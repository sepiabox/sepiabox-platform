// The question box on the timeline (D-101). A question is a plain form post, and the server may take up to a minute and
// a half when the question service has to prepare itself, so the page would sit unchanged. When the form is sent, show the
// "thinking" line and stop a second press; going back to the page (the browser's cache) undoes it. Without JavaScript the
// browser's own loading indicator is all there is. There is no automated test of this script (the repository has no
// JavaScript test setup); a page test checks the line is in the markup.
(() => {
    document.addEventListener("submit", (event) => {
        const form = event.target;
        if (!(form instanceof HTMLFormElement) || !form.classList.contains("ask")) {
            return;
        }

        form.classList.add("busy");
        form.setAttribute("aria-busy", "true");
        form.querySelectorAll("button[type=submit]").forEach((button) => { button.disabled = true; });
    });

    window.addEventListener("pageshow", () => {
        document.querySelectorAll("form.ask.busy").forEach((form) => {
            form.classList.remove("busy");
            form.removeAttribute("aria-busy");
            form.querySelectorAll("button[type=submit]").forEach((button) => { button.disabled = false; });
        });
    });
})();
