// "Photos like this part of a photo" (S-041, D-099): draws a rectangle on the photo's preview and puts it in the form
// next to it as x,y,width,height, fractions of the photo (not of the image element: a tall photo is letterboxed by
// object-fit: contain). Works with a mouse, a finger or a pen, and with the keyboard: Enter or Space starts a box in the
// middle, the arrow keys move it, Shift with the arrow keys resizes it, Ctrl or Alt makes the steps bigger, Escape clears it. Without this script the form
// stays hidden and the page is unchanged. There is no automated test of this script (the repository has no JavaScript
// test setup); docs/operations.md has the manual check.
(() => {
    const minimum = 0.02;
    const step = 0.02;
    const clamp = (value, low, high) => Math.min(Math.max(value, low), high);

    // Where the photo is inside the image element, in pixels relative to the frame.
    function pictureRect(image, frame) {
        const frameBox = frame.getBoundingClientRect();
        const imageBox = image.getBoundingClientRect();
        const naturalWidth = image.naturalWidth || imageBox.width;
        const naturalHeight = image.naturalHeight || imageBox.height;
        const scale = Math.min(imageBox.width / naturalWidth, imageBox.height / naturalHeight);
        const width = naturalWidth * scale;
        const height = naturalHeight * scale;
        return {
            left: imageBox.left - frameBox.left + (imageBox.width - width) / 2,
            top: imageBox.top - frameBox.top + (imageBox.height - height) / 2,
            width,
            height,
        };
    }

    function setup(frame) {
        const root = frame.closest("[data-region]");
        const image = frame.querySelector("img");
        const box = frame.querySelector("[data-region-box]");
        const form = root.querySelector("[data-region-form]");
        const input = form.querySelector("[data-region-input]");
        const submit = form.querySelector("[data-region-submit]");
        const clear = form.querySelector("[data-region-clear]");
        const toggle = root.querySelector("[data-region-toggle]");
        const status = form.querySelector("[data-region-status]");
        if (!image || !box || !input || !submit || !clear || !toggle) {
            return;
        }

        let selection = null; // { x, y, w, h } as fractions of the photo
        let drag = null;

        function render() {
            if (!selection) {
                box.hidden = true;
                input.value = "";
                submit.disabled = true;
                clear.hidden = true;
                return;
            }

            const picture = pictureRect(image, frame);
            box.hidden = false;
            box.style.left = `${picture.left + selection.x * picture.width}px`;
            box.style.top = `${picture.top + selection.y * picture.height}px`;
            box.style.width = `${selection.w * picture.width}px`;
            box.style.height = `${selection.h * picture.height}px`;
            input.value = [selection.x, selection.y, selection.w, selection.h].map(v => v.toFixed(4)).join(",");
            submit.disabled = false;
            clear.hidden = false;
        }

        // Said in words, and only when a selection is finished: a live region that changes on every pointer move would be
        // read out over and over. The texts come from the page, in its language.
        function announce() {
            if (!status) {
                return;
            }

            status.textContent = selection
                ? (status.dataset.selected || "").replace("{0}", Math.round(selection.w * 100)).replace("{1}", Math.round(selection.h * 100))
                : (status.dataset.none || "");
        }

        function pointAt(event) {
            const picture = pictureRect(image, frame);
            const frameBox = frame.getBoundingClientRect();
            return {
                x: clamp((event.clientX - frameBox.left - picture.left) / picture.width, 0, 1),
                y: clamp((event.clientY - frameBox.top - picture.top) / picture.height, 0, 1),
            };
        }

        function setSelecting(on) {
            frame.classList.toggle("selecting", on);
            toggle.setAttribute("aria-pressed", on ? "true" : "false");
            frame.tabIndex = on ? 0 : -1;
            if (on) {
                frame.focus();
            } else {
                selection = null;
                render();
                announce();
            }
        }

        toggle.hidden = false;
        form.hidden = false;
        toggle.addEventListener("click", () => setSelecting(toggle.getAttribute("aria-pressed") !== "true"));
        clear.addEventListener("click", () => {
            selection = null;
            render();
            announce();
            frame.focus();
        });

        frame.addEventListener("pointerdown", event => {
            if (!frame.classList.contains("selecting") || event.button > 0) {
                return;
            }

            event.preventDefault();
            frame.setPointerCapture(event.pointerId);
            drag = { start: pointAt(event), pointer: event.pointerId };
            selection = null;
        });

        frame.addEventListener("pointermove", event => {
            if (!drag || event.pointerId !== drag.pointer) {
                return;
            }

            const here = pointAt(event);
            const x = Math.min(drag.start.x, here.x);
            const y = Math.min(drag.start.y, here.y);
            selection = { x, y, w: Math.abs(here.x - drag.start.x), h: Math.abs(here.y - drag.start.y) };
            render();
        });

        const finish = event => {
            if (!drag || event.pointerId !== drag.pointer) {
                return;
            }

            drag = null;
            // A click, or a sliver, is not a region.
            if (selection && (selection.w < minimum || selection.h < minimum)) {
                selection = null;
            }

            render();
            announce();
        };

        frame.addEventListener("pointerup", finish);
        frame.addEventListener("pointercancel", finish);

        frame.addEventListener("keydown", event => {
            if (!frame.classList.contains("selecting")) {
                return;
            }

            if (event.key === "Escape") {
                selection = null;
                render();
                announce();
                return;
            }

            if (!selection) {
                if (event.key === "Enter" || event.key === " ") {
                    event.preventDefault();
                    selection = { x: 0.3, y: 0.3, w: 0.4, h: 0.4 };
                    render();
                    announce();
                }

                return;
            }

            // Ctrl or Alt with an arrow takes a step five times as big.
            const size = event.ctrlKey || event.altKey ? step * 5 : step;
            const delta = { ArrowLeft: [-size, 0], ArrowRight: [size, 0], ArrowUp: [0, -size], ArrowDown: [0, size] }[event.key];
            if (!delta) {
                return;
            }

            event.preventDefault();
            if (event.shiftKey) {
                selection.w = clamp(selection.w + delta[0], minimum, 1 - selection.x);
                selection.h = clamp(selection.h + delta[1], minimum, 1 - selection.y);
            } else {
                selection.x = clamp(selection.x + delta[0], 0, 1 - selection.w);
                selection.y = clamp(selection.y + delta[1], 0, 1 - selection.h);
            }

            render();
            announce();
        });

        window.addEventListener("resize", render);
        render();
    }

    // Enhanced navigation can bring the page in without loading the scripts again, so look for frames on each load.
    function setupAll() {
        document.querySelectorAll("[data-region-frame]:not([data-region-ready])").forEach(frame => {
            frame.setAttribute("data-region-ready", "");
            setup(frame);
        });
    }

    setupAll();
    if (window.Blazor?.addEventListener) {
        window.Blazor.addEventListener("enhancedload", setupAll);
    } else {
        document.addEventListener("DOMContentLoaded", () => window.Blazor?.addEventListener?.("enhancedload", setupAll));
    }
})();
