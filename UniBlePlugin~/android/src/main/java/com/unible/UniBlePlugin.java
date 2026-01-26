package com.unible;

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
import android.os.Build;
import android.os.Handler;
import android.os.Looper;
import android.os.ParcelUuid;
import android.util.Log;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.HashSet;
import java.util.Iterator;
import java.util.LinkedList;
import java.util.List;
import java.util.Map;
import java.util.Queue;
import java.util.Set;
import java.util.UUID;

/**
 * Main BLE plugin class for Unity
 */
public class UniBlePlugin {
    private static final String TAG = "UniBlePlugin";
    private static final UUID CCCD_UUID = UUID.fromString("00002902-0000-1000-8000-00805f9b34fb");

    private static UniBlePlugin instance;

    private Activity activity;
    private BluetoothManager bluetoothManager;
    private BluetoothAdapter bluetoothAdapter;
    private BluetoothLeScanner scanner;
    private UniBleCallback callback;
    private Handler mainHandler;

    private final Map<String, BluetoothDevice> discoveredDevices = new HashMap<>();
    private final Map<String, BluetoothGatt> connectedGatts = new HashMap<>();
    private final Map<String, ConnectionCallback> connectionCallbacks = new HashMap<>();
    private final Map<String, ServiceDiscoveryCallback> serviceDiscoveryCallbacks = new HashMap<>();
    private final Map<String, ReadCallback> readCallbacks = new HashMap<>();
    private final Map<String, WriteCallback> writeCallbacks = new HashMap<>();
    private final Map<String, NotifyCallback> notifyCallbacks = new HashMap<>();
    private final Map<String, SubscribeResultCallback> subscribeResultCallbacks = new HashMap<>();
    private final Map<String, GattQueue> gattQueues = new HashMap<>();
    private final Set<String> pendingDisconnects = new HashSet<>();

    private boolean isScanning = false;

    // --- GattQueue: serializes GATT operations per device (Bug 5) ---
    private static class GattQueue {
        private final Queue<Runnable> queue = new LinkedList<>();
        private boolean busy = false;

        void enqueue(Runnable operation) {
            Runnable toRun = null;
            synchronized (this) {
                queue.add(operation);
                if (!busy) {
                    busy = true;
                    toRun = queue.poll();
                }
            }
            if (toRun != null) {
                toRun.run();
            }
        }

        void complete() {
            Runnable toRun;
            synchronized (this) {
                toRun = queue.poll();
                if (toRun == null) {
                    busy = false;
                }
            }
            if (toRun != null) {
                toRun.run();
            }
        }
    }

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

    // Bug 1: requestPermissions and onPermissionResult removed.
    // Permission requests are now handled from C# via UnityEngine.Android.Permission.

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
     * Disconnect from a device (Bug 2: only calls gatt.disconnect(), cleanup deferred to callback)
     */
    @SuppressLint("MissingPermission")
    public void disconnect(String deviceId) {
        BluetoothGatt gatt = connectedGatts.get(deviceId);
        if (gatt != null) {
            pendingDisconnects.add(deviceId);
            gatt.disconnect();
        }
    }

    /**
     * Discover services for a device (Bug 5: queued)
     */
    @SuppressLint("MissingPermission")
    public void discoverServices(String deviceId, ServiceDiscoveryCallback callback) {
        BluetoothGatt gatt = connectedGatts.get(deviceId);
        if (gatt == null) {
            callback.onServiceDiscoveryFailed("Device not connected");
            return;
        }

        GattQueue queue = gattQueues.get(deviceId);
        if (queue == null) {
            callback.onServiceDiscoveryFailed("Device not connected");
            return;
        }

        serviceDiscoveryCallbacks.put(deviceId, callback);

        queue.enqueue(() -> {
            if (!gatt.discoverServices()) {
                serviceDiscoveryCallbacks.remove(deviceId);
                queue.complete();
                mainHandler.post(() -> callback.onServiceDiscoveryFailed("Failed to start service discovery"));
            }
        });
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
     * Read a characteristic (Bug 5: queued)
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

        GattQueue queue = gattQueues.get(deviceId);
        if (queue == null) {
            readCallbacks.remove(key);
            callback.onError("Device not connected");
            return;
        }

        queue.enqueue(() -> {
            if (!gatt.readCharacteristic(characteristic)) {
                readCallbacks.remove(key);
                queue.complete();
                mainHandler.post(() -> callback.onError("Failed to read characteristic"));
            }
        });
    }

    /**
     * Write to a characteristic (Bug 4: API 33+ path, Bug 5: queued)
     */
    @SuppressLint("MissingPermission")
    @SuppressWarnings("deprecation")
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

        int writeType = withResponse ?
                BluetoothGattCharacteristic.WRITE_TYPE_DEFAULT :
                BluetoothGattCharacteristic.WRITE_TYPE_NO_RESPONSE;

        GattQueue queue = gattQueues.get(deviceId);
        if (queue == null) {
            writeCallbacks.remove(key);
            callback.onError("Device not connected");
            return;
        }

        queue.enqueue(() -> {
            boolean started;
            if (Build.VERSION.SDK_INT >= 33) {
                started = gatt.writeCharacteristic(characteristic, data, writeType) == 0;
            } else {
                characteristic.setValue(data);
                characteristic.setWriteType(writeType);
                started = gatt.writeCharacteristic(characteristic);
            }
            if (!started) {
                writeCallbacks.remove(key);
                queue.complete();
                mainHandler.post(() -> callback.onError("Failed to write characteristic"));
            }
        });
    }

    /**
     * Subscribe to a characteristic (Bug 3: deferred callback, Bug 4: API 33+, Bug 5: queued)
     */
    @SuppressLint("MissingPermission")
    @SuppressWarnings("deprecation")
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
        if (descriptor == null) {
            // No CCCD descriptor; local notification set, done immediately
            resultCallback.onSuccess();
            return;
        }

        // Bug 3: store result callback, defer onSuccess to onDescriptorWrite
        subscribeResultCallbacks.put(key, resultCallback);

        int properties = characteristic.getProperties();
        byte[] value = (properties & BluetoothGattCharacteristic.PROPERTY_INDICATE) != 0 ?
                BluetoothGattDescriptor.ENABLE_INDICATION_VALUE :
                BluetoothGattDescriptor.ENABLE_NOTIFICATION_VALUE;

        GattQueue queue = gattQueues.get(deviceId);
        if (queue == null) {
            subscribeResultCallbacks.remove(key);
            notifyCallbacks.remove(key);
            resultCallback.onError("Device not connected");
            return;
        }

        queue.enqueue(() -> {
            boolean started;
            if (Build.VERSION.SDK_INT >= 33) {
                started = gatt.writeDescriptor(descriptor, value) == 0;
            } else {
                descriptor.setValue(value);
                started = gatt.writeDescriptor(descriptor);
            }
            if (!started) {
                subscribeResultCallbacks.remove(key);
                notifyCallbacks.remove(key);
                queue.complete();
                mainHandler.post(() -> resultCallback.onError("Failed to write CCCD descriptor"));
            }
        });
    }

    /**
     * Unsubscribe from a characteristic (Bug 3: deferred callback, Bug 4: API 33+, Bug 5: queued)
     */
    @SuppressLint("MissingPermission")
    @SuppressWarnings("deprecation")
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
        if (descriptor == null) {
            resultCallback.onSuccess();
            return;
        }

        subscribeResultCallbacks.put(key, resultCallback);

        GattQueue queue = gattQueues.get(deviceId);
        if (queue == null) {
            subscribeResultCallbacks.remove(key);
            resultCallback.onError("Device not connected");
            return;
        }

        queue.enqueue(() -> {
            boolean started;
            if (Build.VERSION.SDK_INT >= 33) {
                started = gatt.writeDescriptor(descriptor, BluetoothGattDescriptor.DISABLE_NOTIFICATION_VALUE) == 0;
            } else {
                descriptor.setValue(BluetoothGattDescriptor.DISABLE_NOTIFICATION_VALUE);
                started = gatt.writeDescriptor(descriptor);
            }
            if (!started) {
                subscribeResultCallbacks.remove(key);
                queue.complete();
                mainHandler.post(() -> resultCallback.onError("Failed to write CCCD descriptor"));
            }
        });
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

    /**
     * Fail and remove all pending operation callbacks for a device on disconnect.
     */
    private void failPendingCallbacks(String deviceId) {
        String prefix = deviceId + "_";
        String err = "Device disconnected";

        ServiceDiscoveryCallback sdCb = serviceDiscoveryCallbacks.remove(deviceId);
        if (sdCb != null) {
            mainHandler.post(() -> sdCb.onServiceDiscoveryFailed(err));
        }

        Iterator<Map.Entry<String, ReadCallback>> readIter = readCallbacks.entrySet().iterator();
        while (readIter.hasNext()) {
            Map.Entry<String, ReadCallback> entry = readIter.next();
            if (entry.getKey().startsWith(prefix)) {
                ReadCallback cb = entry.getValue();
                readIter.remove();
                mainHandler.post(() -> cb.onError(err));
            }
        }

        Iterator<Map.Entry<String, WriteCallback>> writeIter = writeCallbacks.entrySet().iterator();
        while (writeIter.hasNext()) {
            Map.Entry<String, WriteCallback> entry = writeIter.next();
            if (entry.getKey().startsWith(prefix)) {
                WriteCallback cb = entry.getValue();
                writeIter.remove();
                mainHandler.post(() -> cb.onError(err));
            }
        }

        Iterator<Map.Entry<String, SubscribeResultCallback>> subIter = subscribeResultCallbacks.entrySet().iterator();
        while (subIter.hasNext()) {
            Map.Entry<String, SubscribeResultCallback> entry = subIter.next();
            if (entry.getKey().startsWith(prefix)) {
                SubscribeResultCallback cb = entry.getValue();
                subIter.remove();
                mainHandler.post(() -> cb.onError(err));
            }
        }

        Iterator<Map.Entry<String, NotifyCallback>> notifyIter = notifyCallbacks.entrySet().iterator();
        while (notifyIter.hasNext()) {
            Map.Entry<String, NotifyCallback> entry = notifyIter.next();
            if (entry.getKey().startsWith(prefix)) {
                notifyIter.remove();
            }
        }
    }

    // --- Scan callback ---

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

    // --- GATT callback ---

    private final BluetoothGattCallback gattCallback = new BluetoothGattCallback() {

        @SuppressLint("MissingPermission")
        @Override
        public void onConnectionStateChange(BluetoothGatt gatt, int status, int newState) {
            String deviceId = gatt.getDevice().getAddress();

            if (newState == BluetoothProfile.STATE_CONNECTED) {
                connectedGatts.put(deviceId, gatt);
                gattQueues.put(deviceId, new GattQueue());
                ConnectionCallback cb = connectionCallbacks.remove(deviceId);
                if (cb != null) {
                    mainHandler.post(cb::onConnected);
                }
            } else if (newState == BluetoothProfile.STATE_DISCONNECTED) {
                connectedGatts.remove(deviceId);
                gattQueues.remove(deviceId);
                failPendingCallbacks(deviceId);
                gatt.close();

                boolean wasPendingDisconnect = pendingDisconnects.remove(deviceId);
                ConnectionCallback connCb = connectionCallbacks.remove(deviceId);

                if (connCb != null) {
                    // Disconnect during initial connection phase
                    if (status == BluetoothGatt.GATT_SUCCESS) {
                        mainHandler.post(connCb::onDisconnected);
                    } else {
                        final String error = "Connection failed with status: " + status;
                        mainHandler.post(() -> connCb.onConnectionFailed(error));
                    }
                } else {
                    // Post-connection disconnect (intentional or unexpected)
                    final String error = (!wasPendingDisconnect && status != BluetoothGatt.GATT_SUCCESS)
                            ? "Disconnected with status: " + status
                            : null;
                    if (UniBlePlugin.this.callback != null) {
                        mainHandler.post(() -> UniBlePlugin.this.callback.onDeviceDisconnected(deviceId, error));
                    }
                }
            }
        }

        @Override
        public void onServicesDiscovered(BluetoothGatt gatt, int status) {
            String deviceId = gatt.getDevice().getAddress();
            GattQueue queue = gattQueues.get(deviceId);
            if (queue != null) queue.complete();

            ServiceDiscoveryCallback cb = serviceDiscoveryCallbacks.remove(deviceId);
            if (cb == null) return;

            if (status == BluetoothGatt.GATT_SUCCESS) {
                List<BluetoothGattService> services = gatt.getServices();
                BluetoothGattService[] serviceArray = services.toArray(new BluetoothGattService[0]);
                mainHandler.post(() -> cb.onServicesDiscovered(serviceArray));
            } else {
                final String error = "Service discovery failed with status: " + status;
                mainHandler.post(() -> cb.onServiceDiscoveryFailed(error));
            }
        }

        // Pre-API 33 callback
        @SuppressWarnings("deprecation")
        @Override
        public void onCharacteristicRead(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic, int status) {
            handleCharacteristicRead(gatt, characteristic.getUuid().toString(), characteristic.getValue(), status);
        }

        // API 33+ callback
        @Override
        public void onCharacteristicRead(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic, byte[] value, int status) {
            handleCharacteristicRead(gatt, characteristic.getUuid().toString(), value, status);
        }

        private void handleCharacteristicRead(BluetoothGatt gatt, String charUuid, byte[] value, int status) {
            String deviceId = gatt.getDevice().getAddress();
            GattQueue queue = gattQueues.get(deviceId);
            if (queue != null) queue.complete();

            String key = deviceId + "_" + charUuid;
            ReadCallback cb = readCallbacks.remove(key);
            if (cb == null) return;

            if (status == BluetoothGatt.GATT_SUCCESS) {
                mainHandler.post(() -> cb.onSuccess(value));
            } else {
                final String error = "Read failed with status: " + status;
                mainHandler.post(() -> cb.onError(error));
            }
        }

        @Override
        public void onCharacteristicWrite(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic, int status) {
            String deviceId = gatt.getDevice().getAddress();
            GattQueue queue = gattQueues.get(deviceId);
            if (queue != null) queue.complete();

            String charUuid = characteristic.getUuid().toString();
            String key = deviceId + "_" + charUuid;

            WriteCallback cb = writeCallbacks.remove(key);
            if (cb == null) return;

            if (status == BluetoothGatt.GATT_SUCCESS) {
                mainHandler.post(cb::onSuccess);
            } else {
                final String error = "Write failed with status: " + status;
                mainHandler.post(() -> cb.onError(error));
            }
        }

        // Pre-API 33 callback
        @SuppressWarnings("deprecation")
        @Override
        public void onCharacteristicChanged(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic) {
            handleCharacteristicChanged(gatt, characteristic.getUuid().toString(), characteristic.getValue());
        }

        // API 33+ callback
        @Override
        public void onCharacteristicChanged(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic, byte[] value) {
            handleCharacteristicChanged(gatt, characteristic.getUuid().toString(), value);
        }

        private void handleCharacteristicChanged(BluetoothGatt gatt, String charUuid, byte[] value) {
            String deviceId = gatt.getDevice().getAddress();
            String key = deviceId + "_" + charUuid;

            NotifyCallback cb = notifyCallbacks.get(key);
            if (cb != null) {
                mainHandler.post(() -> cb.onNotify(value));
            }
        }

        // Bug 3: descriptor write callback for subscribe/unsubscribe completion
        @Override
        public void onDescriptorWrite(BluetoothGatt gatt, BluetoothGattDescriptor descriptor, int status) {
            String deviceId = gatt.getDevice().getAddress();
            GattQueue queue = gattQueues.get(deviceId);
            if (queue != null) queue.complete();

            BluetoothGattCharacteristic characteristic = descriptor.getCharacteristic();
            if (characteristic == null) return;

            String charUuid = characteristic.getUuid().toString();
            String key = deviceId + "_" + charUuid;

            SubscribeResultCallback cb = subscribeResultCallbacks.remove(key);
            if (cb == null) return;

            if (status == BluetoothGatt.GATT_SUCCESS) {
                mainHandler.post(cb::onSuccess);
            } else {
                final String error = "Descriptor write failed with status: " + status;
                mainHandler.post(() -> cb.onError(error));
            }
        }
    };
}
