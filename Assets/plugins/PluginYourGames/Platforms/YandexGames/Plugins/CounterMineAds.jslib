mergeInto(LibraryManager.library, {
    CounterMineAd_js: function(request) {
        var finished = false;
        function close() {
            if (finished) return;
            finished = true;
            SendMessage('YandexAds', 'OnAdEvent', request + ':closed');
        }
        try {
            if (typeof ysdk === 'undefined' || !ysdk || !ysdk.adv ||
                typeof ysdk.adv.showFullscreenAdv !== 'function') { close(); return; }
            var result = ysdk.adv.showFullscreenAdv({ callbacks: {
                onOpen: function() {},
                onClose: close,
                onError: close,
                onOffline: close
            }});
            if (result && typeof result.catch === 'function') result.catch(close);
        } catch (error) { close(); }
    }
});
