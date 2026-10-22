// "Select all" and "Select none" on the organize page (OrganizeCard, BulkSelect). The buttons are in the page hidden and
// shown here, so without JavaScript they never appear. There is no automated test of this script (the repository has no
// JavaScript test setup).
(() => {
    function show() {
        document.querySelectorAll("[data-bulk]").forEach((bulk) => { bulk.hidden = false; });
    }

    document.addEventListener("click", (event) => {
        const button = event.target instanceof Element ? event.target.closest("button[data-select]") : null;
        const form = button?.closest("form");
        if (!button || !form) {
            return;
        }

        const checked = button.getAttribute("data-select") === "all";
        form.querySelectorAll("input[type=checkbox][name=Ids]").forEach((box) => { box.checked = checked; });
    });

    show();
    // Enhanced navigation brings a new page in without loading this script again.
    if (window.Blazor) {
        window.Blazor.addEventListener("enhancedload", show);
    } else {
        document.addEventListener("DOMContentLoaded", () => window.Blazor?.addEventListener("enhancedload", show));
    }
})();
