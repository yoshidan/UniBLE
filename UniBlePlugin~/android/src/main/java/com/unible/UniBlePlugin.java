package com.unible;

import android.Manifest;
import android.annotation.SuppressLint;
import android.app.Activity;
import android.bluetooth.BluetoothAdapter;
import android.bluetooth.BluetoothDevice;
import android.bluetooth.BluetoothGatt;
import android.bluetooth.BluetoothGattCallback;
import android.bluetooth.BluetoothGattCharacteristic;
import android.bluetooth.BluetoothGattDescriptor;
import android.bluetooth.BluetoothGattService;
import android.bluetooth.BluetoothManager;
import android.bluetooth.BluetoothProfile;
import android.bluetooth.le.BluetoothLeScanner;
import android.bluetooth.le.ScanCallback;
import android.bluetooth.le.ScanFilter;
import android.bluetooth.le.ScanResult;
import android.bluetooth.le.ScanSettings;
import android.content.Context;
import android.content.pm.PackageManager;
import android.os.Build;
import android.os.Handler;
import android.os.Looper;
import android.os.ParcelUuid;
import android.util.Log;

import androidx.core.app.ActivityCompat;
import androidx.core.content.ContextCompat;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.UUID;

/**
 * Main BLE plugin class for Unity
 */
public class UniBlePlugin {
    private static final String TAG = "UniBlePlugin";
    private static final int PERMISSION_REQUEST_CODE = 1001;
    private static final UUID CCCD_UUID = UUID.fromString("00002902-0000-1000-8000-00805f9b34fb");

    private static UniBlePlugin instance;

    private Activity activity;
    private BluetoothManager bluetoothManager;
    private BluetoothAdapter bluetoothAdapter;
    private BluetoothLeScanner scanner;
    private UniBleCallback callback;
    private PermissionCallback permissionCallback;
    private Handler mainHandler;

    private final Map<String, BluetoothDevice> discoveredDevices = new HashMap<>();
    private final Map<String, BluetoothGatt> connectedGatts = new HashMap<>();
    private final Map<String, ConnectionCallback> connectionCallbacks = new HashMap<>();
    private final Map<String, ServiceDiscoveryCallback> serviceDiscoveryCallbacks = new HashMap<>();
    private final Map<String, ReadCallback> readCallbacks = new HashMap<>();
    private final Map<String, WriteCallback> writeCallbacks = new HashMap<>();
    private final Map<String, NotifyCallback> notifyCallbacks = new HashMap<>();

    private boolean isScanning = false;

    public static synchronized UniBlePlugin getInstance() {
        if (instance == null) {
            instance = new UniBlePlugin();
        }
        return instance;
    }

    private UniBlePlugin() {
        mainHandler = new Handler(Looper.getMainLooper());
    }

    /**
     * Initialize the plugin with Activity
     */
    public void initialize(Activity activity, UniBleCallback callback) {
        this.activity = activity;
        this.callback = callback;
        Context context = activity.getApplicationContext();
        bluetoothManager = (BluetoothManager) context.getSystemService(Context.BLUETOOTH_SERVICE);
        if (bluetoothManager != null) {
            bluetoothAdapter = bluetoothManager.getAdapter();
        }
    }

    /**
     * Check if BLE is available
     */
    public boolean isAvailable() {
        return bluetoothAdapter != null && bluetoothAdapter.isEnabled();
    }

    /**
     * Request required permissions
     */
    public void requestPermissions(PermissionCallback callback) {
        this.permissionCallback = callback;

        if (activity == null) {
            callback.onResult(false);
            return;
        }

        List<String> permissions = new ArrayList<>();

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            // Android 12+
            if (ContextCompat.checkSelfPermission(activity, Manifest.permission.BLUETOOTH_SCAN) != PackageManager.PERMISSION_GRANTED) {
                permissions.add(Manifest.permission.BLUETOOTH_SCAN);
            }
            if (ContextCompat.checkSelfPermission(activity, Manifest.permission.BLUETOOTH_CONNECT) != PackageManager.PERMISSION_GRANTED) {
                permissions.add(Manifest.permission.BLUETOOTH_CONNECT);
            }
        } else {
            // Android 11 and below
            if (ContextCompat.checkSelfPermission(activity, Manifest.permission.ACCESS_FINE_LOCATION) != PackageManager.PERMISSION_GRANTED) {
                permissions.add(Manifest.permission.ACCESS_FINE_LOCATION);
            }
        }

        if (permissions.isEmpty()) {
            callback.onResult(true);
        } else {
            ActivityCompat.requestPermissions(activity, permissions.toArray(new String[0]), PERMISSION_REQUEST_CODE);
        }
    }

    /**
     * Called when permission request result is received
     */
    public void onPermissionResult(boolean granted) {
        if (permissionCallback != null) {
            permissionCallback.onResult(granted);
            permissionCallback = null;
        }
    }

    /**
     * Start scanning for BLE devices
     */
    @SuppressLint("MissingPermission")
    public void startScan(String[] serviceUuids) {
        if (bluetoothAdapter == null || !bluetoothAdapter.isEnabled()) {
            Log.e(TAG, "Bluetooth is not available");
            return;
        }

        if (isScanning) {
            return;
        }

        scanner = bluetoothAdapter.getBluetoothLeScanner();
        if (scanner == null) {
            Log.e(TAG, "Scanner is not available");
            return;
        }

        discoveredDevices.clear();
        isScanning = true;

        List<ScanFilter> filters = new ArrayList<>();
        if (serviceUuids != null) {
            for (String uuid : serviceUuids) {
                ScanFilter filter = new ScanFilter.Builder()
                        .setServiceUuid(ParcelUuid.fromString(uuid))
                        .build();
                filters.add(filter);
            }
        }

        ScanSettings settings = new ScanSettings.Builder()
                .setScanMode(ScanSettings.SCAN_MODE_LOW_LATENCY)
                .build();

        scanner.startScan(filters.isEmpty() ? null : filters, settings, scanCallback);
    }

    /**
     * Stop scanning for BLE devices
     */
    @SuppressLint("MissingPermission")
    public void stopScan() {
        if (!isScanning || scanner == null) {
            return;
        }

        isScanning = false;
        scanner.stopScan(scanCallback);
    }

    /**
     * Get a device by ID
     */
    public BluetoothDevice getDevice(String deviceId) {
        return discoveredDevices.get(deviceId);
    }

    /**
     * Connect to a device
     */
    @SuppressLint("MissingPermission")
    public void connect(String deviceId, ConnectionCallback callback) {
        BluetoothDevice device = discoveredDevices.get(deviceId);
        if (device == null && bluetoothAdapter != null) {
            try {
                device = bluetoothAdapter.getRemoteDevice(deviceId);
            } catch (IllegalArgumentException e) {
                callback.onConnectionFailed("Invalid device address: " + deviceId);
                return;
            }
        }

        if (device == null) {
            callback.onConnectionFailed("Device not found");
            return;
        }

        connectionCallbacks.put(deviceId, callback);

        BluetoothGatt gatt = device.connectGatt(
                activity,
                false,
                gattCallback,
                BluetoothDevice.TRANSPORT_LE
        );

        if (gatt == null) {
            connectionCallbacks.remove(deviceId);
            callback.onConnectionFailed("Failed to create GATT connection");
        }
    }

    /**
     * Disconnect from a device
     */
    @SuppressLint("MissingPermission")
    public void disconnect(String deviceId) {
        BluetoothGatt gatt = connectedGatts.get(deviceId);
        if (gatt != null) {
            gatt.disconnect();
            gatt.close();
            connectedGatts.remove(deviceId);
        }
    }

    /**
     * Discover services for a device
     */
    @SuppressLint("MissingPermission")
    public void discoverServices(String deviceId, ServiceDiscoveryCallback callback) {
        BluetoothGatt gatt = connectedGatts.get(deviceId);
        if (gatt == null) {
            callback.onServiceDiscoveryFailed("Device not connected");
            return;
        }

        serviceDiscoveryCallbacks.put(deviceId, callback);

        if (!gatt.discoverServices()) {
            serviceDiscoveryCallbacks.remove(deviceId);
            callback.onServiceDiscoveryFailed("Failed to start service discovery");
        }
    }

    /**
     * Get services for a device (after discovery)
     */
    public BluetoothGattService[] getServices(String deviceId) {
        BluetoothGatt gatt = connectedGatts.get(deviceId);
        if (gatt == null) {
            return new BluetoothGattService[0];
        }
        List<BluetoothGattService> services = gatt.getServices();
        return services.toArray(new BluetoothGattService[0]);
    }

    /**
     * Get characteristics for a service
     */
    public BluetoothGattCharacteristic[] getCharacteristics(String deviceId, String serviceUuid) {
        BluetoothGatt gatt = connectedGatts.get(deviceId);
        if (gatt == null) {
            return new BluetoothGattCharacteristic[0];
        }
        BluetoothGattService service = gatt.getService(UUID.fromString(serviceUuid));
        if (service == null) {
            return new BluetoothGattCharacteristic[0];
        }
        List<BluetoothGattCharacteristic> characteristics = service.getCharacteristics();
        return characteristics.toArray(new BluetoothGattCharacteristic[0]);
    }

    /**
     * Read a characteristic
     */
    @SuppressLint("MissingPermission")
    public void readCharacteristic(String deviceId, String serviceUuid, String characteristicUuid, ReadCallback callback) {
        BluetoothGatt gatt = connectedGatts.get(deviceId);
        if (gatt == null) {
            callback.onError("Device not connected");
            return;
        }

        BluetoothGattCharacteristic characteristic = findCharacteristic(gatt, serviceUuid, characteristicUuid);
        if (characteristic == null) {
            callback.onError("Characteristic not found");
            return;
        }

        String key = deviceId + "_" + characteristicUuid;
        readCallbacks.put(key, callback);

        if (!gatt.readCharacteristic(characteristic)) {
            readCallbacks.remove(key);
            callback.onError("Failed to read characteristic");
        }
    }

    /**
     * Write to a characteristic
     */
    @SuppressLint("MissingPermission")
    public void writeCharacteristic(String deviceId, String serviceUuid, String characteristicUuid, byte[] data, boolean withResponse, WriteCallback callback) {
        BluetoothGatt gatt = connectedGatts.get(deviceId);
        if (gatt == null) {
            callback.onError("Device not connected");
            return;
        }

        BluetoothGattCharacteristic characteristic = findCharacteristic(gatt, serviceUuid, characteristicUuid);
        if (characteristic == null) {
            callback.onError("Characteristic not found");
            return;
        }

        String key = deviceId + "_" + characteristicUuid;
        writeCallbacks.put(key, callback);

        characteristic.setValue(data);
        characteristic.setWriteType(withResponse ?
                BluetoothGattCharacteristic.WRITE_TYPE_DEFAULT :
                BluetoothGattCharacteristic.WRITE_TYPE_NO_RESPONSE);

        if (!gatt.writeCharacteristic(characteristic)) {
            writeCallbacks.remove(key);
            callback.onError("Failed to write characteristic");
        }
    }

    /**
     * Subscribe to a characteristic
     */
    @SuppressLint("MissingPermission")
    public void subscribeCharacteristic(String deviceId, String serviceUuid, String characteristicUuid, NotifyCallback notifyCallback, SubscribeResultCallback resultCallback) {
        BluetoothGatt gatt = connectedGatts.get(deviceId);
        if (gatt == null) {
            resultCallback.onError("Device not connected");
            return;
        }

        BluetoothGattCharacteristic characteristic = findCharacteristic(gatt, serviceUuid, characteristicUuid);
        if (characteristic == null) {
            resultCallback.onError("Characteristic not found");
            return;
        }

        String key = deviceId + "_" + characteristicUuid;
        notifyCallbacks.put(key, notifyCallback);

        if (!gatt.setCharacteristicNotification(characteristic, true)) {
            notifyCallbacks.remove(key);
            resultCallback.onError("Failed to enable notification");
            return;
        }

        BluetoothGattDescriptor descriptor = characteristic.getDescriptor(CCCD_UUID);
        if (descriptor != null) {
            int properties = characteristic.getProperties();
            byte[] value = (properties & BluetoothGattCharacteristic.PROPERTY_INDICATE) != 0 ?
                    BluetoothGattDescriptor.ENABLE_INDICATION_VALUE :
                    BluetoothGattDescriptor.ENABLE_NOTIFICATION_VALUE;
            descriptor.setValue(value);
            gatt.writeDescriptor(descriptor);
        }

        resultCallback.onSuccess();
    }

    /**
     * Unsubscribe from a characteristic
     */
    @SuppressLint("MissingPermission")
    public void unsubscribeCharacteristic(String deviceId, String serviceUuid, String characteristicUuid, SubscribeResultCallback resultCallback) {
        BluetoothGatt gatt = connectedGatts.get(deviceId);
        if (gatt == null) {
            resultCallback.onError("Device not connected");
            return;
        }

        BluetoothGattCharacteristic characteristic = findCharacteristic(gatt, serviceUuid, characteristicUuid);
        if (characteristic == null) {
            resultCallback.onError("Characteristic not found");
            return;
        }

        String key = deviceId + "_" + characteristicUuid;
        notifyCallbacks.remove(key);

        gatt.setCharacteristicNotification(characteristic, false);

        BluetoothGattDescriptor descriptor = characteristic.getDescriptor(CCCD_UUID);
        if (descriptor != null) {
            descriptor.setValue(BluetoothGattDescriptor.DISABLE_NOTIFICATION_VALUE);
            gatt.writeDescriptor(descriptor);
        }

        resultCallback.onSuccess();
    }

    private BluetoothGattCharacteristic findCharacteristic(BluetoothGatt gatt, String serviceUuid, String characteristicUuid) {
        if (serviceUuid != null && !serviceUuid.isEmpty()) {
            BluetoothGattService service = gatt.getService(UUID.fromString(serviceUuid));
            if (service != null) {
                return service.getCharacteristic(UUID.fromString(characteristicUuid));
            }
        }

        // Search all services
        UUID uuid = UUID.fromString(characteristicUuid);
        for (BluetoothGattService service : gatt.getServices()) {
            BluetoothGattCharacteristic characteristic = service.getCharacteristic(uuid);
            if (characteristic != null) {
                return characteristic;
            }
        }
        return null;
    }

    private final ScanCallback scanCallback = new ScanCallback() {
        @SuppressLint("MissingPermission")
        @Override
        public void onScanResult(int callbackType, ScanResult result) {
            BluetoothDevice device = result.getDevice();
            String deviceId = device.getAddress();
            String deviceName = device.getName();

            if (!discoveredDevices.containsKey(deviceId)) {
                discoveredDevices.put(deviceId, device);
                if (callback != null) {
                    callback.onDeviceDiscovered(deviceId, deviceName != null ? deviceName : "", device);
                }
            }
        }

        @Override
        public void onScanFailed(int errorCode) {
            Log.e(TAG, "Scan failed with error code: " + errorCode);
            isScanning = false;
        }
    };

    private final BluetoothGattCallback gattCallback = new BluetoothGattCallback() {
        @SuppressLint("MissingPermission")
        @Override
        public void onConnectionStateChange(BluetoothGatt gatt, int status, int newState) {
            String deviceId = gatt.getDevice().getAddress();

            if (newState == BluetoothProfile.STATE_CONNECTED) {
                connectedGatts.put(deviceId, gatt);
                ConnectionCallback callback = connectionCallbacks.remove(deviceId);
                if (callback != null) {
                    mainHandler.post(callback::onConnected);
                }
            } else if (newState == BluetoothProfile.STATE_DISCONNECTED) {
                connectedGatts.remove(deviceId);
                ConnectionCallback callback = connectionCallbacks.remove(deviceId);
                if (callback != null) {
                    if (status == BluetoothGatt.GATT_SUCCESS) {
                        mainHandler.post(callback::onDisconnected);
                    } else {
                        final String error = "Connection failed with status: " + status;
                        mainHandler.post(() -> callback.onConnectionFailed(error));
                    }
                }
                gatt.close();
            }
        }

        @Override
        public void onServicesDiscovered(BluetoothGatt gatt, int status) {
            String deviceId = gatt.getDevice().getAddress();
            ServiceDiscoveryCallback callback = serviceDiscoveryCallbacks.remove(deviceId);

            if (callback == null) return;

            if (status == BluetoothGatt.GATT_SUCCESS) {
                List<BluetoothGattService> services = gatt.getServices();
                BluetoothGattService[] serviceArray = services.toArray(new BluetoothGattService[0]);
                mainHandler.post(() -> callback.onServicesDiscovered(serviceArray));
            } else {
                final String error = "Service discovery failed with status: " + status;
                mainHandler.post(() -> callback.onServiceDiscoveryFailed(error));
            }
        }

        @Override
        public void onCharacteristicRead(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic, int status) {
            String deviceId = gatt.getDevice().getAddress();
            String charUuid = characteristic.getUuid().toString();
            String key = deviceId + "_" + charUuid;

            ReadCallback callback = readCallbacks.remove(key);
            if (callback == null) return;

            if (status == BluetoothGatt.GATT_SUCCESS) {
                byte[] value = characteristic.getValue();
                mainHandler.post(() -> callback.onSuccess(value));
            } else {
                final String error = "Read failed with status: " + status;
                mainHandler.post(() -> callback.onError(error));
            }
        }

        @Override
        public void onCharacteristicWrite(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic, int status) {
            String deviceId = gatt.getDevice().getAddress();
            String charUuid = characteristic.getUuid().toString();
            String key = deviceId + "_" + charUuid;

            WriteCallback callback = writeCallbacks.remove(key);
            if (callback == null) return;

            if (status == BluetoothGatt.GATT_SUCCESS) {
                mainHandler.post(callback::onSuccess);
            } else {
                final String error = "Write failed with status: " + status;
                mainHandler.post(() -> callback.onError(error));
            }
        }

        @Override
        public void onCharacteristicChanged(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic) {
            String deviceId = gatt.getDevice().getAddress();
            String charUuid = characteristic.getUuid().toString();
            String key = deviceId + "_" + charUuid;

            NotifyCallback callback = notifyCallbacks.get(key);
            if (callback != null) {
                byte[] value = characteristic.getValue();
                mainHandler.post(() -> callback.onNotify(value));
            }
        }
    };
}
