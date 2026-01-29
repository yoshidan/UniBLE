#!/bin/bash

# Build script for UniBlePlugin Android library (.aar)

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUTPUT_DIR="$SCRIPT_DIR/../../Assets/UniBLE/Runtime/Plugins/Android"
AAR_NAME="UniBlePlugin.aar"

# Gradle build output path
AAR_BUILD="$SCRIPT_DIR/build/outputs/aar/android-release.aar"

# Check prerequisites
echo "Checking build prerequisites..."

if ! command -v java >/dev/null 2>&1; then
    echo "Error: java not found. Install JDK." >&2
    exit 1
fi

if [ -f "$SCRIPT_DIR/gradlew" ]; then
    GRADLE_CMD="$SCRIPT_DIR/gradlew"
elif command -v gradle >/dev/null 2>&1; then
    GRADLE_CMD="gradle"
else
    echo "Error: Neither gradlew nor gradle found. Install Gradle or run 'gradle wrapper' in $SCRIPT_DIR" >&2
    exit 1
fi

echo "Using Gradle: $GRADLE_CMD"
echo "Java version: $(java -version 2>&1 | head -1)"

# Create output directory if it doesn't exist
mkdir -p "$OUTPUT_DIR"

# Build the release .aar
echo "Building Android plugin (assembleRelease)..."
cd "$SCRIPT_DIR"
$GRADLE_CMD assembleRelease --quiet

# Verify build output exists
if [ ! -f "$AAR_BUILD" ]; then
    echo "Error: Build output not found at $AAR_BUILD" >&2
    exit 1
fi

# Copy .aar to Unity plugins directory
cp "$AAR_BUILD" "$OUTPUT_DIR/$AAR_NAME"
echo "Copied .aar to $OUTPUT_DIR/$AAR_NAME"

echo "Build complete: $OUTPUT_DIR/$AAR_NAME"
