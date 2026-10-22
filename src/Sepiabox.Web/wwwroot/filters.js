// The filter chips of the timeline (S-067) are details elements in one GET form and work without this script: "Apply
// filters" submits everything. With it, a click outside or Escape closes the open popover, the people search narrows the
// rows as you type, and picking a radio option submits the form at once. Checkboxes never submit by themselves, so several
// people can be ticked before "Show results". There is no automated test of this script (the repository has no
// JavaScript test setup).
(() => {
    const open = () => document.querySelectorAll("details.fchip[open]");

    document.addEventListener("click", (event) => {
        open().forEach((chip) => {
            if (!(event.target instanceof Node) || !chip.contains(event.target)) {
                chip.open = false;
            }
        });
    });

    document.addEventListener("keydown", (event) => {
        if (event.key !== "Escape") {
            return;
        }

        const chip = Array.from(open()).find((c) => c.contains(document.activeElement));
        if (chip) {
            chip.open = false;
            chip.querySelector("summary")?.focus();
        }
    });

    document.addEventListener("input", (event) => {
        const box = event.target;
        if (!(box instanceof HTMLInputElement) || !box.hasAttribute("data-filter-search")) {
            return;
        }

        const needle = box.value.trim().toLocaleLowerCase();
        box.closest(".pop")?.querySelectorAll("[data-filter-options] .opt").forEach((row) => {
            const name = (row.getAttribute("data-name") ?? row.textContent ?? "").toLocaleLowerCase();
            row.hidden = needle !== "" && !name.includes(needle);
        });
    });

    document.addEventListener("change", (event) => {
        const input = event.target;
        if (input instanceof HTMLInputElement && input.type === "radio" && input.closest("details.fchip")) {
            input.form?.requestSubmit();
        }
    });
})();
