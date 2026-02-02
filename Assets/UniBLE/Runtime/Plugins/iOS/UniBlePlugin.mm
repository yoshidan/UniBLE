#import "UniBlePlugin.h"
#import <CoreBluetooth/CoreBluetooth.h>

// Debug logging
static BOOL g_debugEnabled = NO;
#define UNIBLE_LOG(fmt, ...) do { if (g_debugEnabled) NSLog(@"[UniBLE Native] " fmt, ##__VA_ARGS__); } while(0)

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
@property (nonatomic, strong) NSMutableDictionary<NSString*, CBPeripheral*>* discoveredPeripherals;
@property (nonatomic, strong) NSMutableDictionary<NSString*, NSMutableDictionary*>* peripheralCallbacks;
@property (nonatomic, assign) StateChangedCallback stateChangedCallback;
@property (nonatomic, assign) DeviceDiscoveredCallback deviceDiscoveredCallback;
@property (nonatomic, assign) DisconnectCallback globalDisconnectCallback;
@property (nonatomic, assign) BOOL pendingScan;
@property (nonatomic, strong) NSArray<CBUUID*>* pendingScanServiceUuids;

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
    @public
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
        _discoveredPeripherals = [[NSMutableDictionary alloc] init];
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
        self.centralManager = [[CBCentralManager alloc] initWithDelegate:self queue:_syncQueue];
    }
}

- (BOOL)isAvailable {
    return self.centralManager.state == CBManagerStatePoweredOn;
}

- (void)startScanWithServiceUuids:(NSArray<CBUUID*>*)serviceUuids {
    if (self.centralManager.state != CBManagerStatePoweredOn) {
        UNIBLE_LOG(@"State is not PoweredOn (%ld), queuing scan for later", (long)self.centralManager.state);
        self.pendingScan = YES;
        self.pendingScanServiceUuids = serviceUuids;
        return;
    }

    self.pendingScan = NO;
    self.pendingScanServiceUuids = nil;

    // Stop any existing scan first
    [self.centralManager stopScan];

    // Clear scan-discovered peripherals only (keep connected/known references)
    [self.discoveredPeripherals removeAllObjects];

    UNIBLE_LOG(@"Actually starting scan now");
    [self.centralManager scanForPeripheralsWithServices:serviceUuids options:@{CBCentralManagerScanOptionAllowDuplicatesKey: @NO}];
}

- (void)stopScan {
    [self.centralManager stopScan];

    // Remove peripherals that were not rediscovered and are not connected.
    NSArray<NSString*>* knownIds = [self.peripherals allKeys];
    for (NSString* deviceId in knownIds) {
        if (self.discoveredPeripherals[deviceId]) {
            continue;
        }
        CBPeripheral* peripheral = self.peripherals[deviceId];
        if (peripheral && peripheral.state == CBPeripheralStateConnected) {
            continue;
        }
        [self.peripherals removeObjectForKey:deviceId];
    }
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
    CBUUID* targetServiceUuid = [self cbUuidFromString:serviceUuid];
    if (!targetServiceUuid) {
        if (callback) {
            NSString* errorMsg = [self invalidUuidMessage:serviceUuid label:@"service"];
            callback([deviceId UTF8String], [serviceUuid UTF8String], nil, [errorMsg UTF8String]);
        }
        return;
    }
    for (CBService* service in peripheral.services) {
        if ([service.UUID isEqual:targetServiceUuid]) {
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
    NSString* key = [NSString stringWithFormat:@"characteristicDiscoveryCallback_%@", targetService.UUID.UUIDString];
    callbacks[key] = [NSValue valueWithPointer:(void*)callback];

    [peripheral discoverCharacteristics:nil forService:targetService];
}

- (void)readCharacteristicForPeripheral:(NSString*)deviceId serviceUuid:(NSString*)serviceUuid characteristicUuid:(NSString*)characteristicUuid callback:(ReadCallback)callback {
    CBUUID* targetServiceUuid = [self cbUuidFromString:serviceUuid];
    CBUUID* targetCharacteristicUuid = [self cbUuidFromString:characteristicUuid];
    if (!targetServiceUuid || !targetCharacteristicUuid) {
        if (callback) {
            NSString* errorMsg = !targetServiceUuid
                ? [self invalidUuidMessage:serviceUuid label:@"service"]
                : [self invalidUuidMessage:characteristicUuid label:@"characteristic"];
            callback([deviceId UTF8String], [serviceUuid UTF8String], [characteristicUuid UTF8String], nil, 0, [errorMsg UTF8String]);
        }
        return;
    }
    CBCharacteristic* characteristic = [self findCharacteristicForPeripheral:deviceId serviceUuid:serviceUuid characteristicUuid:characteristicUuid];
    if (!characteristic) {
        if (callback) {
            callback([deviceId UTF8String], [serviceUuid UTF8String], [characteristicUuid UTF8String], nil, 0, "Characteristic not found");
        }
        return;
    }

    NSString* normalizedUuid = [characteristic.UUID.UUIDString uppercaseString];
    NSString* normalizedServiceUuid = [characteristic.service.UUID.UUIDString uppercaseString];
    NSMutableDictionary* callbacks = [self callbacksForPeripheral:deviceId];
    NSString* key = [NSString stringWithFormat:@"readCallback_%@_%@", normalizedServiceUuid, normalizedUuid];
    callbacks[key] = [NSValue valueWithPointer:(void*)callback];

    CBPeripheral* peripheral = self.peripherals[deviceId];
    [peripheral readValueForCharacteristic:characteristic];
}

- (void)writeCharacteristicForPeripheral:(NSString*)deviceId serviceUuid:(NSString*)serviceUuid characteristicUuid:(NSString*)characteristicUuid data:(NSData*)data withResponse:(BOOL)withResponse callback:(WriteCallback)callback {
    CBUUID* targetServiceUuid = [self cbUuidFromString:serviceUuid];
    CBUUID* targetCharacteristicUuid = [self cbUuidFromString:characteristicUuid];
    if (!targetServiceUuid || !targetCharacteristicUuid) {
        if (callback) {
            NSString* errorMsg = !targetServiceUuid
                ? [self invalidUuidMessage:serviceUuid label:@"service"]
                : [self invalidUuidMessage:characteristicUuid label:@"characteristic"];
            callback([deviceId UTF8String], [serviceUuid UTF8String], [characteristicUuid UTF8String], [errorMsg UTF8String]);
        }
        return;
    }
    CBCharacteristic* characteristic = [self findCharacteristicForPeripheral:deviceId serviceUuid:serviceUuid characteristicUuid:characteristicUuid];
    if (!characteristic) {
        if (callback) {
            callback([deviceId UTF8String], [serviceUuid UTF8String], [characteristicUuid UTF8String], "Characteristic not found");
        }
        return;
    }

    NSString* normalizedUuid = [characteristic.UUID.UUIDString uppercaseString];
    NSString* normalizedServiceUuid = [characteristic.service.UUID.UUIDString uppercaseString];
    NSMutableDictionary* callbacks = [self callbacksForPeripheral:deviceId];
    NSString* key = [NSString stringWithFormat:@"writeCallback_%@_%@", normalizedServiceUuid, normalizedUuid];
    callbacks[key] = [NSValue valueWithPointer:(void*)callback];

    CBPeripheral* peripheral = self.peripherals[deviceId];
    CBCharacteristicWriteType writeType = withResponse ? CBCharacteristicWriteWithResponse : CBCharacteristicWriteWithoutResponse;
    [peripheral writeValue:data forCharacteristic:characteristic type:writeType];

    // For write without response, call callback immediately
    if (!withResponse && callback) {
        [callbacks removeObjectForKey:key];
        callback([deviceId UTF8String], [normalizedServiceUuid UTF8String], [normalizedUuid UTF8String], nil);
    }
}

- (void)subscribeToCharacteristicForPeripheral:(NSString*)deviceId serviceUuid:(NSString*)serviceUuid characteristicUuid:(NSString*)characteristicUuid notifyCallback:(NotifyCallback)notifyCallback resultCallback:(SubscribeCallback)resultCallback {
    CBUUID* targetServiceUuid = [self cbUuidFromString:serviceUuid];
    CBUUID* targetCharacteristicUuid = [self cbUuidFromString:characteristicUuid];
    if (!targetServiceUuid || !targetCharacteristicUuid) {
        if (resultCallback) {
            NSString* errorMsg = !targetServiceUuid
                ? [self invalidUuidMessage:serviceUuid label:@"service"]
                : [self invalidUuidMessage:characteristicUuid label:@"characteristic"];
            resultCallback([deviceId UTF8String], [serviceUuid UTF8String], [characteristicUuid UTF8String], [errorMsg UTF8String]);
        }
        return;
    }
    CBCharacteristic* characteristic = [self findCharacteristicForPeripheral:deviceId serviceUuid:serviceUuid characteristicUuid:characteristicUuid];
    if (!characteristic) {
        if (resultCallback) {
            resultCallback([deviceId UTF8String], [serviceUuid UTF8String], [characteristicUuid UTF8String], "Characteristic not found");
        }
        return;
    }

    NSString* normalizedUuid = [characteristic.UUID.UUIDString uppercaseString];
    NSString* normalizedServiceUuid = [characteristic.service.UUID.UUIDString uppercaseString];

    NSMutableDictionary* callbacks = [self callbacksForPeripheral:deviceId];
    NSString* notifyKey = [NSString stringWithFormat:@"notifyCallback_%@_%@", normalizedServiceUuid, normalizedUuid];
    NSString* subscribeKey = [NSString stringWithFormat:@"subscribeCallback_%@_%@", normalizedServiceUuid, normalizedUuid];
    callbacks[notifyKey] = [NSValue valueWithPointer:(void*)notifyCallback];
    callbacks[subscribeKey] = [NSValue valueWithPointer:(void*)resultCallback];

    CBPeripheral* peripheral = self.peripherals[deviceId];
    [peripheral setNotifyValue:YES forCharacteristic:characteristic];
}

- (void)unsubscribeFromCharacteristicForPeripheral:(NSString*)deviceId serviceUuid:(NSString*)serviceUuid characteristicUuid:(NSString*)characteristicUuid callback:(SubscribeCallback)callback {
    CBUUID* targetServiceUuid = [self cbUuidFromString:serviceUuid];
    CBUUID* targetCharacteristicUuid = [self cbUuidFromString:characteristicUuid];
    if (!targetServiceUuid || !targetCharacteristicUuid) {
        if (callback) {
            NSString* errorMsg = !targetServiceUuid
                ? [self invalidUuidMessage:serviceUuid label:@"service"]
                : [self invalidUuidMessage:characteristicUuid label:@"characteristic"];
            callback([deviceId UTF8String], [serviceUuid UTF8String], [characteristicUuid UTF8String], [errorMsg UTF8String]);
        }
        return;
    }
    CBCharacteristic* characteristic = [self findCharacteristicForPeripheral:deviceId serviceUuid:serviceUuid characteristicUuid:characteristicUuid];
    if (!characteristic) {
        if (callback) {
            callback([deviceId UTF8String], [serviceUuid UTF8String], [characteristicUuid UTF8String], "Characteristic not found");
        }
        return;
    }

    NSString* normalizedUuid = [characteristic.UUID.UUIDString uppercaseString];
    NSString* normalizedServiceUuid = [characteristic.service.UUID.UUIDString uppercaseString];

    NSMutableDictionary* callbacks = [self callbacksForPeripheral:deviceId];
    NSString* key = [NSString stringWithFormat:@"unsubscribeCallback_%@_%@", normalizedServiceUuid, normalizedUuid];
    callbacks[key] = [NSValue valueWithPointer:(void*)callback];

    // Remove notify callback
    NSString* notifyKey = [NSString stringWithFormat:@"notifyCallback_%@_%@", normalizedServiceUuid, normalizedUuid];
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

- (CBUUID*)cbUuidFromString:(NSString*)uuidString {
    if (!uuidString) {
        return nil;
    }
    NSString* trimmed = [uuidString stringByTrimmingCharactersInSet:[NSCharacterSet whitespaceAndNewlineCharacterSet]];
    if (trimmed.length == 0) {
        return nil;
    }
    return [CBUUID UUIDWithString:trimmed];
}

- (NSString*)invalidUuidMessage:(NSString*)uuidString label:(NSString*)label {
    NSString* value = uuidString ? uuidString : @"(null)";
    return [NSString stringWithFormat:@"Invalid %@ UUID: %@", label, value];
}

- (CBCharacteristic*)findCharacteristicForPeripheral:(NSString*)deviceId serviceUuid:(NSString*)serviceUuid characteristicUuid:(NSString*)characteristicUuid {
    CBPeripheral* peripheral = self.peripherals[deviceId];
    if (!peripheral) return nil;

    CBUUID* targetServiceUuid = [self cbUuidFromString:serviceUuid];
    CBUUID* targetCharacteristicUuid = [self cbUuidFromString:characteristicUuid];
    if (!targetServiceUuid || !targetCharacteristicUuid) {
        return nil;
    }

    for (CBService* service in peripheral.services) {
        if ([service.UUID isEqual:targetServiceUuid]) {
            for (CBCharacteristic* characteristic in service.characteristics) {
                if ([characteristic.UUID isEqual:targetCharacteristicUuid]) {
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
    UNIBLE_LOG(@"centralManagerDidUpdateState: %ld", (long)central.state);
    int state = 0;
    switch (central.state) {
        case CBManagerStateUnknown: state = 0; break;
        case CBManagerStateUnsupported: state = 1; break;
        case CBManagerStateUnauthorized: state = 2; break;
        case CBManagerStatePoweredOff: state = 3; break;
        case CBManagerStatePoweredOn: state = 4; break;
        default: state = 0; break;
    }

    UNIBLE_LOG(@"Calling state callback with state: %d", state);
    if (self.stateChangedCallback) {
        // Call directly - C# side handles thread safety with MainThreadDispatcher
        self.stateChangedCallback(state);
    }
    UNIBLE_LOG(@"State callback done");

    // Start pending scan if state became PoweredOn
    if (central.state == CBManagerStatePoweredOn && self.pendingScan) {
        UNIBLE_LOG(@"Executing pending scan");
        [self startScanWithServiceUuids:self.pendingScanServiceUuids];
    }
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

    // Already on _syncQueue (CBCentralManager delegate queue), no dispatch needed
    if (!self.peripherals[deviceId]) {
        self.peripherals[deviceId] = peripheral;
        peripheral.delegate = self;
    }

    if (!self.discoveredPeripherals[deviceId]) {
        self.discoveredPeripherals[deviceId] = peripheral;
        if (self.deviceDiscoveredCallback) {
            // Call C# callback directly - C# copies string data immediately via Marshal.PtrToStringAnsi,
            // and handles thread safety via MainThreadDispatcher.Enqueue()
            self.deviceDiscoveredCallback(
                [deviceId UTF8String],
                [(peripheral.name ?: @"Unknown") UTF8String],
                serviceUuidsJson ? [serviceUuidsJson UTF8String] : NULL
            );
        }
    }
}

- (void)centralManager:(CBCentralManager*)central didConnectPeripheral:(CBPeripheral*)peripheral {
    NSString* deviceId = peripheral.identifier.UUIDString;
    UNIBLE_LOG(@"didConnectPeripheral: %@", deviceId);

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
    UNIBLE_LOG(@"didDisconnectPeripheral: %@, error: %@", deviceId, error);

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
    NSString* deviceId = peripheral.identifier.UUIDString;
    NSString* characteristicUuid = [characteristic.UUID.UUIDString uppercaseString];
    NSString* serviceUuid = [characteristic.service.UUID.UUIDString uppercaseString];
    NSMutableDictionary* callbacks = self.peripheralCallbacks[deviceId];

    // Check if this is a read callback or notify callback
    NSString* readKey = [NSString stringWithFormat:@"readCallback_%@_%@", serviceUuid, characteristicUuid];
    NSValue* readCallbackValue = callbacks[readKey];

    if (readCallbackValue) {
        ReadCallback callback = (ReadCallback)[readCallbackValue pointerValue];
        if (callback) {
            if (error) {
                callback([deviceId UTF8String], [serviceUuid UTF8String], [characteristicUuid UTF8String], nil, 0, [error.localizedDescription UTF8String]);
            } else {
                NSData* data = characteristic.value;
                callback([deviceId UTF8String], [serviceUuid UTF8String], [characteristicUuid UTF8String], data.bytes, (int)data.length, nil);
            }
        }
        [callbacks removeObjectForKey:readKey];
    } else {
        // This is a notification
        NSString* notifyKey = [NSString stringWithFormat:@"notifyCallback_%@_%@", serviceUuid, characteristicUuid];
        NSValue* notifyCallbackValue = callbacks[notifyKey];
        if (notifyCallbackValue) {
            NotifyCallback callback = (NotifyCallback)[notifyCallbackValue pointerValue];
            if (callback) {
                NSData* data = characteristic.value;
                callback([deviceId UTF8String], [serviceUuid UTF8String], [characteristicUuid UTF8String], data.bytes, (int)data.length);
            }
        }
    }
}

- (void)peripheral:(CBPeripheral*)peripheral didWriteValueForCharacteristic:(CBCharacteristic*)characteristic error:(NSError*)error {
    NSString* deviceId = peripheral.identifier.UUIDString;
    NSString* characteristicUuid = [characteristic.UUID.UUIDString uppercaseString];
    NSString* serviceUuid = [characteristic.service.UUID.UUIDString uppercaseString];
    NSMutableDictionary* callbacks = self.peripheralCallbacks[deviceId];

    NSString* key = [NSString stringWithFormat:@"writeCallback_%@_%@", serviceUuid, characteristicUuid];
    NSValue* callbackValue = callbacks[key];
    if (callbackValue) {
        WriteCallback callback = (WriteCallback)[callbackValue pointerValue];
        if (callback) {
            if (error) {
                callback([deviceId UTF8String], [serviceUuid UTF8String], [characteristicUuid UTF8String], [error.localizedDescription UTF8String]);
            } else {
                callback([deviceId UTF8String], [serviceUuid UTF8String], [characteristicUuid UTF8String], nil);
            }
        }
        [callbacks removeObjectForKey:key];
    }
}

- (void)peripheral:(CBPeripheral*)peripheral didUpdateNotificationStateForCharacteristic:(CBCharacteristic*)characteristic error:(NSError*)error {
    NSString* deviceId = peripheral.identifier.UUIDString;
    NSString* characteristicUuid = [characteristic.UUID.UUIDString uppercaseString];
    NSString* serviceUuid = [characteristic.service.UUID.UUIDString uppercaseString];
    NSMutableDictionary* callbacks = self.peripheralCallbacks[deviceId];

    NSString* subscribeKey = [NSString stringWithFormat:@"subscribeCallback_%@_%@", serviceUuid, characteristicUuid];
    NSString* unsubscribeKey = [NSString stringWithFormat:@"unsubscribeCallback_%@_%@", serviceUuid, characteristicUuid];

    NSValue* subscribeCallbackValue = callbacks[subscribeKey];
    NSValue* unsubscribeCallbackValue = callbacks[unsubscribeKey];

    if (subscribeCallbackValue) {
        SubscribeCallback callback = (SubscribeCallback)[subscribeCallbackValue pointerValue];
        if (callback) {
            if (error) {
                callback([deviceId UTF8String], [serviceUuid UTF8String], [characteristicUuid UTF8String], [error.localizedDescription UTF8String]);
            } else {
                callback([deviceId UTF8String], [serviceUuid UTF8String], [characteristicUuid UTF8String], nil);
            }
        }
        [callbacks removeObjectForKey:subscribeKey];
    }

    if (unsubscribeCallbackValue) {
        SubscribeCallback callback = (SubscribeCallback)[unsubscribeCallbackValue pointerValue];
        if (callback) {
            if (error) {
                callback([deviceId UTF8String], [serviceUuid UTF8String], [characteristicUuid UTF8String], [error.localizedDescription UTF8String]);
            } else {
                callback([deviceId UTF8String], [serviceUuid UTF8String], [characteristicUuid UTF8String], nil);
            }
        }
        [callbacks removeObjectForKey:unsubscribeKey];
    }
}

@end

#pragma mark - C Interface

extern "C" {

void UniBle_SetDebugEnabled(bool enabled) {
    g_debugEnabled = enabled;
}

void UniBle_Initialize(StateChangedCallback stateCallback, DeviceDiscoveredCallback deviceCallback, DisconnectCallback disconnectCallback) {
    [[UniBleManager shared] initializeWithStateCallback:stateCallback deviceCallback:deviceCallback disconnectCallback:disconnectCallback];
}

bool UniBle_IsAvailable(void) {
    return [[UniBleManager shared] isAvailable];
}

void UniBle_StartScan(const char* serviceUuidsJson) {
    UNIBLE_LOG(@"UniBle_StartScan called with: %s", serviceUuidsJson ? serviceUuidsJson : "null");
    NSArray<CBUUID*>* uuids = nil;

    if (serviceUuidsJson) {
        NSString* json = [NSString stringWithUTF8String:serviceUuidsJson];
        UNIBLE_LOG(@"Parsing JSON: %@", json);
        // Parse simple JSON array: ["uuid1", "uuid2"]
        json = [json stringByTrimmingCharactersInSet:[NSCharacterSet whitespaceCharacterSet]];
        if ([json hasPrefix:@"["] && [json hasSuffix:@"]"]) {
            json = [json substringWithRange:NSMakeRange(1, json.length - 2)];
            NSArray* parts = [json componentsSeparatedByString:@","];
            NSMutableArray<CBUUID*>* mutableUuids = [NSMutableArray array];
            for (NSString* part in parts) {
                NSString* trimmed = [part stringByTrimmingCharactersInSet:[NSCharacterSet whitespaceCharacterSet]];
                trimmed = [trimmed stringByTrimmingCharactersInSet:[NSCharacterSet characterSetWithCharactersInString:@"\""]];
                if (trimmed.length > 0) {
                    CBUUID* uuid = [CBUUID UUIDWithString:trimmed];
                    if (uuid) {
                        [mutableUuids addObject:uuid];
                        UNIBLE_LOG(@"Added UUID: %@", uuid);
                    }
                }
            }
            if (mutableUuids.count > 0) {
                uuids = mutableUuids;
            }
        }
    }

    UNIBLE_LOG(@"Starting scan with %lu service UUIDs", (unsigned long)(uuids ? uuids.count : 0));
    UniBleManager* manager = [UniBleManager shared];
    dispatch_async(manager->_syncQueue, ^{
        [manager startScanWithServiceUuids:uuids];
    });
}

void UniBle_StopScan(void) {
    UniBleManager* manager = [UniBleManager shared];
    dispatch_async(manager->_syncQueue, ^{
        [manager stopScan];
    });
}

void UniBle_Connect(const char* deviceId, ConnectionCallback callback) {
    NSString* deviceIdStr = [NSString stringWithUTF8String:deviceId];
    UniBleManager* manager = [UniBleManager shared];
    dispatch_async(manager->_syncQueue, ^{
        [manager connectPeripheral:deviceIdStr callback:callback];
    });
}

void UniBle_Disconnect(const char* deviceId, DisconnectCallback callback) {
    NSString* deviceIdStr = [NSString stringWithUTF8String:deviceId];
    UniBleManager* manager = [UniBleManager shared];
    dispatch_async(manager->_syncQueue, ^{
        [manager disconnectPeripheral:deviceIdStr callback:callback];
    });
}

void UniBle_DiscoverServices(const char* deviceId, ServiceDiscoveryCallback callback) {
    NSString* deviceIdStr = [NSString stringWithUTF8String:deviceId];
    UniBleManager* manager = [UniBleManager shared];
    dispatch_async(manager->_syncQueue, ^{
        [manager discoverServicesForPeripheral:deviceIdStr callback:callback];
    });
}

void UniBle_DiscoverCharacteristics(const char* deviceId, const char* serviceUuid, CharacteristicDiscoveryCallback callback) {
    NSString* deviceIdStr = [NSString stringWithUTF8String:deviceId];
    NSString* serviceUuidStr = [NSString stringWithUTF8String:serviceUuid];
    UniBleManager* manager = [UniBleManager shared];
    dispatch_async(manager->_syncQueue, ^{
        [manager discoverCharacteristicsForPeripheral:deviceIdStr serviceUuid:serviceUuidStr callback:callback];
    });
}

void UniBle_ReadCharacteristic(const char* deviceId, const char* serviceUuid, const char* characteristicUuid, ReadCallback callback) {
    NSString* deviceIdStr = [NSString stringWithUTF8String:deviceId];
    NSString* serviceUuidStr = [NSString stringWithUTF8String:serviceUuid];
    NSString* characteristicUuidStr = [NSString stringWithUTF8String:characteristicUuid];
    UniBleManager* manager = [UniBleManager shared];
    dispatch_async(manager->_syncQueue, ^{
        [manager readCharacteristicForPeripheral:deviceIdStr serviceUuid:serviceUuidStr characteristicUuid:characteristicUuidStr callback:callback];
    });
}

void UniBle_WriteCharacteristic(const char* deviceId, const char* serviceUuid, const char* characteristicUuid, const void* data, int dataLength, bool withResponse, WriteCallback callback) {
    NSString* deviceIdStr = [NSString stringWithUTF8String:deviceId];
    NSString* serviceUuidStr = [NSString stringWithUTF8String:serviceUuid];
    NSString* characteristicUuidStr = [NSString stringWithUTF8String:characteristicUuid];
    NSData* nsData = [NSData dataWithBytes:data length:dataLength];
    UniBleManager* manager = [UniBleManager shared];
    dispatch_async(manager->_syncQueue, ^{
        [manager writeCharacteristicForPeripheral:deviceIdStr serviceUuid:serviceUuidStr characteristicUuid:characteristicUuidStr data:nsData withResponse:withResponse callback:callback];
    });
}

void UniBle_Subscribe(const char* deviceId, const char* serviceUuid, const char* characteristicUuid, NotifyCallback notifyCallback, SubscribeCallback resultCallback) {
    NSString* deviceIdStr = [NSString stringWithUTF8String:deviceId];
    NSString* serviceUuidStr = [NSString stringWithUTF8String:serviceUuid];
    NSString* characteristicUuidStr = [NSString stringWithUTF8String:characteristicUuid];
    UniBleManager* manager = [UniBleManager shared];
    dispatch_async(manager->_syncQueue, ^{
        [manager subscribeToCharacteristicForPeripheral:deviceIdStr serviceUuid:serviceUuidStr characteristicUuid:characteristicUuidStr notifyCallback:notifyCallback resultCallback:resultCallback];
    });
}

void UniBle_Unsubscribe(const char* deviceId, const char* serviceUuid, const char* characteristicUuid, SubscribeCallback resultCallback) {
    NSString* deviceIdStr = [NSString stringWithUTF8String:deviceId];
    NSString* serviceUuidStr = [NSString stringWithUTF8String:serviceUuid];
    NSString* characteristicUuidStr = [NSString stringWithUTF8String:characteristicUuid];
    UniBleManager* manager = [UniBleManager shared];
    dispatch_async(manager->_syncQueue, ^{
        [manager unsubscribeFromCharacteristicForPeripheral:deviceIdStr serviceUuid:serviceUuidStr characteristicUuid:characteristicUuidStr callback:resultCallback];
    });
}

}
