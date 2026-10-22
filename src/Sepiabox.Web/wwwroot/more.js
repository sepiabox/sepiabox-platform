// The "More" disclosure of the header (MainLayout, D-102) is a details element and works without this script. With it, a
// click outside closes it, so does tabbing or clicking away from it, and so does a page change by enhanced navigation,
// which keeps the layout; Escape closes it only when the focus is inside it (so it never pulls the focus out of a search
// field or the region frame) and puts the focus back on its summary. There is no automated test of this script (the
// repository has no JavaScript test setup).
(() => {
    const open = () => document.querySelectorAll("details[data-more][open]");

    document.addEventListener("click", (event) => {
        open().forEach((more) => {
            if (!(event.target instanceof Node) || !more.contains(event.target)) {
                more.open = false;
            }
        });
    });

    document.addEventListener("focusout", (event) => {
        const more = event.target instanceof Element ? event.target.closest("details[data-more]") : null;
        // relatedTarget is null when the focus leaves the page, which closes it too; a click inside keeps it (relatedTarget is inside).
        if (more?.open && !(event.relatedTarget instanceof Node && more.contains(event.relatedTarget))) {
            more.open = false;
        }
    });

    document.addEventListener("keydown", (event) => {
        if (event.key !== "Escape") {
            return;
        }

        open().forEach((more) => {
            if (more.contains(document.activeElement)) {
                more.open = false;
                more.querySelector("summary")?.focus();
            }
        });
    });

    const close = () => open().forEach((more) => { more.open = false; });
    if (window.Blazor) {
        window.Blazor.addEventListener("enhancedload", close);
    } else {
        document.addEventListener("DOMContentLoaded", () => window.Blazor?.addEventListener("enhancedload", close));
    }
})();
