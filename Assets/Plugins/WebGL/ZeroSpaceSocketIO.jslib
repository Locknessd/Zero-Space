mergeInto(LibraryManager.library, {
    $ZeroSpaceSocketIO: {
        connections: {},
        libraryPromise: null,
        loadLibrary: function (url) {
            if (typeof window.io === "function") return Promise.resolve(window.io);
            if (!ZeroSpaceSocketIO.libraryPromise) {
                ZeroSpaceSocketIO.libraryPromise = new Promise(function (resolve, reject) {
                    var script = document.createElement("script");
                    script.src = url;
                    script.onload = function () {
                        if (typeof window.io === "function") resolve(window.io);
                        else reject(new Error("Socket.IO browser library did not initialize."));
                    };
                    script.onerror = function () {
                        reject(new Error("Unable to load the bundled Socket.IO browser library: " + url));
                    };
                    document.head.appendChild(script);
                }).catch(function (error) {
                    ZeroSpaceSocketIO.libraryPromise = null;
                    throw error;
                });
            }
            return ZeroSpaceSocketIO.libraryPromise;
        },
        send: function (entry, eventName, data) {
            if (ZeroSpaceSocketIO.connections[entry.id] !== entry) return;
            SendMessage(entry.receiver, "OnWebGLSocketEvent", JSON.stringify({
                connectionId: entry.id,
                eventName: eventName,
                data: data || ""
            }));
        },
        close: function (id) {
            var entry = ZeroSpaceSocketIO.connections[id];
            if (!entry) return;
            delete ZeroSpaceSocketIO.connections[id];
            if (entry.reconnectTimer) clearTimeout(entry.reconnectTimer);
            if (entry.socket) {
                entry.socket.removeAllListeners();
                entry.socket.io.removeAllListeners();
                entry.socket.disconnect();
            }
        }
    },

    ZeroSpaceSocketIO_Connect__deps: ["$ZeroSpaceSocketIO"],
    ZeroSpaceSocketIO_Connect: function (idPtr, receiverPtr, urlPtr, optionsPtr, libraryUrlPtr) {
        var id = UTF8ToString(idPtr);
        var url = UTF8ToString(urlPtr);
        var options = JSON.parse(UTF8ToString(optionsPtr));
        var libraryUrl = UTF8ToString(libraryUrlPtr);
        ZeroSpaceSocketIO.close(id);
        var entry = { id: id, receiver: UTF8ToString(receiverPtr), socket: null, reconnectTimer: null };
        ZeroSpaceSocketIO.connections[id] = entry;
        ZeroSpaceSocketIO.loadLibrary(libraryUrl).then(function (io) {
            if (ZeroSpaceSocketIO.connections[id] !== entry) return;
            var socket = io(url, options);
            entry.socket = socket;
            socket.on("connect", function () { ZeroSpaceSocketIO.send(entry, "connect"); });
            socket.on("connect_error", function (error) {
                ZeroSpaceSocketIO.send(entry, socket.active ? "connect_error" : "fatal_error", error.message);
            });
            socket.on("disconnect", function (reason) {
                ZeroSpaceSocketIO.send(entry, "disconnect", reason);
                // Server-initiated disconnects require an explicit connect call.
                if (options.reconnection && reason === "io server disconnect") {
                    entry.reconnectTimer = setTimeout(function () {
                        if (ZeroSpaceSocketIO.connections[id] === entry) socket.connect();
                    }, options.reconnectionDelay);
                }
            });
            socket.io.on("reconnect", function (attempt) {
                ZeroSpaceSocketIO.send(entry, "reconnect", String(attempt));
            });
            socket.io.on("reconnect_attempt", function (attempt) {
                ZeroSpaceSocketIO.send(entry, "reconnect_attempt", String(attempt));
            });
            socket.io.on("reconnect_failed", function () {
                ZeroSpaceSocketIO.send(entry, "reconnect_failed");
            });
            ["meme_battle_snapshot", "meme_battle_start_result", "meme_battle_event",
                "meme_battle_battle_result"].forEach(function (eventName) {
                socket.on(eventName, function () {
                    var args = Array.prototype.slice.call(arguments);
                    ZeroSpaceSocketIO.send(entry, eventName, JSON.stringify(args));
                });
            });
            socket.connect();
        }).catch(function (error) {
            ZeroSpaceSocketIO.send(entry, "fatal_error", error.message);
        });
    },

    ZeroSpaceSocketIO_Emit__deps: ["$ZeroSpaceSocketIO"],
    ZeroSpaceSocketIO_Emit: function (idPtr, eventPtr, argsPtr) {
        var entry = ZeroSpaceSocketIO.connections[UTF8ToString(idPtr)];
        if (!entry || !entry.socket || !entry.socket.connected) return;
        var args = JSON.parse(UTF8ToString(argsPtr));
        args.unshift(UTF8ToString(eventPtr));
        entry.socket.emit.apply(entry.socket, args);
    },

    ZeroSpaceSocketIO_Disconnect__deps: ["$ZeroSpaceSocketIO"],
    ZeroSpaceSocketIO_Disconnect: function (idPtr) {
        ZeroSpaceSocketIO.close(UTF8ToString(idPtr));
    }
});
