package com.unible;

/**
 * Callback interface for write operations
 */
public interface WriteCallback {
    void onSuccess();
    void onError(String error);
}
