// The "More" disclosure of the header (MainLayout, D-102) is a details element and works without this script. With it, a
// click outside or Escape closes it (Escape puts the focus back on its summary), and so does a page change by enhanced
// navigation, which keeps the layout. There is no automated test of this script (the repository has no JavaScript test
// setup).
(() => {
    const open = () => document.querySelectorAll("details[data-more][open]");

    document.addEventListener("click", (event) => {
        open().forEach((more) => {
            if (!(event.target instanceof Node) || !more.contains(event.target)) {
                more.open = false;
            }
        });
    });

    document.addEventListener("keydown", (event) => {
        if (event.key !== "Escape") {
            return;
        }

        open().forEach((more) => {
            more.open = false;
            more.querySelector("summary")?.focus();
        });
    });

    const close = () => open().forEach((more) => { more.open = false; });
    if (window.Blazor) {
        window.Blazor.addEventListener("enhancedload", close);
    } else {
        document.addEventListener("DOMContentLoaded", () => window.Blazor?.addEventListener("enhancedload", close));
    }
})();
