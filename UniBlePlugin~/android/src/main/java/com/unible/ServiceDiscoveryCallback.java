package com.unible;

import android.bluetooth.BluetoothGattService;

/**
 * Callback interface for service discovery events
 */
public interface ServiceDiscoveryCallback {
    void onServicesDiscovered(BluetoothGattService[] services);
    void onServiceDiscoveryFailed(String error);
}
