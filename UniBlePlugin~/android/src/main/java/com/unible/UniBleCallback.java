package com.unible;

import android.bluetooth.BluetoothDevice;

/**
 * Callback interface for BLE events
 */
public interface UniBleCallback {
    void onDeviceDiscovered(String deviceId, String deviceName, BluetoothDevice device);
    void onStateChanged(int state);
}
