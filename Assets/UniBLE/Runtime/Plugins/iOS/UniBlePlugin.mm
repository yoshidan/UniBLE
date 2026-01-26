#import "UniBlePlugin.h"
#import <CoreBluetooth/CoreBluetooth.h>

// Characteristic properties mapping (matches Unity BleCharacteristicProperties)
static const int PROP_BROADCAST = 1;
static const int PROP_READ = 2;
static const int PROP_WRITE_WITHOUT_RESPONSE = 4;
static const int PROP_WRITE = 8;
static const int PROP_NOTIFY = 16;
static const int PROP_INDICATE = 32;

@interface UniBleManager : NSObject <CBCentralManagerDelegate, CBPeripheralDelegate>

@property (nonatomic, strong) CBCentralManager* centralManager;
@property (nonatomic, strong) NSMutableDictionary<NSString*, CBPeripheral*>* peripherals;
@property (nonatomic, strong) NSMutableDictionary<NSString*, NSMutableDictionary*>* peripheralCallbacks;
@property (nonatomic, assign) StateChangedCallback stateChangedCallback;
@property (nonatomic, assign) DeviceDiscoveredCallback deviceDiscoveredCallback;
@property (nonatomic, assign) DisconnectCallback globalDisconnectCallback;

+ (instancetype)shared;
- (void)initializeWithStateCallback:(StateChangedCallback)stateCallback deviceCallback:(DeviceDiscoveredCallback)deviceCallback disconnectCallback:(DisconnectCallback)disconnectCallback;
- (BOOL)isAvailable;
- (void)startScanWithServiceUuids:(NSArray<CBUUID*>*)serviceUuids;
- (void)stopScan;
- (void)connectPeripheral:(NSString*)deviceId callback:(ConnectionCallback)callback;
- (void)disconnectPeripheral:(NSString*)deviceId callback:(DisconnectCallback)callback;
- (void)discoverServicesForPeripheral:(NSString*)deviceId callback:(ServiceDiscoveryCallback)callback;
- (void)discoverCharacteristicsForPeripheral:(NSString*)deviceId serviceUuid:(NSString*)serviceUuid callback:(CharacteristicDiscoveryCallback)callback;
- (void)readCharacteristicForPeripheral:(NSString*)deviceId serviceUuid:(NSString*)serviceUuid characteristicUuid:(NSString*)characteristicUuid callback:(ReadCallback)callback;
- (void)writeCharacteristicForPeripheral:(NSString*)deviceId serviceUuid:(NSString*)serviceUuid characteristicUuid:(NSString*)characteristicUuid data:(NSData*)data withResponse:(BOOL)withResponse callback:(WriteCallback)callback;
- (void)subscribeToCharacteristicForPeripheral:(NSString*)deviceId serviceUuid:(NSString*)serviceUuid characteristicUuid:(NSString*)characteristicUuid notifyCallback:(NotifyCallback)notifyCallback resultCallback:(SubscribeCallback)resultCallback;
- (void)unsubscribeFromCharacteristicForPeripheral:(NSString*)deviceId serviceUuid:(NSString*)serviceUuid characteristicUuid:(NSString*)characteristicUuid callback:(SubscribeCallback)callback;

@end

// Global reference to prevent deallocation
static UniBleManager* g_sharedInstance = nil;

@implementation UniBleManager {
    dispatch_queue_t _syncQueue;
}

+ (instancetype)shared {
    static dispatch_once_t onceToken;
    dispatch_once(&onceToken, ^{
        g_sharedInstance = [[UniBleManager alloc] init];
    });
    return g_sharedInstance;
}

- (instancetype)init {
    self = [super init];
    if (self) {
        _peripherals = [[NSMutableDictionary alloc] init];
        _peripheralCallbacks = [[NSMutableDictionary alloc] init];
        _syncQueue = dispatch_queue_create("com.unible.sync", DISPATCH_QUEUE_SERIAL);
    }
    return self;
}

- (void)initializeWithStateCallback:(StateChangedCallback)stateCallback deviceCallback:(DeviceDiscoveredCallback)deviceCallback disconnectCallback:(DisconnectCallback)disconnectCallback {
    self.stateChangedCallback = stateCallback;
    self.deviceDiscoveredCallback = deviceCallback;
    self.globalDisconnectCallback = disconnectCallback;

    if (!self.centralManager) {
        self.centralManager = [[CBCentralManager alloc] initWithDelegate:self queue:nil];
    }
}

- (BOOL)isAvailable {
    return self.centralManager.state == CBManagerStatePoweredOn;
}

- (void)startScanWithServiceUuids:(NSArray<CBUUID*>*)serviceUuids {
    if (self.centralManager.state != CBManagerStatePoweredOn) {
        return;
    }

    // Stop any existing scan first
    [self.centralManager stopScan];

    // Clear previously discovered peripherals so they can be discovered again
    [self.peripherals removeAllObjects];

    [self.centralManager scanForPeripheralsWithServices:serviceUuids options:@{CBCentralManagerScanOptionAllowDuplicatesKey: @NO}];
}

- (void)stopScan {
    [self.centralManager stopScan];
}

- (void)connectPeripheral:(NSString*)deviceId callback:(ConnectionCallback)callback {
    CBPeripheral* peripheral = self.peripherals[deviceId];
    if (!peripheral) {
        if (callback) {
            callback([deviceId UTF8String], false, "Peripheral not found");
        }
        return;
    }

    NSMutableDictionary* callbacks = [self callbacksForPeripheral:deviceId];
    callbacks[@"connectionCallback"] = [NSValue valueWithPointer:(void*)callback];

    // Set delegate before connecting (important for receiving peripheral callbacks)
    peripheral.delegate = self;
    [self.centralManager connectPeripheral:peripheral options:nil];
}

- (void)disconnectPeripheral:(NSString*)deviceId callback:(DisconnectCallback)callback {
    CBPeripheral* peripheral = self.peripherals[deviceId];
    if (!peripheral) {
        if (callback) {
            callback([deviceId UTF8String], "Peripheral not found");
        }
        return;
    }

    NSMutableDictionary* callbacks = [self callbacksForPeripheral:deviceId];
    if (callback) {
        callbacks[@"disconnectCallback"] = [NSValue valueWithPointer:(void*)callback];
    }

    [self.centralManager cancelPeripheralConnection:peripheral];
}

- (void)discoverServicesForPeripheral:(NSString*)deviceId callback:(ServiceDiscoveryCallback)callback {
    CBPeripheral* peripheral = self.peripherals[deviceId];
    if (!peripheral) {
        if (callback) {
            callback([deviceId UTF8String], nil, "Peripheral not found");
        }
        return;
    }

    NSMutableDictionary* callbacks = [self callbacksForPeripheral:deviceId];
    callbacks[@"serviceDiscoveryCallback"] = [NSValue valueWithPointer:(void*)callback];

    [peripheral discoverServices:nil];
}

- (void)discoverCharacteristicsForPeripheral:(NSString*)deviceId serviceUuid:(NSString*)serviceUuid callback:(CharacteristicDiscoveryCallback)callback {
    CBPeripheral* peripheral = self.peripherals[deviceId];
    if (!peripheral) {
        if (callback) {
            callback([deviceId UTF8String], [serviceUuid UTF8String], nil, "Peripheral not found");
        }
        return;
    }

    CBService* targetService = nil;
    for (CBService* service in peripheral.services) {
        if ([[service.UUID.UUIDString lowercaseString] isEqualToString:[serviceUuid lowercaseString]]) {
            targetService = service;
            break;
        }
    }

    if (!targetService) {
        if (callback) {
            callback([deviceId UTF8String], [serviceUuid UTF8String], nil, "Service not found");
        }
        return;
    }

    NSMutableDictionary* callbacks = [self callbacksForPeripheral:deviceId];
    NSString* key = [NSString stringWithFormat:@"characteristicDiscoveryCallback_%@", serviceUuid];
    callbacks[key] = [NSValue valueWithPointer:(void*)callback];

    [peripheral discoverCharacteristics:nil forService:targetService];
}

- (void)readCharacteristicForPeripheral:(NSString*)deviceId serviceUuid:(NSString*)serviceUuid characteristicUuid:(NSString*)characteristicUuid callback:(ReadCallback)callback {
    CBCharacteristic* characteristic = [self findCharacteristicForPeripheral:deviceId serviceUuid:serviceUuid characteristicUuid:characteristicUuid];
    if (!characteristic) {
        if (callback) {
            callback([deviceId UTF8String], [characteristicUuid UTF8String], nil, 0, "Characteristic not found");
        }
        return;
    }

    NSMutableDictionary* callbacks = [self callbacksForPeripheral:deviceId];
    NSString* key = [NSString stringWithFormat:@"readCallback_%@", characteristicUuid];
    callbacks[key] = [NSValue valueWithPointer:(void*)callback];

    CBPeripheral* peripheral = self.peripherals[deviceId];
    [peripheral readValueForCharacteristic:characteristic];
}

- (void)writeCharacteristicForPeripheral:(NSString*)deviceId serviceUuid:(NSString*)serviceUuid characteristicUuid:(NSString*)characteristicUuid data:(NSData*)data withResponse:(BOOL)withResponse callback:(WriteCallback)callback {
    CBCharacteristic* characteristic = [self findCharacteristicForPeripheral:deviceId serviceUuid:serviceUuid characteristicUuid:characteristicUuid];
    if (!characteristic) {
        if (callback) {
            callback([deviceId UTF8String], [characteristicUuid UTF8String], "Characteristic not found");
        }
        return;
    }

    NSMutableDictionary* callbacks = [self callbacksForPeripheral:deviceId];
    NSString* key = [NSString stringWithFormat:@"writeCallback_%@", characteristicUuid];
    callbacks[key] = [NSValue valueWithPointer:(void*)callback];

    CBPeripheral* peripheral = self.peripherals[deviceId];
    CBCharacteristicWriteType writeType = withResponse ? CBCharacteristicWriteWithResponse : CBCharacteristicWriteWithoutResponse;
    [peripheral writeValue:data forCharacteristic:characteristic type:writeType];

    // For write without response, call callback immediately
    if (!withResponse && callback) {
        callback([deviceId UTF8String], [characteristicUuid UTF8String], nil);
    }
}

- (void)subscribeToCharacteristicForPeripheral:(NSString*)deviceId serviceUuid:(NSString*)serviceUuid characteristicUuid:(NSString*)characteristicUuid notifyCallback:(NotifyCallback)notifyCallback resultCallback:(SubscribeCallback)resultCallback {
    NSLog(@"[UniBLE Native] subscribeToCharacteristicForPeripheral called");
    NSLog(@"[UniBLE Native]   deviceId: %@", deviceId);
    NSLog(@"[UniBLE Native]   serviceUuid: %@", serviceUuid);
    NSLog(@"[UniBLE Native]   characteristicUuid: %@", characteristicUuid);

    CBCharacteristic* characteristic = [self findCharacteristicForPeripheral:deviceId serviceUuid:serviceUuid characteristicUuid:characteristicUuid];
    if (!characteristic) {
        NSLog(@"[UniBLE Native] Characteristic not found!");
        if (resultCallback) {
            resultCallback([deviceId UTF8String], [characteristicUuid UTF8String], "Characteristic not found");
        }
        return;
    }

    NSLog(@"[UniBLE Native] Found characteristic: %@, properties: %lu", characteristic.UUID, (unsigned long)characteristic.properties);

    // Use the actual characteristic UUID from CoreBluetooth (uppercased) for consistent key
    NSString* actualUuid = [characteristic.UUID.UUIDString uppercaseString];
    NSLog(@"[UniBLE Native] Subscribing to characteristic: %@ (input was: %@)", actualUuid, characteristicUuid);

    NSMutableDictionary* callbacks = [self callbacksForPeripheral:deviceId];
    NSString* notifyKey = [NSString stringWithFormat:@"notifyCallback_%@", actualUuid];
    NSString* subscribeKey = [NSString stringWithFormat:@"subscribeCallback_%@", actualUuid];
    NSLog(@"[UniBLE Native] Storing notify callback with key: %@", notifyKey);
    callbacks[notifyKey] = [NSValue valueWithPointer:(void*)notifyCallback];
    callbacks[subscribeKey] = [NSValue valueWithPointer:(void*)resultCallback];

    CBPeripheral* peripheral = self.peripherals[deviceId];
    NSLog(@"[UniBLE Native] Peripheral state: %ld, delegate: %@", (long)peripheral.state, peripheral.delegate);
    [peripheral setNotifyValue:YES forCharacteristic:characteristic];
    NSLog(@"[UniBLE Native] setNotifyValue:YES called");
}

- (void)unsubscribeFromCharacteristicForPeripheral:(NSString*)deviceId serviceUuid:(NSString*)serviceUuid characteristicUuid:(NSString*)characteristicUuid callback:(SubscribeCallback)callback {
    CBCharacteristic* characteristic = [self findCharacteristicForPeripheral:deviceId serviceUuid:serviceUuid characteristicUuid:characteristicUuid];
    if (!characteristic) {
        if (callback) {
            callback([deviceId UTF8String], [characteristicUuid UTF8String], "Characteristic not found");
        }
        return;
    }

    NSMutableDictionary* callbacks = [self callbacksForPeripheral:deviceId];
    NSString* key = [NSString stringWithFormat:@"unsubscribeCallback_%@", characteristicUuid];
    callbacks[key] = [NSValue valueWithPointer:(void*)callback];

    // Remove notify callback
    NSString* notifyKey = [NSString stringWithFormat:@"notifyCallback_%@", characteristicUuid];
    [callbacks removeObjectForKey:notifyKey];

    CBPeripheral* peripheral = self.peripherals[deviceId];
    [peripheral setNotifyValue:NO forCharacteristic:characteristic];
}

#pragma mark - Helper methods

- (NSMutableDictionary*)callbacksForPeripheral:(NSString*)deviceId {
    NSMutableDictionary* callbacks = self.peripheralCallbacks[deviceId];
    if (!callbacks) {
        callbacks = [NSMutableDictionary dictionary];
        self.peripheralCallbacks[deviceId] = callbacks;
    }
    return callbacks;
}

- (CBCharacteristic*)findCharacteristicForPeripheral:(NSString*)deviceId serviceUuid:(NSString*)serviceUuid characteristicUuid:(NSString*)characteristicUuid {
    CBPeripheral* peripheral = self.peripherals[deviceId];
    if (!peripheral) return nil;

    for (CBService* service in peripheral.services) {
        if ([[service.UUID.UUIDString lowercaseString] isEqualToString:[serviceUuid lowercaseString]]) {
            for (CBCharacteristic* characteristic in service.characteristics) {
                if ([[characteristic.UUID.UUIDString lowercaseString] isEqualToString:[characteristicUuid lowercaseString]]) {
                    return characteristic;
                }
            }
        }
    }
    return nil;
}

- (int)propertiesFromCharacteristic:(CBCharacteristic*)characteristic {
    int props = 0;
    CBCharacteristicProperties cbProps = characteristic.properties;

    if (cbProps & CBCharacteristicPropertyBroadcast) props |= PROP_BROADCAST;
    if (cbProps & CBCharacteristicPropertyRead) props |= PROP_READ;
    if (cbProps & CBCharacteristicPropertyWriteWithoutResponse) props |= PROP_WRITE_WITHOUT_RESPONSE;
    if (cbProps & CBCharacteristicPropertyWrite) props |= PROP_WRITE;
    if (cbProps & CBCharacteristicPropertyNotify) props |= PROP_NOTIFY;
    if (cbProps & CBCharacteristicPropertyIndicate) props |= PROP_INDICATE;

    return props;
}

#pragma mark - CBCentralManagerDelegate

- (void)centralManagerDidUpdateState:(CBCentralManager*)central {
    NSLog(@"[UniBLE Native] centralManagerDidUpdateState: %ld", (long)central.state);
    int state = 0;
    switch (central.state) {
        case CBManagerStateUnknown: state = 0; break;
        case CBManagerStateUnsupported: state = 1; break;
        case CBManagerStateUnauthorized: state = 2; break;
        case CBManagerStatePoweredOff: state = 3; break;
        case CBManagerStatePoweredOn: state = 4; break;
        default: state = 0; break;
    }

    NSLog(@"[UniBLE Native] Calling state callback with state: %d", state);
    if (self.stateChangedCallback) {
        // Call directly - delegate methods are already on main queue (we passed nil for queue in init)
        // C# side handles thread safety with MainThreadDispatcher
        self.stateChangedCallback(state);
    }
    NSLog(@"[UniBLE Native] State callback done");
}

- (void)centralManager:(CBCentralManager*)central didDiscoverPeripheral:(CBPeripheral*)peripheral advertisementData:(NSDictionary<NSString*, id>*)advertisementData RSSI:(NSNumber*)RSSI {
    if (!peripheral) {
        return;
    }

    NSString* deviceId = peripheral.identifier.UUIDString;
    if (!deviceId) {
        return;
    }

    // Build service UUIDs JSON from advertisement data
    NSArray* serviceUUIDs = advertisementData[CBAdvertisementDataServiceUUIDsKey];
    NSString* serviceUuidsJson = nil;
    if (serviceUUIDs && serviceUUIDs.count > 0) {
        NSMutableArray* uuidStrings = [NSMutableArray array];
        for (CBUUID* uuid in serviceUUIDs) {
            [uuidStrings addObject:[NSString stringWithFormat:@"\"%@\"", uuid.UUIDString]];
        }
        serviceUuidsJson = [NSString stringWithFormat:@"[%@]", [uuidStrings componentsJoinedByString:@","]];
    }

    // Use sync queue to prevent race conditions
    dispatch_sync(_syncQueue, ^{
        if (!self.peripherals[deviceId]) {
            self.peripherals[deviceId] = peripheral;
            peripheral.delegate = self;

            if (self.deviceDiscoveredCallback) {
                DeviceDiscoveredCallback callback = self.deviceDiscoveredCallback;
                NSString* name = peripheral.name ?: @"Unknown";
                // Copy strings to ensure they remain valid
                const char* deviceIdCStr = strdup([deviceId UTF8String]);
                const char* nameCStr = strdup([name UTF8String]);
                const char* serviceUuidsCStr = serviceUuidsJson ? strdup([serviceUuidsJson UTF8String]) : NULL;
                dispatch_async(dispatch_get_main_queue(), ^{
                    callback(deviceIdCStr, nameCStr, serviceUuidsCStr);
                    free((void*)deviceIdCStr);
                    free((void*)nameCStr);
                    if (serviceUuidsCStr) free((void*)serviceUuidsCStr);
                });
            }
        }
    });
}

- (void)centralManager:(CBCentralManager*)central didConnectPeripheral:(CBPeripheral*)peripheral {
    NSString* deviceId = peripheral.identifier.UUIDString;
    NSLog(@"[UniBLE Native] didConnectPeripheral: %@", deviceId);

    // Ensure delegate is set after connection (important for receiving peripheral callbacks like didUpdateValueForCharacteristic)
    peripheral.delegate = self;

    NSMutableDictionary* callbacks = self.peripheralCallbacks[deviceId];

    NSValue* callbackValue = callbacks[@"connectionCallback"];
    if (callbackValue) {
        ConnectionCallback callback = (ConnectionCallback)[callbackValue pointerValue];
        if (callback) {
            callback([deviceId UTF8String], true, nil);
        }
        [callbacks removeObjectForKey:@"connectionCallback"];
    }
}

- (void)centralManager:(CBCentralManager*)central didFailToConnectPeripheral:(CBPeripheral*)peripheral error:(NSError*)error {
    NSString* deviceId = peripheral.identifier.UUIDString;
    NSMutableDictionary* callbacks = self.peripheralCallbacks[deviceId];

    NSValue* callbackValue = callbacks[@"connectionCallback"];
    if (callbackValue) {
        ConnectionCallback callback = (ConnectionCallback)[callbackValue pointerValue];
        if (callback) {
            NSString* errorMsg = error.localizedDescription ?: @"Connection failed";
            callback([deviceId UTF8String], false, [errorMsg UTF8String]);
        }
        [callbacks removeObjectForKey:@"connectionCallback"];
    }
}

- (void)centralManager:(CBCentralManager*)central didDisconnectPeripheral:(CBPeripheral*)peripheral error:(NSError*)error {
    NSString* deviceId = peripheral.identifier.UUIDString;
    NSLog(@"[UniBLE Native] didDisconnectPeripheral: %@, error: %@", deviceId, error);

    const char* errorStr = error ? [error.localizedDescription UTF8String] : nil;
    const char* deviceIdStr = [deviceId UTF8String];

    NSMutableDictionary* callbacks = self.peripheralCallbacks[deviceId];
    NSValue* callbackValue = callbacks[@"disconnectCallback"];

    // Call per-device callback if exists (from explicit DisconnectAsync call)
    if (callbackValue) {
        DisconnectCallback callback = (DisconnectCallback)[callbackValue pointerValue];
        if (callback) {
            callback(deviceIdStr, errorStr);
        }
        [callbacks removeObjectForKey:@"disconnectCallback"];
    }

    // Always call global disconnect callback (for unexpected disconnections)
    if (self.globalDisconnectCallback) {
        self.globalDisconnectCallback(deviceIdStr, errorStr);
    }

    // Clear delegate
    peripheral.delegate = nil;
}

#pragma mark - CBPeripheralDelegate

- (void)peripheral:(CBPeripheral*)peripheral didDiscoverServices:(NSError*)error {
    NSString* deviceId = peripheral.identifier.UUIDString;
    NSMutableDictionary* callbacks = self.peripheralCallbacks[deviceId];

    NSValue* callbackValue = callbacks[@"serviceDiscoveryCallback"];
    if (callbackValue) {
        ServiceDiscoveryCallback callback = (ServiceDiscoveryCallback)[callbackValue pointerValue];
        if (callback) {
            if (error) {
                callback([deviceId UTF8String], nil, [error.localizedDescription UTF8String]);
            } else {
                NSMutableArray* uuids = [NSMutableArray array];
                for (CBService* service in peripheral.services) {
                    [uuids addObject:[NSString stringWithFormat:@"\"%@\"", service.UUID.UUIDString]];
                }
                NSString* json = [NSString stringWithFormat:@"[%@]", [uuids componentsJoinedByString:@","]];
                callback([deviceId UTF8String], [json UTF8String], nil);
            }
        }
        [callbacks removeObjectForKey:@"serviceDiscoveryCallback"];
    }
}

- (void)peripheral:(CBPeripheral*)peripheral didDiscoverCharacteristicsForService:(CBService*)service error:(NSError*)error {
    NSString* deviceId = peripheral.identifier.UUIDString;
    NSString* serviceUuid = service.UUID.UUIDString;
    NSMutableDictionary* callbacks = self.peripheralCallbacks[deviceId];

    NSString* key = [NSString stringWithFormat:@"characteristicDiscoveryCallback_%@", serviceUuid];
    NSValue* callbackValue = callbacks[key];
    if (callbackValue) {
        CharacteristicDiscoveryCallback callback = (CharacteristicDiscoveryCallback)[callbackValue pointerValue];
        if (callback) {
            if (error) {
                callback([deviceId UTF8String], [serviceUuid UTF8String], nil, [error.localizedDescription UTF8String]);
            } else {
                NSMutableArray* items = [NSMutableArray array];
                for (CBCharacteristic* characteristic in service.characteristics) {
                    int props = [self propertiesFromCharacteristic:characteristic];
                    NSString* item = [NSString stringWithFormat:@"{\"uuid\":\"%@\",\"properties\":%d}", characteristic.UUID.UUIDString, props];
                    [items addObject:item];
                }
                NSString* json = [NSString stringWithFormat:@"[%@]", [items componentsJoinedByString:@","]];
                callback([deviceId UTF8String], [serviceUuid UTF8String], [json UTF8String], nil);
            }
        }
        [callbacks removeObjectForKey:key];
    }
}

- (void)peripheral:(CBPeripheral*)peripheral didUpdateValueForCharacteristic:(CBCharacteristic*)characteristic error:(NSError*)error {
    NSLog(@"[UniBLE Native] *** didUpdateValueForCharacteristic CALLED ***");
    NSLog(@"[UniBLE Native]   peripheral: %@", peripheral.identifier.UUIDString);
    NSLog(@"[UniBLE Native]   characteristic: %@", characteristic.UUID.UUIDString);
    NSLog(@"[UniBLE Native]   value length: %lu", (unsigned long)characteristic.value.length);
    NSLog(@"[UniBLE Native]   error: %@", error);

    NSString* deviceId = peripheral.identifier.UUIDString;
    NSString* characteristicUuid = [characteristic.UUID.UUIDString uppercaseString];
    NSMutableDictionary* callbacks = self.peripheralCallbacks[deviceId];

    NSLog(@"[UniBLE Native] didUpdateValueForCharacteristic: %@, error: %@", characteristicUuid, error);

    // Check if this is a read callback or notify callback
    NSString* readKey = [NSString stringWithFormat:@"readCallback_%@", characteristicUuid];
    NSValue* readCallbackValue = callbacks[readKey];

    if (readCallbackValue) {
        ReadCallback callback = (ReadCallback)[readCallbackValue pointerValue];
        if (callback) {
            if (error) {
                callback([deviceId UTF8String], [characteristicUuid UTF8String], nil, 0, [error.localizedDescription UTF8String]);
            } else {
                NSData* data = characteristic.value;
                callback([deviceId UTF8String], [characteristicUuid UTF8String], data.bytes, (int)data.length, nil);
            }
        }
        [callbacks removeObjectForKey:readKey];
    } else {
        // This is a notification
        NSString* notifyKey = [NSString stringWithFormat:@"notifyCallback_%@", characteristicUuid];
        NSLog(@"[UniBLE Native] Looking for notify callback with key: %@", notifyKey);
        NSValue* notifyCallbackValue = callbacks[notifyKey];
        NSLog(@"[UniBLE Native] Notify callback found: %@", notifyCallbackValue ? @"YES" : @"NO");
        if (notifyCallbackValue) {
            NotifyCallback callback = (NotifyCallback)[notifyCallbackValue pointerValue];
            if (callback) {
                NSData* data = characteristic.value;
                NSLog(@"[UniBLE Native] Calling notify callback with %lu bytes", (unsigned long)data.length);
                callback([deviceId UTF8String], [characteristicUuid UTF8String], data.bytes, (int)data.length);
            }
        }
    }
}

- (void)peripheral:(CBPeripheral*)peripheral didWriteValueForCharacteristic:(CBCharacteristic*)characteristic error:(NSError*)error {
    NSString* deviceId = peripheral.identifier.UUIDString;
    NSString* characteristicUuid = characteristic.UUID.UUIDString;
    NSMutableDictionary* callbacks = self.peripheralCallbacks[deviceId];

    NSString* key = [NSString stringWithFormat:@"writeCallback_%@", characteristicUuid];
    NSValue* callbackValue = callbacks[key];
    if (callbackValue) {
        WriteCallback callback = (WriteCallback)[callbackValue pointerValue];
        if (callback) {
            if (error) {
                callback([deviceId UTF8String], [characteristicUuid UTF8String], [error.localizedDescription UTF8String]);
            } else {
                callback([deviceId UTF8String], [characteristicUuid UTF8String], nil);
            }
        }
        [callbacks removeObjectForKey:key];
    }
}

- (void)peripheral:(CBPeripheral*)peripheral didUpdateNotificationStateForCharacteristic:(CBCharacteristic*)characteristic error:(NSError*)error {
    NSString* deviceId = peripheral.identifier.UUIDString;
    NSString* characteristicUuid = [characteristic.UUID.UUIDString uppercaseString];
    NSMutableDictionary* callbacks = self.peripheralCallbacks[deviceId];

    NSLog(@"[UniBLE Native] didUpdateNotificationStateForCharacteristic: %@, isNotifying: %d, error: %@",
          characteristicUuid, characteristic.isNotifying, error);

    NSString* subscribeKey = [NSString stringWithFormat:@"subscribeCallback_%@", characteristicUuid];
    NSString* unsubscribeKey = [NSString stringWithFormat:@"unsubscribeCallback_%@", characteristicUuid];

    NSValue* subscribeCallbackValue = callbacks[subscribeKey];
    NSValue* unsubscribeCallbackValue = callbacks[unsubscribeKey];

    if (subscribeCallbackValue) {
        SubscribeCallback callback = (SubscribeCallback)[subscribeCallbackValue pointerValue];
        if (callback) {
            if (error) {
                callback([deviceId UTF8String], [characteristicUuid UTF8String], [error.localizedDescription UTF8String]);
            } else {
                callback([deviceId UTF8String], [characteristicUuid UTF8String], nil);
            }
        }
        [callbacks removeObjectForKey:subscribeKey];
    }

    if (unsubscribeCallbackValue) {
        SubscribeCallback callback = (SubscribeCallback)[unsubscribeCallbackValue pointerValue];
        if (callback) {
            if (error) {
                callback([deviceId UTF8String], [characteristicUuid UTF8String], [error.localizedDescription UTF8String]);
            } else {
                callback([deviceId UTF8String], [characteristicUuid UTF8String], nil);
            }
        }
        [callbacks removeObjectForKey:unsubscribeKey];
    }
}

@end

#pragma mark - C Interface

extern "C" {

void UniBle_Initialize(StateChangedCallback stateCallback, DeviceDiscoveredCallback deviceCallback, DisconnectCallback disconnectCallback) {
    [[UniBleManager shared] initializeWithStateCallback:stateCallback deviceCallback:deviceCallback disconnectCallback:disconnectCallback];
}

bool UniBle_IsAvailable(void) {
    return [[UniBleManager shared] isAvailable];
}

void UniBle_StartScan(const char* serviceUuidsJson) {
    NSLog(@"[UniBLE Native] UniBle_StartScan called with: %s", serviceUuidsJson ? serviceUuidsJson : "null");
    NSArray<CBUUID*>* uuids = nil;

    if (serviceUuidsJson) {
        NSString* json = [NSString stringWithUTF8String:serviceUuidsJson];
        NSLog(@"[UniBLE Native] Parsing JSON: %@", json);
        // Parse simple JSON array: ["uuid1", "uuid2"]
        json = [json stringByTrimmingCharactersInSet:[NSCharacterSet whitespaceCharacterSet]];
        if ([json hasPrefix:@"["] && [json hasSuffix:@"]"]) {
            json = [json substringWithRange:NSMakeRange(1, json.length - 2)];
            NSArray* parts = [json componentsSeparatedByString:@","];
            NSMutableArray<CBUUID*>* mutableUuids = [NSMutableArray array];
            for (NSString* part in parts) {
                NSString* trimmed = [part stringByTrimmingCharactersInSet:[NSCharacterSet whitespaceCharacterSet]];
                trimmed = [trimmed stringByTrimmingCharactersInSet:[NSCharacterSet characterSetWithCharactersInString:@"\""]];
                NSLog(@"[UniBLE Native] Parsing UUID: '%@'", trimmed);
                if (trimmed.length > 0) {
                    CBUUID* uuid = [CBUUID UUIDWithString:trimmed];
                    if (uuid) {
                        [mutableUuids addObject:uuid];
                        NSLog(@"[UniBLE Native] Added UUID: %@", uuid);
                    } else {
                        NSLog(@"[UniBLE Native] Failed to create CBUUID from: %@", trimmed);
                    }
                }
            }
            if (mutableUuids.count > 0) {
                uuids = mutableUuids;
            }
        }
    }

    NSLog(@"[UniBLE Native] Starting scan with %lu service UUIDs", (unsigned long)(uuids ? uuids.count : 0));
    [[UniBleManager shared] startScanWithServiceUuids:uuids];
    NSLog(@"[UniBLE Native] Scan started");
}

void UniBle_StopScan(void) {
    [[UniBleManager shared] stopScan];
}

void UniBle_Connect(const char* deviceId, ConnectionCallback callback) {
    NSString* deviceIdStr = [NSString stringWithUTF8String:deviceId];
    [[UniBleManager shared] connectPeripheral:deviceIdStr callback:callback];
}

void UniBle_Disconnect(const char* deviceId, DisconnectCallback callback) {
    NSString* deviceIdStr = [NSString stringWithUTF8String:deviceId];
    [[UniBleManager shared] disconnectPeripheral:deviceIdStr callback:callback];
}

void UniBle_DiscoverServices(const char* deviceId, ServiceDiscoveryCallback callback) {
    NSString* deviceIdStr = [NSString stringWithUTF8String:deviceId];
    [[UniBleManager shared] discoverServicesForPeripheral:deviceIdStr callback:callback];
}

void UniBle_DiscoverCharacteristics(const char* deviceId, const char* serviceUuid, CharacteristicDiscoveryCallback callback) {
    NSString* deviceIdStr = [NSString stringWithUTF8String:deviceId];
    NSString* serviceUuidStr = [NSString stringWithUTF8String:serviceUuid];
    [[UniBleManager shared] discoverCharacteristicsForPeripheral:deviceIdStr serviceUuid:serviceUuidStr callback:callback];
}

void UniBle_ReadCharacteristic(const char* deviceId, const char* serviceUuid, const char* characteristicUuid, ReadCallback callback) {
    NSString* deviceIdStr = [NSString stringWithUTF8String:deviceId];
    NSString* serviceUuidStr = [NSString stringWithUTF8String:serviceUuid];
    NSString* characteristicUuidStr = [NSString stringWithUTF8String:characteristicUuid];
    [[UniBleManager shared] readCharacteristicForPeripheral:deviceIdStr serviceUuid:serviceUuidStr characteristicUuid:characteristicUuidStr callback:callback];
}

void UniBle_WriteCharacteristic(const char* deviceId, const char* serviceUuid, const char* characteristicUuid, const void* data, int dataLength, bool withResponse, WriteCallback callback) {
    NSString* deviceIdStr = [NSString stringWithUTF8String:deviceId];
    NSString* serviceUuidStr = [NSString stringWithUTF8String:serviceUuid];
    NSString* characteristicUuidStr = [NSString stringWithUTF8String:characteristicUuid];
    NSData* nsData = [NSData dataWithBytes:data length:dataLength];
    [[UniBleManager shared] writeCharacteristicForPeripheral:deviceIdStr serviceUuid:serviceUuidStr characteristicUuid:characteristicUuidStr data:nsData withResponse:withResponse callback:callback];
}

void UniBle_Subscribe(const char* deviceId, const char* serviceUuid, const char* characteristicUuid, NotifyCallback notifyCallback, SubscribeCallback resultCallback) {
    NSString* deviceIdStr = [NSString stringWithUTF8String:deviceId];
    NSString* serviceUuidStr = [NSString stringWithUTF8String:serviceUuid];
    NSString* characteristicUuidStr = [NSString stringWithUTF8String:characteristicUuid];
    [[UniBleManager shared] subscribeToCharacteristicForPeripheral:deviceIdStr serviceUuid:serviceUuidStr characteristicUuid:characteristicUuidStr notifyCallback:notifyCallback resultCallback:resultCallback];
}

void UniBle_Unsubscribe(const char* deviceId, const char* serviceUuid, const char* characteristicUuid, SubscribeCallback resultCallback) {
    NSString* deviceIdStr = [NSString stringWithUTF8String:deviceId];
    NSString* serviceUuidStr = [NSString stringWithUTF8String:serviceUuid];
    NSString* characteristicUuidStr = [NSString stringWithUTF8String:characteristicUuid];
    [[UniBleManager shared] unsubscribeFromCharacteristicForPeripheral:deviceIdStr serviceUuid:serviceUuidStr characteristicUuid:characteristicUuidStr callback:resultCallback];
}

}
