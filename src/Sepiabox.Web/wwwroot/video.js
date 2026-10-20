// The video player on the detail page (VideoPlayer.razor, D-049). Its src is the web's own address, which
// redirects to a signed storage link. When playback fails (an expired or unsignable link, a lost
// connection, a codec the browser lacks), ask the web once more for a fresh link and resume where the
// video was; when that fails too, show the message under the player instead of a blank frame.
// The pages are static and enhanced navigation swaps their content, so the listener is on the document.
// Media errors do not bubble, hence the capture phase.
document.addEventListener("error", (event) => {
    const video = event.target;
    if (!(video instanceof HTMLVideoElement) || !video.hasAttribute("data-player")) {
        return;
    }

    if (video.dataset.retried !== "true") {
        video.dataset.retried = "true";
        const resumeAt = video.currentTime;
        const wasPlaying = !video.paused;
        // A new address, so neither the browser's cached redirect nor its media cache is reused.
        const url = new URL(video.getAttribute("src"), document.baseURI);
        url.searchParams.set("retry", Date.now().toString());
        video.addEventListener("loadedmetadata", () => {
            video.currentTime = resumeAt;
            if (wasPlaying) {
                video.play().catch(() => { });
            }
        }, { once: true });
        video.src = url.toString();
        return;
    }

    const message = video.closest(".player")?.querySelector(".player-error");
    if (message) {
        message.hidden = false;
    }
}, true);
