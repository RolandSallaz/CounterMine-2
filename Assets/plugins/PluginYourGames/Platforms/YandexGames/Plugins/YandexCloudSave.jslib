mergeInto(LibraryManager.library,
{
	// Yandex cloud saves (player data) + public player name.
	// All reads come back via SendMessage to keep the C# side allocation-free.
	CloudSaveSet_js: function (keyPtr, jsonPtr) {
		try {
			var key = UTF8ToString(keyPtr);
			var json = UTF8ToString(jsonPtr);
			if (typeof ysdk === 'undefined' || ysdk === null) { console.warn('CloudSaveSet: YSDK not ready'); return; }
			ysdk.getPlayer().then(function (player) {
				var data = {};
				data[key] = json;
				return player.setData(data, true);
			}).catch(function (e) { console.error('CloudSaveSet: ' + e); });
		} catch (e) { console.error('CloudSaveSet: ' + e); }
	},

	CloudSaveGet_js: function (keyPtr, objPtr, methodPtr) {
		try {
			var key = UTF8ToString(keyPtr);
			var obj = UTF8ToString(objPtr);
			var method = UTF8ToString(methodPtr);
			if (typeof ysdk === 'undefined' || ysdk === null) { console.warn('CloudSaveGet: YSDK not ready'); SendMessage(obj, method, ''); return; }
			ysdk.getPlayer().then(function (player) {
				return player.getData([key]);
			}).then(function (data) {
				var v = (data && typeof data[key] !== 'undefined' && data[key] !== null) ? data[key] : '';
				SendMessage(obj, method, v);
			}).catch(function (e) { console.error('CloudSaveGet: ' + e); SendMessage(obj, method, ''); });
		} catch (e) { console.error('CloudSaveGet: ' + e); }
	},

	YandexPlayerName_js: function (objPtr, methodPtr) {
		try {
			var obj = UTF8ToString(objPtr);
			var method = UTF8ToString(methodPtr);
			if (typeof ysdk === 'undefined' || ysdk === null) { console.warn('PlayerName: YSDK not ready'); SendMessage(obj, method, ''); return; }
			ysdk.getPlayer().then(function (player) {
				var name = '';
				try { name = player.getName() || ''; } catch (e) { console.error('PlayerName: ' + e); }
				SendMessage(obj, method, name);
			}).catch(function (e) { console.error('PlayerName: ' + e); SendMessage(obj, method, ''); });
		} catch (e) { console.error('PlayerName: ' + e); }
	}
});
