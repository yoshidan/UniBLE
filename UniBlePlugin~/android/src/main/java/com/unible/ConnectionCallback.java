package com.unible;

/**
 * Callback interface for connection events
 */
public interface ConnectionCallback {
    void onConnected();
    void onConnectionFailed(String error);
    void onDisconnected();
}
