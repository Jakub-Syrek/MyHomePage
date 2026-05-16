window.setupVideoHandlers = function() {
    console.log('setupVideoHandlers called');
    const videos = document.querySelectorAll('video');
    console.log('Found videos:', videos.length);

    videos.forEach(video => {
        if (!video.dataset.handlerAttached) {
            console.log('Attaching handlers to video');

            video.addEventListener('play', function() {
                console.log('VIDEO PLAY');
                const card = this.closest('.video-card');
                if (card) {
                    card.classList.add('playing');
                    console.log('Added playing class');
                }
            });

            video.addEventListener('pause', function() {
                console.log('VIDEO PAUSE');
                const card = this.closest('.video-card');
                if (card) {
                    card.classList.remove('playing');
                    console.log('Removed playing class');
                }
            });

            video.dataset.handlerAttached = 'true';
        }
    });
};

window.startVideoHandlerInterval = function() {
    console.log('startVideoHandlerInterval called');
    window.setupVideoHandlers();
    setInterval(window.setupVideoHandlers, 500);
};

window.appShareSupported = function () {
    return typeof navigator !== 'undefined' && typeof navigator.share === 'function';
};

window.appShare = async function (title, text, url) {
    if (!window.appShareSupported()) return false;
    try {
        await navigator.share({ title: title, text: text, url: url });
        return true;
    } catch (e) {
        // User cancelled or share failed — silently swallow
        return false;
    }
};
