#!/bin/bash

# Build script for UniBlePlugin macOS bundle

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUTPUT_DIR="$SCRIPT_DIR/../../Assets/UniBLE/Runtime/Plugins/macOS"
BUNDLE_NAME="UniBlePlugin.bundle"

# Create output directory if it doesn't exist
mkdir -p "$OUTPUT_DIR"

# Remove old bundle if it exists
rm -rf "$OUTPUT_DIR/$BUNDLE_NAME"

# Create bundle structure
mkdir -p "$OUTPUT_DIR/$BUNDLE_NAME/Contents/MacOS"

# Compile the plugin for both arm64 and x86_64
echo "Compiling UniBlePlugin for arm64..."
clang++ -arch arm64 \
    -framework CoreBluetooth \
    -framework Foundation \
    -dynamiclib \
    -std=c++11 \
    -fPIC \
    -o "$OUTPUT_DIR/$BUNDLE_NAME/Contents/MacOS/UniBlePlugin_arm64" \
    "$SCRIPT_DIR/UniBlePlugin.mm"

echo "Compiling UniBlePlugin for x86_64..."
clang++ -arch x86_64 \
    -framework CoreBluetooth \
    -framework Foundation \
    -dynamiclib \
    -std=c++11 \
    -fPIC \
    -o "$OUTPUT_DIR/$BUNDLE_NAME/Contents/MacOS/UniBlePlugin_x86_64" \
    "$SCRIPT_DIR/UniBlePlugin.mm"

# Create universal binary
echo "Creating universal binary..."
lipo -create \
    "$OUTPUT_DIR/$BUNDLE_NAME/Contents/MacOS/UniBlePlugin_arm64" \
    "$OUTPUT_DIR/$BUNDLE_NAME/Contents/MacOS/UniBlePlugin_x86_64" \
    -output "$OUTPUT_DIR/$BUNDLE_NAME/Contents/MacOS/UniBlePlugin"

# Clean up architecture-specific binaries
rm "$OUTPUT_DIR/$BUNDLE_NAME/Contents/MacOS/UniBlePlugin_arm64"
rm "$OUTPUT_DIR/$BUNDLE_NAME/Contents/MacOS/UniBlePlugin_x86_64"

# Create Info.plist
cat > "$OUTPUT_DIR/$BUNDLE_NAME/Contents/Info.plist" << 'EOF'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleDevelopmentRegion</key>
    <string>en</string>
    <key>CFBundleExecutable</key>
    <string>UniBlePlugin</string>
    <key>CFBundleIdentifier</key>
    <string>com.unible.plugin</string>
    <key>CFBundleInfoDictionaryVersion</key>
    <string>6.0</string>
    <key>CFBundleName</key>
    <string>UniBlePlugin</string>
    <key>CFBundlePackageType</key>
    <string>BNDL</string>
    <key>CFBundleShortVersionString</key>
    <string>1.0</string>
    <key>CFBundleVersion</key>
    <string>1</string>
    <key>NSBluetoothAlwaysUsageDescription</key>
    <string>This app uses Bluetooth to communicate with BLE devices.</string>
    <key>NSPrincipalClass</key>
    <string></string>
</dict>
</plist>
EOF

echo "Build complete: $OUTPUT_DIR/$BUNDLE_NAME"
