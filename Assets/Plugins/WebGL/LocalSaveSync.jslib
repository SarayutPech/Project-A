// WebGL: ไฟล์ใน Application.persistentDataPath อยู่ใน memory (IDBFS) จนกว่าจะ sync ลง IndexedDB
// ไม่ sync = F5 แล้วเซฟหาย -> LocalJson.Flush() เรียกฟังก์ชันนี้หลังเขียนไฟล์ทุกครั้ง
// sync ซ้อนกันไม่ได้: ระหว่างกำลัง sync ถ้ามีเซฟใหม่ จะ sync อีกรอบหลังรอบนี้จบ
var LocalSaveSync = {
    $LocalSaveSyncState: { syncing: false, pending: false },

    ProjectA_SyncFS__deps: ['$LocalSaveSyncState'],
    ProjectA_SyncFS: function () {
        if (typeof FS === 'undefined' || !FS.syncfs) return;
        var state = LocalSaveSyncState;
        if (state.syncing) {
            state.pending = true;
            return;
        }
        state.syncing = true;
        var run = function () {
            FS.syncfs(false, function (err) {
                if (err) console.warn('[LocalSaveSync] syncfs failed', err);
                if (state.pending) {
                    state.pending = false;
                    run();
                } else {
                    state.syncing = false;
                }
            });
        };
        run();
    }
};

autoAddDeps(LocalSaveSync, '$LocalSaveSyncState');
mergeInto(LibraryManager.library, LocalSaveSync);
