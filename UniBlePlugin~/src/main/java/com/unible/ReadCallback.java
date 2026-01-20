package com.unible;

/**
 * Callback interface for read operations
 */
public interface ReadCallback {
    void onSuccess(byte[] data);
    void onError(String error);
}
