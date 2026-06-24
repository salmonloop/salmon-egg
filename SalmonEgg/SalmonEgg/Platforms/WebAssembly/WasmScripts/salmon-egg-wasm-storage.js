let persistentStorageReady;

export function loadFileSystem() {
    return syncFileSystem(true);
}

export function flushFileSystem() {
    return syncFileSystem(false);
}

function syncFileSystem(populateFromBackingStore) {
    const storageFolder = globalThis.Windows?.Storage?.StorageFolder;
    if (!storageFolder || typeof storageFolder.synchronizeFileSystem !== "function") {
        return Promise.reject(new Error("Uno StorageFolder file system synchronization is not available in this WebAssembly runtime."));
    }

    persistentStorageReady ??= ensurePersistentStorageReady(storageFolder);

    return persistentStorageReady.then(() => new Promise((resolve, reject) => {
        const synchronizeWhenIdle = () => {
            if (storageFolder._isSynchronizing) {
                globalThis.setTimeout(synchronizeWhenIdle, 25);
                return;
            }

            try {
                storageFolder.synchronizeFileSystem(populateFromBackingStore, error => {
                    if (error) {
                        reject(error);
                        return;
                    }

                    resolve();
                });
            } catch (error) {
                reject(error);
            }
        };

        synchronizeWhenIdle();
    }));
}

function ensurePersistentStorageReady(storageFolder) {
    if (storageFolder._isInitialized) {
        return Promise.resolve();
    }

    if (typeof storageFolder.makePersistent !== "function") {
        return Promise.reject(new Error("Uno StorageFolder persistent filesystem setup is not available in this WebAssembly runtime."));
    }

    return storageFolder.makePersistent(["/local"]).catch(error => {
        const message = error?.message ?? String(error);
        if (/file exists/i.test(message)) {
            return;
        }

        throw error;
    });
}
