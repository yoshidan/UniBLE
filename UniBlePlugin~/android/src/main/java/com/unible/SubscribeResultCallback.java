package com.unible;

/**
 * Callback interface for subscribe/unsubscribe result
 */
public interface SubscribeResultCallback {
    void onSuccess();
    void onError(String error);
}
