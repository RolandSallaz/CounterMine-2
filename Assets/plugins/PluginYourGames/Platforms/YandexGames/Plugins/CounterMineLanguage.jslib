mergeInto(LibraryManager.library, {
    CounterMineLanguage_js: function () {
        var language = 'en';
        if (typeof ysdk !== 'undefined' && ysdk && ysdk.environment && ysdk.environment.i18n) {
            language = ysdk.environment.i18n.lang || 'en';
        }
        var buffer = _malloc(lengthBytesUTF8(language) + 1);
        stringToUTF8(language, buffer, lengthBytesUTF8(language) + 1);
        return buffer;
    }
});
