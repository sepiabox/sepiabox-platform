// The filter chips of the timeline (S-067) are details elements in one GET form and work without this script: "Apply
// filters" submits everything. With it, a click outside or Escape closes the open popover, the people search narrows the
// rows as you type, and a pointer choice (or Enter) on a radio, or a click on "With me", submits the form at once (arrow keys do not). Checkboxes never submit by themselves, so several
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
        const pop = box.closest(".pop");
        let shown = 0;
        pop?.querySelectorAll("[data-filter-options] .opt").forEach((row) => {
            const name = (row.getAttribute("data-name") ?? row.textContent ?? "").toLocaleLowerCase();
            row.hidden = needle !== "" && !name.includes(needle);
            if (!row.hidden) {
                shown++;
            }
        });
        const none = pop?.querySelector("[data-filter-empty]");
        if (none) {
            none.hidden = shown > 0;
        }
    });

    // A radio (and the "With me" pill, a single yes or no) submits when it is chosen with a pointer, or with Enter, and never
    // on a keyboard change: the arrow keys move the choice through a group and each step would otherwise submit (a keyboard
    // user browsing the years). The last thing the person did tells which it was.
    let byPointer = false;
    document.addEventListener("pointerdown", () => { byPointer = true; }, true);
    document.addEventListener("keydown", () => { byPointer = false; }, true);

    const submitsAtOnce = (input) =>
        (input.type === "radio" && input.closest("details.fchip") !== null) ||
        (input.type === "checkbox" && input.closest("label.pill.me") !== null);

    document.addEventListener("change", (event) => {
        const input = event.target;
        if (byPointer && input instanceof HTMLInputElement && submitsAtOnce(input)) {
            input.form?.requestSubmit();
        }
    });

    document.addEventListener("keydown", (event) => {
        const input = event.target;
        if (event.key === "Enter" && input instanceof HTMLInputElement && input.type === "radio" && input.closest("details.fchip")) {
            event.preventDefault();
            input.form?.requestSubmit();
        }
    });

    // "Close" in a sheet's footer needs this script: the class shows it (a page change by enhanced navigation keeps the class).
    document.documentElement.classList.add("filters-js");
    document.addEventListener("click", (event) => {
        const button = event.target instanceof Element ? event.target.closest("button[data-close]") : null;
        const chip = button?.closest("details.fchip");
        if (chip) {
            chip.open = false;
            chip.querySelector("summary")?.focus();
        }
    });
})();
