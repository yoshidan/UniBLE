#!/bin/bash

# Build script for UniBlePlugin iOS static library

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUTPUT_DIR="$SCRIPT_DIR/../../Assets/UniBLE/Runtime/Plugins/iOS"
LIB_NAME="libUniBlePlugin.a"

# iOS SDK path
IOS_SDK=$(xcrun --sdk iphoneos --show-sdk-path)
IOS_SIM_SDK=$(xcrun --sdk iphonesimulator --show-sdk-path)

# Create output directory if it doesn't exist
mkdir -p "$OUTPUT_DIR"

# Remove old library if it exists
rm -f "$OUTPUT_DIR/$LIB_NAME"

# Compile for iOS device (arm64)
echo "Compiling UniBlePlugin for iOS device (arm64)..."
clang++ -arch arm64 \
    -isysroot "$IOS_SDK" \
    -miphoneos-version-min=12.0 \
    -framework CoreBluetooth \
    -framework Foundation \
    -std=c++11 \
    -fPIC \
    -fobjc-arc \
    -c \
    -o "$OUTPUT_DIR/UniBlePlugin_arm64.o" \
    "$SCRIPT_DIR/UniBlePlugin.mm"

# Compile for iOS simulator (arm64 for Apple Silicon)
echo "Compiling UniBlePlugin for iOS simulator (arm64)..."
clang++ -arch arm64 \
    -isysroot "$IOS_SIM_SDK" \
    -mios-simulator-version-min=12.0 \
    -framework CoreBluetooth \
    -framework Foundation \
    -std=c++11 \
    -fPIC \
    -fobjc-arc \
    -c \
    -o "$OUTPUT_DIR/UniBlePlugin_sim_arm64.o" \
    "$SCRIPT_DIR/UniBlePlugin.mm"

# Compile for iOS simulator (x86_64 for Intel Mac)
echo "Compiling UniBlePlugin for iOS simulator (x86_64)..."
clang++ -arch x86_64 \
    -isysroot "$IOS_SIM_SDK" \
    -mios-simulator-version-min=12.0 \
    -framework CoreBluetooth \
    -framework Foundation \
    -std=c++11 \
    -fPIC \
    -fobjc-arc \
    -c \
    -o "$OUTPUT_DIR/UniBlePlugin_sim_x86_64.o" \
    "$SCRIPT_DIR/UniBlePlugin.mm"

# Create static libraries
echo "Creating static libraries..."
ar rcs "$OUTPUT_DIR/libUniBlePlugin_device.a" "$OUTPUT_DIR/UniBlePlugin_arm64.o"
ar rcs "$OUTPUT_DIR/libUniBlePlugin_sim_arm64.a" "$OUTPUT_DIR/UniBlePlugin_sim_arm64.o"
ar rcs "$OUTPUT_DIR/libUniBlePlugin_sim_x86_64.a" "$OUTPUT_DIR/UniBlePlugin_sim_x86_64.o"

# Create fat library for simulator (arm64 + x86_64)
echo "Creating fat library for simulator..."
lipo -create \
    "$OUTPUT_DIR/libUniBlePlugin_sim_arm64.a" \
    "$OUTPUT_DIR/libUniBlePlugin_sim_x86_64.a" \
    -output "$OUTPUT_DIR/libUniBlePlugin_simulator.a"

# Create XCFramework (recommended for modern iOS development)
echo "Creating XCFramework..."
rm -rf "$OUTPUT_DIR/UniBlePlugin.xcframework"
xcodebuild -create-xcframework \
    -library "$OUTPUT_DIR/libUniBlePlugin_device.a" \
    -library "$OUTPUT_DIR/libUniBlePlugin_simulator.a" \
    -output "$OUTPUT_DIR/UniBlePlugin.xcframework"

# Also keep the device-only static library for Unity (Unity often uses .a directly)
cp "$OUTPUT_DIR/libUniBlePlugin_device.a" "$OUTPUT_DIR/$LIB_NAME"

# Clean up intermediate files
rm -f "$OUTPUT_DIR/UniBlePlugin_arm64.o"
rm -f "$OUTPUT_DIR/UniBlePlugin_sim_arm64.o"
rm -f "$OUTPUT_DIR/UniBlePlugin_sim_x86_64.o"
rm -f "$OUTPUT_DIR/libUniBlePlugin_device.a"
rm -f "$OUTPUT_DIR/libUniBlePlugin_sim_arm64.a"
rm -f "$OUTPUT_DIR/libUniBlePlugin_sim_x86_64.a"
rm -f "$OUTPUT_DIR/libUniBlePlugin_simulator.a"

# Sync source files to iOS plugin directory (single source of truth)
echo "Syncing source files to iOS plugin directory..."
cp "$SCRIPT_DIR/UniBlePlugin.h" "$OUTPUT_DIR/UniBlePlugin.h"
cp "$SCRIPT_DIR/UniBlePlugin.mm" "$OUTPUT_DIR/UniBlePlugin.mm"

echo "Build complete: $OUTPUT_DIR/$LIB_NAME"
echo "XCFramework: $OUTPUT_DIR/UniBlePlugin.xcframework"
