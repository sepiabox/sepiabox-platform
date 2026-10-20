// The video player on the detail page (VideoPlayer.razor, D-049). Its src is the web's own address, which
// redirects to a signed storage link. When playback fails (an expired or unsignable link, a lost
// connection, a codec the browser lacks), ask the web once more for a fresh link and resume where the
// video was; when that fails too, show the message under the player instead of a blank frame.
// Each failure gets one retry: once a retried load plays, a later failure (a link expired hours later)
// gets its own. There is no automated test of this script (the repository has no JavaScript test setup);
// docs/operations.md has the manual check.

const playerSelector = "video[data-player]";

function handleFailure(video) {
    if (video.dataset.retried !== "true") {
        retry(video);
        return;
    }

    // The retry failed as well: stop waiting for it and explain.
    video.retryListeners?.abort();
    const message = video.closest(".player")?.querySelector(".player-error");
    if (message && message.hidden) {
        message.textContent = message.dataset.message;
        message.hidden = false;
    }
}

function retry(video) {
    video.dataset.retried = "true";
    const resumeAt = video.currentTime;
    const wasPlaying = video.dataset.playing === "true";

    // A new address, so neither the browser's cached redirect nor its media cache is reused.
    const url = new URL(video.getAttribute("src"), document.baseURI);
    url.searchParams.set("retry", Date.now().toString());
    const retryUrl = url.toString();

    video.retryListeners?.abort();
    const listeners = new AbortController();
    video.retryListeners = listeners;
    const stillThisRetry = () => video.src === retryUrl;

    video.addEventListener("loadedmetadata", () => {
        if (!stillThisRetry()) {
            return;
        }

        video.currentTime = resumeAt;
        if (wasPlaying) {
            // A browser may refuse to play without a gesture; the video then waits, paused, at the same place.
            video.play().catch(() => { });
        }
    }, { once: true, signal: listeners.signal });

    // The retried link works: a later failure deserves a retry of its own.
    video.addEventListener("canplay", () => {
        if (stillThisRetry()) {
            delete video.dataset.retried;
            listeners.abort();
        }
    }, { once: true, signal: listeners.signal });

    video.src = retryUrl;
}

function onEvent(event) {
    const video = event.target;
    if (!(video instanceof HTMLVideoElement) || !video.matches(playerSelector)) {
        return;
    }

    if (event.type === "error") {
        handleFailure(video);
    } else {
        // Recorded as it happens: by the time of an error the element may already report itself paused.
        video.dataset.playing = event.type === "play" ? "true" : "false";
    }
}

// Errors that fired before this script ran (a fast failure on a full page load) left no event to catch.
function checkPlayersAlreadyFailed() {
    for (const video of document.querySelectorAll(playerSelector)) {
        if (video.error !== null) {
            handleFailure(video);
        }
    }
}

// The pages are static and enhanced navigation swaps their content, so the listeners are on the document.
// Media events do not bubble, hence the capture phase.
for (const type of ["error", "play", "pause"]) {
    document.addEventListener(type, onEvent, true);
}

checkPlayersAlreadyFailed();
if (window.Blazor?.addEventListener) {
    window.Blazor.addEventListener("enhancedload", checkPlayersAlreadyFailed);
} else {
    document.addEventListener("DOMContentLoaded", () => window.Blazor?.addEventListener?.("enhancedload", checkPlayersAlreadyFailed));
}
