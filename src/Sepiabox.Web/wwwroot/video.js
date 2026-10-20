// The video player on the detail page (VideoPlayer.razor, D-049). Its src is the web's own address, which
// redirects to a signed storage link. When the link or the network fails (an expired or unsignable link, a
// lost connection), ask the web once more for a fresh link and resume where the video was; otherwise show
// the message under the player instead of a blank frame.
// A failure gets one retry. Another retry is allowed only after the retried video has played 30 seconds
// past where it failed, so a fault that repeats at the same point ends in the message instead of a loop,
// and never more than 3 times per player. There is no automated test of this script (the repository has
// no JavaScript test setup); docs/operations.md has the manual check.
(() => {
    const playerSelector = "video[data-player]";
    const progressBeforeAnotherRetry = 30;
    const maxRetries = 3;

    // Per player: retries so far, whether a retry is still unproven, its listeners, and the play state.
    const states = new WeakMap();

    function stateOf(video) {
        let state = states.get(video);
        if (!state) {
            state = { retries: 0, retryPending: false, listeners: null, playing: false };
            states.set(video, state);
        }

        return state;
    }

    // A refused or expired link reaches the element as a source it cannot use (an HTML or XML error page
    // instead of video), a dropped connection as a network error. A decoding error or an abort would only
    // happen again with a new link.
    function isWorthRetrying(error) {
        return error?.code === MediaError.MEDIA_ERR_NETWORK || error?.code === MediaError.MEDIA_ERR_SRC_NOT_SUPPORTED;
    }

    function handleFailure(video) {
        const state = stateOf(video);
        if (isWorthRetrying(video.error) && !state.retryPending && state.retries < maxRetries) {
            retry(video, state);
            return;
        }

        state.listeners?.abort();
        const message = video.closest(".player")?.querySelector(".player-error");
        if (message && message.textContent === "") {
            // The alert is hidden while empty (CSS), so filling it both shows and announces it.
            message.textContent = message.dataset.message;
        }
    }

    function retry(video, state) {
        state.retries++;
        state.retryPending = true;
        const resumeAt = video.currentTime;
        const wasPlaying = state.playing;

        // A new address, so neither the browser's cached redirect nor its media cache is reused.
        const url = new URL(video.getAttribute("src"), document.baseURI);
        url.searchParams.set("retry", Date.now().toString());
        const retryUrl = url.toString();

        state.listeners?.abort();
        const listeners = new AbortController();
        state.listeners = listeners;
        const stillThisRetry = () => video.src === retryUrl;

        video.addEventListener("loadedmetadata", () => {
            if (!stillThisRetry()) {
                return;
            }

            video.currentTime = resumeAt;
            if (wasPlaying) {
                // A browser may refuse to play without a gesture; the video then waits, paused, at the same place.
                video.play().catch(() => { state.playing = false; });
            }
        }, { once: true, signal: listeners.signal });

        // Real progress past the failure point: a later failure (a link expired hours later) gets its own retry.
        video.addEventListener("timeupdate", () => {
            if (stillThisRetry() && video.currentTime > resumeAt + progressBeforeAnotherRetry) {
                state.retryPending = false;
                listeners.abort();
            }
        }, { signal: listeners.signal });

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
            stateOf(video).playing = event.type === "play";
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
})();
