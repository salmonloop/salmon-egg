export function loadFileSystem() {
    return syncFileSystem(true);
}

export function flushFileSystem() {
    return syncFileSystem(false);
}

function syncFileSystem(populateFromBackingStore) {
    return new Promise((resolve, reject) => {
        const storageFolder = globalThis.Windows?.Storage?.StorageFolder;
        if (!storageFolder || typeof storageFolder.synchronizeFileSystem !== "function") {
            reject(new Error("Uno StorageFolder file system synchronization is not available in this WebAssembly runtime."));
            return;
        }

        const synchronizeWhenIdle = () => {
            if (storageFolder._isSynchronizing) {
                globalThis.setTimeout(synchronizeWhenIdle, 25);
                return;
            }

            try {
                storageFolder.synchronizeFileSystem(populateFromBackingStore, resolve);
            } catch (error) {
                reject(error);
            }
        };

        synchronizeWhenIdle();
    });
}
