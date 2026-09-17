mergeInto(LibraryManager.library,
{
	// NOTE: every function is guarded with typeof checks so the game also runs
	// outside Yandex Games (plain WebGL hosting, local tests). Off-platform
	// everything becomes a safe no-op with a console warning.
	IsInitSDK_js: function ()
	{
		try { return (typeof initYSDK !== 'undefined' && initYSDK) ? 1 : 0; }
		catch (e) { return 0; }
	},

	InitGame_js: function ()
	{
		try {
			if (typeof InitGame === 'function') InitGame();
			else console.warn('InitGame: not on Yandex platform, skipped');
		} catch (e) { console.error('InitGame: ' + e); }
	},

	GameReadyAPI_js: function() {
		try {
			if (typeof ysdk !== 'undefined' && ysdk !== null && ysdk.features !== undefined && ysdk.features.LoadingAPI !== undefined && ysdk.features.LoadingAPI !== null) {
				ysdk.features.LoadingAPI.ready();
				try {
					if (typeof LogStyledMessage === 'function') LogStyledMessage('Game Ready');
					else console.log('Game Ready');
				} catch (logError) { console.log('Game Ready'); }
			}
			else {
				console.warn('GameReadyAPI: not on Yandex platform, skipped');
			}
		} catch (e) { console.error('GameReadyAPI: ' + e); }
	},

	GameplayStart_js: function () {
		try {
			if (typeof ysdk !== 'undefined' && ysdk !== null && ysdk.features !== undefined && ysdk.features.GameplayAPI !== undefined) {
				ysdk.features.GameplayAPI.start();
			}
			else {
				console.warn('GameplayStart: not on Yandex platform, skipped');
			}
		} catch (e) { console.error('GameplayStart: ' + e); }
	},

	GameplayStop_js: function () {
		try {
			if (typeof ysdk !== 'undefined' && ysdk !== null && ysdk.features !== undefined && ysdk.features.GameplayAPI !== undefined) {
				ysdk.features.GameplayAPI.stop();
			}
			else {
				console.warn('GameplayStop: not on Yandex platform, skipped');
			}
		} catch (e) { console.error('GameplayStop: ' + e); }
	},

	LogStyledMessage: function(message, style) {
		try {
			if (typeof LogStyledMessage === 'function') LogStyledMessage(UTF8ToString(message), UTF8ToString(style));
			else console.log(UTF8ToString(message));
		} catch (e) { console.error('LogStyledMessage: ' + e); }
	},

	LogStyledMessage: function(message) {
		try {
			if (typeof LogStyledMessage === 'function') LogStyledMessage(UTF8ToString(message));
			else console.log(UTF8ToString(message));
		} catch (e) { console.error('LogStyledMessage: ' + e); }
	}
});
