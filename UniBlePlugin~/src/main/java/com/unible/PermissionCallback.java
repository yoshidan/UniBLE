package com.unible;

/**
 * Callback interface for permission request result
 */
public interface PermissionCallback {
    void onResult(boolean granted);
}
