mergeInto(LibraryManager.library, {
    // 存储WebSocket实例的数组
    $webSockets: [],

    // 跟踪下一个可用的实例ID
    $nextInstanceId: 1,

    // 创建一个新的WebSocket实例
    WebSocketCreate: function(urlPtr) {
        var url = UTF8ToString(urlPtr);

        try {
            var socket = {
                socket: new WebSocket(url),
                buffer: new Uint8Array(0),
                error: null,
                instanceId: nextInstanceId++
            };

            var instanceId = socket.instanceId;

            socket.socket.binaryType = "arraybuffer";

            // 设置事件处理程序
            socket.socket.onopen = function() {
                if (socket.socket) {
                    dynCall('vi', Runtime.dynCall, [_WebSocketCallbacks_OnOpenCallback, instanceId]);
                }
            };

            socket.socket.onclose = function() {
                if (socket.socket) {
                    dynCall('vi', Runtime.dynCall, [_WebSocketCallbacks_OnCloseCallback, instanceId]);
                }
            };

            socket.socket.onerror = function(error) {
                if (socket.socket) {
                    var errorMsg = "WebSocket error";
                    var errorMsgPtr = allocate(intArrayFromString(errorMsg), 'i8', ALLOC_NORMAL);
                    dynCall('vii', Runtime.dynCall, [_WebSocketCallbacks_OnErrorCallback, instanceId, errorMsgPtr]);
                    _free(errorMsgPtr);
                }
            };

            socket.socket.onmessage = function(event) {
                if (socket.socket && event.data) {
                    if (event.data instanceof ArrayBuffer) {
                        var array = new Uint8Array(event.data);
                        var base64 = arrayBufferToBase64(array);
                        var base64Ptr = allocate(intArrayFromString(base64), 'i8', ALLOC_NORMAL);
                        dynCall('vii', Runtime.dynCall, [_WebSocketCallbacks_OnMessageCallback, instanceId, base64Ptr]);
                        _free(base64Ptr);
                    }
                }
            };

            webSockets[instanceId] = socket;
            return instanceId;
        } catch (err) {
            console.error("WebSocketCreate error: " + err.message);
            return -1;
        }
    },

    // 关闭并删除WebSocket实例
    WebSocketClose: function(instanceId) {
        var socket = webSockets[instanceId];
        if (socket && socket.socket) {
            socket.socket.close();
            delete webSockets[instanceId];
        }
    },

    // 发送二进制数据
    WebSocketSendData: function(instanceId, base64DataPtr) {
        var socket = webSockets[instanceId];
        if (!socket || !socket.socket) return;

        try {
            var base64Data = UTF8ToString(base64DataPtr);
            var binaryData = base64ToArrayBuffer(base64Data);
            socket.socket.send(binaryData);
        } catch (err) {
            console.error("WebSocketSendData error: " + err.message);
            var errorMsg = "Send error: " + err.message;
            var errorMsgPtr = allocate(intArrayFromString(errorMsg), 'i8', ALLOC_NORMAL);
            dynCall('vii', Runtime.dynCall, [_WebSocketCallbacks_OnErrorCallback, instanceId, errorMsgPtr]);
            _free(errorMsgPtr);
        }
    },

    // 辅助函数：ArrayBuffer转Base64
    $arrayBufferToBase64: function(buffer) {
        var binary = '';
        var bytes = new Uint8Array(buffer);
        for (var i = 0; i < bytes.byteLength; i++) {
            binary += String.fromCharCode(bytes[i]);
        }
        return btoa(binary);
    },

    // 辅助函数：Base64转ArrayBuffer
    $base64ToArrayBuffer: function(base64) {
        var binary_string = atob(base64);
        var len = binary_string.length;
        var bytes = new Uint8Array(len);
        for (var i = 0; i < len; i++) {
            bytes[i] = binary_string.charCodeAt(i);
        }
        return bytes.buffer;
    }
});
