#ifndef UniBlePlugin_h
#define UniBlePlugin_h

#import <Foundation/Foundation.h>
#import <CoreBluetooth/CoreBluetooth.h>

// Callback types
typedef void (*StateChangedCallback)(int state);
typedef void (*DeviceDiscoveredCallback)(const char* deviceId, const char* deviceName, const char* serviceUuidsJson);
typedef void (*ConnectionCallback)(const char* deviceId, bool success, const char* error);
typedef void (*ServiceDiscoveryCallback)(const char* deviceId, const char* servicesJson, const char* error);
typedef void (*CharacteristicDiscoveryCallback)(const char* deviceId, const char* serviceUuid, const char* characteristicsJson, const char* error);
typedef void (*ReadCallback)(const char* deviceId, const char* serviceUuid, const char* characteristicUuid, const void* data, int dataLength, const char* error);
typedef void (*WriteCallback)(const char* deviceId, const char* serviceUuid, const char* characteristicUuid, const char* error);
typedef void (*NotifyCallback)(const char* deviceId, const char* serviceUuid, const char* characteristicUuid, const void* data, int dataLength);
typedef void (*SubscribeCallback)(const char* deviceId, const char* serviceUuid, const char* characteristicUuid, const char* error);
typedef void (*DisconnectCallback)(const char* deviceId, const char* error);

// Exported functions
extern "C" {
    void UniBle_SetDebugEnabled(bool enabled);
    void UniBle_Initialize(StateChangedCallback stateCallback, DeviceDiscoveredCallback deviceCallback, DisconnectCallback disconnectCallback);
    bool UniBle_IsAvailable(void);
    void UniBle_StartScan(const char* serviceUuidsJson);
    void UniBle_StopScan(void);
    void UniBle_Connect(const char* deviceId, ConnectionCallback callback);
    void UniBle_Disconnect(const char* deviceId, DisconnectCallback callback);
    void UniBle_DiscoverServices(const char* deviceId, ServiceDiscoveryCallback callback);
    void UniBle_DiscoverCharacteristics(const char* deviceId, const char* serviceUuid, CharacteristicDiscoveryCallback callback);
    void UniBle_ReadCharacteristic(const char* deviceId, const char* serviceUuid, const char* characteristicUuid, ReadCallback callback);
    void UniBle_WriteCharacteristic(const char* deviceId, const char* serviceUuid, const char* characteristicUuid, const void* data, int dataLength, bool withResponse, WriteCallback callback);
    void UniBle_Subscribe(const char* deviceId, const char* serviceUuid, const char* characteristicUuid, NotifyCallback notifyCallback, SubscribeCallback resultCallback);
    void UniBle_Unsubscribe(const char* deviceId, const char* serviceUuid, const char* characteristicUuid, SubscribeCallback resultCallback);
}

#endif /* UniBlePlugin_h */
