# ==============================================================================
# UniBLE - Native Plugin Build System
# ==============================================================================
#
# Build native BLE plugins for all supported platforms.
# Each platform delegates to its own build script.
#
# Targets:
#   make all       - Build all platforms (ios, macos, android)
#   make ios       - Build iOS static library (.a) and XCFramework
#   make macos     - Build macOS universal bundle (.bundle)
#   make android   - Build Android library (.aar)
#   make clean     - Remove all build artifacts
#   make help      - Show this help message
#
# Prerequisites:
#   iOS/macOS: Xcode command line tools (clang++, xcodebuild, lipo)
#   Android:   Java JDK, Gradle (or gradlew)
#
# ==============================================================================

.PHONY: all ios macos android clean help

# Directories
ROOT_DIR       := $(shell pwd)
NATIVE_DIR     := $(ROOT_DIR)/UniBlePlugin~
APPLE_SRC_DIR  := $(NATIVE_DIR)/apple
ANDROID_DIR    := $(NATIVE_DIR)/android
PLUGIN_DIR     := $(ROOT_DIR)/Assets/UniBLE/Runtime/Plugins

# Apple source files (used as dependencies)
APPLE_SRC      := $(APPLE_SRC_DIR)/UniBlePlugin.mm
APPLE_HDR      := $(APPLE_SRC_DIR)/UniBlePlugin.h

# Output directories
IOS_OUT        := $(PLUGIN_DIR)/iOS
MACOS_OUT      := $(PLUGIN_DIR)/macOS
ANDROID_OUT    := $(PLUGIN_DIR)/Android

# Output artifacts
IOS_LIB        := $(IOS_OUT)/libUniBlePlugin.a
MACOS_BUNDLE   := $(MACOS_OUT)/UniBlePlugin.bundle
ANDROID_AAR    := $(ANDROID_OUT)/UniBlePlugin.aar

# ==============================================================================
# Default target
# ==============================================================================

all: ios macos android ## Build all platforms
	@echo ""
	@echo "======================================"
	@echo " All platforms built successfully!"
	@echo "======================================"

# ==============================================================================
# iOS build
# ==============================================================================

ios: $(IOS_LIB) ## Build iOS static library and XCFramework
	@echo "[iOS] Build complete"

$(IOS_LIB): $(APPLE_SRC) $(APPLE_HDR)
	@echo ""
	@echo "======================================"
	@echo " Building iOS native plugin"
	@echo "======================================"
	@cd "$(APPLE_SRC_DIR)" && bash build_ios.sh

# ==============================================================================
# macOS build
# ==============================================================================

macos: $(MACOS_BUNDLE) ## Build macOS universal bundle
	@echo "[macOS] Build complete"

$(MACOS_BUNDLE): $(APPLE_SRC) $(APPLE_HDR)
	@echo ""
	@echo "======================================"
	@echo " Building macOS native plugin"
	@echo "======================================"
	@cd "$(APPLE_SRC_DIR)" && bash build_macos.sh

# ==============================================================================
# Android build
# ==============================================================================

android: $(ANDROID_AAR) ## Build Android library (.aar)
	@echo "[Android] Build complete"

$(ANDROID_AAR): $(shell find $(ANDROID_DIR)/src -type f 2>/dev/null) $(ANDROID_DIR)/build.gradle
	@echo ""
	@echo "======================================"
	@echo " Building Android native plugin"
	@echo "======================================"
	@cd "$(ANDROID_DIR)" && bash build_android.sh

# ==============================================================================
# Clean
# ==============================================================================

clean: ## Remove all build artifacts
	@echo "Cleaning build artifacts..."
	@# iOS
	rm -f "$(IOS_LIB)"
	rm -rf "$(IOS_OUT)/UniBlePlugin.xcframework"
	@# macOS
	rm -rf "$(MACOS_BUNDLE)"
	@# Android
	rm -f "$(ANDROID_AAR)"
	@if [ -d "$(ANDROID_DIR)/build" ]; then \
		rm -rf "$(ANDROID_DIR)/build"; \
	fi
	@echo "Clean complete"

# ==============================================================================
# Help
# ==============================================================================

help: ## Show this help message
	@echo "UniBLE Native Plugin Build System"
	@echo ""
	@echo "Usage: make [target]"
	@echo ""
	@echo "Targets:"
	@grep -E '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) | \
		awk 'BEGIN {FS = ":.*?## "}; {printf "  %-20s %s\n", $$1, $$2}'
	@echo ""
	@echo "Output locations:"
	@echo "  iOS:     $(IOS_OUT)/"
	@echo "  macOS:   $(MACOS_OUT)/"
	@echo "  Android: $(ANDROID_OUT)/"
