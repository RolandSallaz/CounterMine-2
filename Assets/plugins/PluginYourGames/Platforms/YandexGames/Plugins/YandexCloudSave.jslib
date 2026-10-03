mergeInto(LibraryManager.library, {
    $CMCloud: {
        playerPromise: null, playerRetryAt: 0, entries: {}, conflicts: {}, nextWriteAt: 0,
        notify: function(state) { SendMessage('YandexCloudSave', 'OnSaveStatus', state); },
        timed: function(promise) {
            return new Promise(function(resolve, reject) {
                var timer = setTimeout(function() { reject(new Error('timeout')); }, 12000);
                Promise.resolve(promise).then(function(v) { clearTimeout(timer); resolve(v); }, function(e) { clearTimeout(timer); reject(e); });
            });
        },
        player: function() {
            if (CMCloud.playerPromise) return CMCloud.playerPromise;
            if (typeof counterMineStandalone !== 'undefined' && counterMineStandalone) {
                CMCloud.playerPromise = Promise.resolve({
                    getUniqueID: function() { return 'standalone'; }, getName: function() { return ''; },
                    getData: function(keys) { var data={}; keys.forEach(function(k){data[k]=localStorage.getItem('cm-standalone:'+k)||'';}); return Promise.resolve(data); },
                    setData: function(data) { Object.keys(data).forEach(function(k){localStorage.setItem('cm-standalone:'+k,data[k]);}); return Promise.resolve(); }
                });
                return CMCloud.playerPromise;
            }
            if (typeof ysdk === 'undefined' || !ysdk || Date.now() < CMCloud.playerRetryAt) return Promise.reject(new Error('sdk unavailable'));
            CMCloud.playerRetryAt = Date.now() + 20000;
            CMCloud.playerPromise = CMCloud.timed(ysdk.getPlayer()).catch(function(e) { CMCloud.playerPromise=null; throw e; });
            return CMCloud.playerPromise;
        },
        valid: function(value) { if(value==='') return true; try { var v=JSON.parse(value); return v!==null && typeof v==='object' && !Array.isArray(v); } catch(e) { return false; } },
        store: function(entry) {
            try { localStorage.setItem(entry.storage, JSON.stringify(entry.record)); return true; }
            catch(e) { CMCloud.notify('storage-unavailable'); return false; }
        },
        reply: function(obj, method, request, ok, value, error) {
            SendMessage(obj, method, JSON.stringify({request:request,ok:ok,value:value||'',error:error||''}));
        },
        load: function(key,obj,method,request) {
            CMCloud.player().then(function(player) {
                var identity=player.getUniqueID();
                if(!identity) throw new Error('missing identity');
                var storage='cm-save-v2:'+encodeURIComponent(identity)+':'+key;
                var cached=null;
                try { var raw=localStorage.getItem(storage); if(raw)cached=JSON.parse(raw); } catch(e) {}
                return CMCloud.timed(player.getData([key])).then(function(data) {
                    var remote=data && data[key]!=null ? data[key] : '';
                    if(typeof remote!=='string'||!CMCloud.valid(remote))throw new Error('invalid cloud data');
                    var entry={key:key,player:player,storage:storage,record:{value:remote,base:remote,dirty:false},busy:false,timer:null};
                    if(cached && cached.dirty && CMCloud.valid(cached.value)) {
                        if(remote!==cached.base && remote!==cached.value) {
                            CMCloud.conflicts[key]={entry:entry,cached:cached,obj:obj,method:method,request:request};
                            CMCloud.notify('conflict');CMCloud.reply(obj,method,request,false,'','conflict');return;
                        }
                        if(remote!==cached.value)entry.record={value:cached.value,base:remote,dirty:true};
                    }
                    CMCloud.entries[key]=entry;CMCloud.store(entry);
                    CMCloud.notify(entry.record.dirty?'pending':'saved');
                    CMCloud.reply(obj,method,request,true,entry.record.value,'');
                    if(entry.record.dirty)CMCloud.schedule(entry,0);
                });
            }).catch(function(e){CMCloud.notify('retrying');CMCloud.reply(obj,method,request,false,'','unavailable');});
        },
        schedule: function(entry,delay) {
            if(entry.busy||entry.timer!==null||!entry.record.dirty)return;
            entry.timer=setTimeout(function(){entry.timer=null;CMCloud.flush(entry);},Math.max(delay,CMCloud.nextWriteAt-Date.now(),0));
        },
        flush: function(entry) {
            if(entry.busy||!entry.record.dirty)return;
            if(Date.now()<CMCloud.nextWriteAt){CMCloud.schedule(entry,0);return;}
            entry.busy=true;CMCloud.nextWriteAt=Date.now()+4000;
            var sent=entry.record.value, data={};data[entry.key]=sent;
            // Do not race a second write against an unresolved first write.
            Promise.resolve().then(function(){return entry.player.setData(data,true);}).then(function(){
                entry.busy=false;entry.record.base=sent;entry.record.dirty=entry.record.value!==sent;
                var durable=CMCloud.store(entry);
                CMCloud.notify(entry.record.dirty?'pending':durable?'saved':'cloud-only');
                CMCloud.schedule(entry,0);
            },function(){entry.busy=false;CMCloud.notify('pending');CMCloud.schedule(entry,15000);});
        },
        save: function(key,json) {
            var entry=CMCloud.entries[key];
            if(!entry||!CMCloud.valid(json)){CMCloud.notify('retrying');return;}
            entry.record.value=json;entry.record.dirty=true;
            var durable=CMCloud.store(entry);if(durable)CMCloud.notify('pending');
            CMCloud.schedule(entry,0);
        },
        resolve: function(key,useLocal) {
            var conflict=CMCloud.conflicts[key];if(!conflict)return;
            var entry=conflict.entry;
            // Keep both versions as a recovery backup before an explicit user choice.
            try {localStorage.setItem(entry.storage+':conflict-backup',JSON.stringify({local:conflict.cached,cloud:entry.record.value}));}catch(e){}
            if(useLocal)entry.record={value:conflict.cached.value,base:entry.record.value,dirty:true};
            CMCloud.entries[key]=entry;delete CMCloud.conflicts[key];CMCloud.store(entry);
            CMCloud.notify(entry.record.dirty?'pending':'saved');
            CMCloud.reply(conflict.obj,conflict.method,conflict.request,true,entry.record.value,'');
            if(entry.record.dirty)CMCloud.schedule(entry,0);
        }
    },
    CloudSaveSet_js__deps: ['$CMCloud'],
    CloudSaveSet_js: function(key,json) { CMCloud.save(UTF8ToString(key),UTF8ToString(json)); },
    CloudSaveGet_js__deps: ['$CMCloud'],
    CloudSaveGet_js: function(key,obj,method,request) { CMCloud.load(UTF8ToString(key),UTF8ToString(obj),UTF8ToString(method),request); },
    CloudSaveResolve_js__deps: ['$CMCloud'],
    CloudSaveResolve_js: function(key,useLocal) { CMCloud.resolve(UTF8ToString(key),!!useLocal); },
    YandexPlayerName_js__deps: ['$CMCloud'],
    YandexPlayerName_js: function(obj,method) {
        var objectName=UTF8ToString(obj),methodName=UTF8ToString(method);
        CMCloud.player().then(function(player){SendMessage(objectName,methodName,player.getName()||'');},function(){SendMessage(objectName,methodName,'');});
    }
});
