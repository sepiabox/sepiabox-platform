// "Select all" and "Select none" on the organize page (OrganizeCard, BulkSelect), and the action's label with the number of photos
// checked ("Set aside 12 checked photos", S-075; the server renders the same words for the boxes checked on arrival). The buttons are in the page hidden and
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
        updateBar(form);
    });

    function updateBar(form) {
        const button = form.querySelector("button[data-bar-button]");
        if (!button) {
            return;
        }

        const count = form.querySelectorAll("input[type=checkbox][name=Ids]:checked").length;
        const pattern = count === 0 ? button.dataset.none : count === 1 ? button.dataset.one : button.dataset.other;
        button.textContent = (pattern ?? "").replace("{0}", count.toLocaleString());
        button.disabled = count === 0;
    }

    function updateAll() {
        document.querySelectorAll("form.organize-form").forEach(updateBar);
    }

    document.addEventListener("change", (event) => {
        const box = event.target;
        if (box instanceof HTMLInputElement && box.name === "Ids" && box.form) {
            updateBar(box.form);
        }
    });

    function start() {
        show();
        updateAll();
    }

    start();
    // Enhanced navigation brings a new page in without loading this script again.
    if (window.Blazor) {
        window.Blazor.addEventListener("enhancedload", start);
    } else {
        document.addEventListener("DOMContentLoaded", () => window.Blazor?.addEventListener("enhancedload", start));
    }
})();
