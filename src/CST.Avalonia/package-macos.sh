#!/bin/bash

# CST Avalonia macOS Packaging Script
# This script builds and packages the CST Avalonia application for macOS
# Usage: ./package-macos.sh [architecture]
# Architecture options: arm64 (default), x64

set -e  # Exit on error

# Parse architecture argument
ARCH=${1:-arm64}  # Default to arm64 if no argument provided

# Validate architecture
case $ARCH in
    arm64)
        RID="osx-arm64"
        ARCH_NAME="Apple Silicon"
        ;;
    x64)
        RID="osx-x64"
        ARCH_NAME="Intel"
        ;;
    *)
        echo "Error: Invalid architecture '$ARCH'"
        echo "Usage: $0 [arm64|x64]"
        echo "  arm64 - Build for Apple Silicon Macs (default)"
        echo "  x64   - Build for Intel Macs"
        exit 1
        ;;
esac

echo "CST Avalonia macOS Packaging Script"
echo "=================================="
echo "Architecture: $ARCH_NAME ($ARCH)"
echo ""

# Configuration
APP_NAME="CST Reader"
BUNDLE_NAME="CST Reader.app"
PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BUILD_DIR="$PROJECT_DIR/bin/Release/net9.0/$RID"
PUBLISH_DIR="$PROJECT_DIR/bin/Release/net9.0/$RID/publish"
XSL_SOURCE_DIR="$PROJECT_DIR/Xsl"
DIST_DIR="$PROJECT_DIR/dist"

# Check for available signing identities early
SIGNING_IDENTITY=$(security find-identity -v -p codesigning | grep "Developer ID Application" | head -1 | sed 's/^[[:space:]]*[0-9]*)[[:space:]]*[A-Z0-9]*[[:space:]]*"//' | sed 's/".*$//')

# Clean previous builds
echo "Cleaning previous builds..."
rm -rf "$BUNDLE_NAME"
rm -rf "$BUILD_DIR"
rm -rf "$PUBLISH_DIR"

# Build the application
echo "Building CST Avalonia for macOS $ARCH_NAME..."
dotnet publish -c Release -r $RID --self-contained -p:PublishSingleFile=false

# Create app bundle structure
echo "Creating app bundle structure..."
mkdir -p "$BUNDLE_NAME/Contents/MacOS"
mkdir -p "$BUNDLE_NAME/Contents/Resources"
mkdir -p "$BUNDLE_NAME/Contents/Resources/Xsl"

# Copy Info.plist
echo "Copying Info.plist..."
cp "$PROJECT_DIR/Info.plist" "$BUNDLE_NAME/Contents/"

# Copy app icon
echo "Copying app icon..."
if [ -f "$PROJECT_DIR/Assets/cst.icns" ]; then
    cp "$PROJECT_DIR/Assets/cst.icns" "$BUNDLE_NAME/Contents/Resources/AppIcon.icns"
    echo "  - Icon copied successfully"
else
    echo "  WARNING: Icon file not found at $PROJECT_DIR/Assets/cst.icns"
fi

# Copy all published files to MacOS directory first
echo "Copying application files..."
cp -R "$PUBLISH_DIR/"* "$BUNDLE_NAME/Contents/MacOS/"


# Copy XSL files to Resources
echo "Copying XSL files..."
if [ -d "$XSL_SOURCE_DIR" ]; then
    cp "$XSL_SOURCE_DIR"/*.xsl "$BUNDLE_NAME/Contents/Resources/Xsl/" 2>/dev/null || true
    echo "  - Copied $(ls -1 "$BUNDLE_NAME/Contents/Resources/Xsl/"*.xsl 2>/dev/null | wc -l) XSL files"
else
    echo "  WARNING: XSL source directory not found at $XSL_SOURCE_DIR"
fi

# Create launch script
echo "Creating launch script..."
cat > "$BUNDLE_NAME/Contents/MacOS/CST" << 'EOF'
#!/bin/bash

# Get the directory of this script (Contents/MacOS)
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Change to the MacOS directory where the executable is located
cd "$DIR"

# Set environment variables for the .NET runtime
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=0

# CST Application Settings
# Uncomment and modify the line below to change log level:
# export CST_LOG_LEVEL=debug
# Options: debug, information, warning, error, fatal

# Launch the CST.Avalonia application
exec ./CST.Avalonia "$@"
EOF

# Make launch script executable
chmod +x "$BUNDLE_NAME/Contents/MacOS/CST"

# Fix line endings in launch script (convert to Unix)
if command -v dos2unix &> /dev/null; then
    dos2unix "$BUNDLE_NAME/Contents/MacOS/CST"
else
    # Use sed as fallback
    sed -i '' 's/\r$//' "$BUNDLE_NAME/Contents/MacOS/CST"
fi

# Set executable permissions
echo "Setting executable permissions..."
chmod +x "$BUNDLE_NAME/Contents/MacOS/CST.Avalonia"
find "$BUNDLE_NAME/Contents/MacOS" -name "*.dylib" -exec chmod +x {} \;

# Code signing
echo ""
echo "Code signing the application..."

if [ -n "$SIGNING_IDENTITY" ]; then
    echo "Found signing identity: $SIGNING_IDENTITY"

    # Create entitlements file for .NET runtime (with all required keys)
    ENTITLEMENTS_FILE="/tmp/cst-entitlements.plist"
    cat > "$ENTITLEMENTS_FILE" << 'EOF'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>com.apple.security.cs.allow-jit</key>
    <true/>
    <key>com.apple.security.cs.allow-unsigned-executable-memory</key>
    <true/>
    <key>com.apple.security.cs.disable-library-validation</key>
    <true/>
</dict>
</plist>
EOF


    # First pass: Sign all files EXCEPT CST.Avalonia to satisfy deep verification
    echo "Signing all components except main executable..."
    find "$BUNDLE_NAME/Contents/MacOS" -type f ! -name "CST.Avalonia" -exec codesign --force --sign "$SIGNING_IDENTITY" {} \;

    # Second pass: Re-sign true executables WITH the hardened runtime option (still excluding CST.Avalonia)
    echo "Applying Hardened Runtime to executables..."
    find "$BUNDLE_NAME/Contents/MacOS" -name "*.dylib" -o -name "*.dll" | xargs -I {} codesign --force --options runtime --sign "$SIGNING_IDENTITY" "{}"
    find "$BUNDLE_NAME/Contents/MacOS/CefGlueBrowserProcess" -type f -perm +111 -exec codesign --force --options runtime --sign "$SIGNING_IDENTITY" {} \; 2>/dev/null || true
    # Sign createdump and any other executable files with hardened runtime
    find "$BUNDLE_NAME/Contents/MacOS" -type f -perm +111 -name "createdump" -exec codesign --force --options runtime --sign "$SIGNING_IDENTITY" {} \;
    codesign --force --options runtime --sign "$SIGNING_IDENTITY" "$BUNDLE_NAME/Contents/MacOS/CST"

    # Sign the main executable with entitlements and the runtime option
    echo "Signing main executable with entitlements..."
    codesign --force --options runtime --entitlements "$ENTITLEMENTS_FILE" --sign "$SIGNING_IDENTITY" "$BUNDLE_NAME/Contents/MacOS/CST.Avalonia"

    # Sign the entire app bundle to seal it (with entitlements to preserve them)
    echo "Signing app bundle..."
    codesign --force --options runtime --entitlements "$ENTITLEMENTS_FILE" --sign "$SIGNING_IDENTITY" "$BUNDLE_NAME"

    # Clean up entitlements file
    rm -f "$ENTITLEMENTS_FILE"

    # Verify the entire app bundle with strict deep verification
    echo "Verifying app bundle signatures..."
    if codesign --verify --deep --strict --verbose=2 "$BUNDLE_NAME" 2>/dev/null; then
        echo "✅ App bundle signature verification passed!"
    else
        echo "❌ App bundle signature verification failed"
        exit 1
    fi

    # Test with Gatekeeper simulation
    echo "Testing with Gatekeeper simulation..."
    if spctl --assess --type execute --verbose "$BUNDLE_NAME" 2>/dev/null; then
        echo "✅ Gatekeeper simulation passed!"
    else
        echo "⚠️  Gatekeeper simulation failed (may require notarization for full acceptance)"
    fi

    echo "✅ Code signing completed successfully!"
    echo "   Note: App bundle is properly signed and sealed"
    echo "   Note: All components including main executable are signed"
else
    echo "⚠️  No Developer ID Application certificate found in keychain."
    echo "   The app will be unsigned and users will see security warnings."
    echo "   To add code signing:"
    echo "   1. Download your Developer ID Application certificate from Apple Developer Portal"
    echo "   2. Double-click the .cer file to install it in Keychain Access"
    echo "   3. Run this script again"
fi

# Display bundle info
echo ""
echo "App bundle created successfully!"
echo "================================"
echo "Architecture: $ARCH_NAME ($ARCH)"
echo "Bundle: $BUNDLE_NAME"
echo "Size: $(du -sh "$BUNDLE_NAME" | cut -f1)"
echo "XSL files: $(ls -1 "$BUNDLE_NAME/Contents/Resources/Xsl/"*.xsl 2>/dev/null | wc -l)"

# Create DMG if create-dmg is available
if command -v create-dmg &> /dev/null; then
    echo ""
    echo "Creating DMG installer..."
    
    # Create dist directory if it doesn't exist
    mkdir -p "$DIST_DIR"
    
    DMG_NAME="CST-Reader-${ARCH}.dmg"
    DMG_PATH="$DIST_DIR/$DMG_NAME"
    
    # Remove old DMG if it exists
    rm -f "$DMG_PATH"
    
    # Create DMG with create-dmg
    create-dmg \
        --volname "CST Reader" \
        --window-pos 200 120 \
        --window-size 600 400 \
        --icon-size 100 \
        --icon "$BUNDLE_NAME" 175 120 \
        --hide-extension "$BUNDLE_NAME" \
        --app-drop-link 425 120 \
        --no-internet-enable \
        "$DMG_PATH" \
        "$BUNDLE_NAME"
    
    if [ $? -eq 0 ]; then
        echo ""
        echo "DMG created successfully!"
        echo "DMG file: $DMG_PATH"
        echo "DMG size: $(du -sh "$DMG_PATH" | cut -f1)"

        # Sign the DMG if we have a signing identity
        if [ -n "$SIGNING_IDENTITY" ]; then
            echo ""
            echo "Signing DMG..."
            codesign --force --sign "$SIGNING_IDENTITY" "$DMG_PATH"
            if codesign --verify --verbose "$DMG_PATH"; then
                echo "✅ DMG signed successfully!"
            else
                echo "❌ DMG signature verification failed!"
            fi
        fi

        # Clean up the .app bundle since we have the DMG
        echo "Cleaning up temporary .app bundle..."
        rm -rf "$BUNDLE_NAME"
    else
        echo ""
        echo "Failed to create DMG. You can still distribute the .app bundle directly."
    fi
else
    echo ""
    echo "Note: create-dmg not found. Install with 'brew install create-dmg' to automatically create DMG."
    echo "The .app bundle is available at: $BUNDLE_NAME"
fi

echo ""
if [ -f "$DMG_PATH" ]; then
    echo "Distribution file ready in: $DIST_DIR/"
    echo "To test the application:"
    echo "  open \"$DMG_PATH\""
else
    echo "To test the application:"
    echo "  open \"$BUNDLE_NAME\""
fi
echo ""
echo "To enable debug logging:"
echo "  1. Install the app from the DMG"
echo "  2. Right-click CST Reader in Applications → Show Package Contents"
echo "  3. Navigate to Contents/MacOS/CST"
echo "  4. Uncomment: export CST_LOG_LEVEL=debug"
echo ""
echo "To build for other architectures:"
echo "  ./package-macos.sh arm64  # Apple Silicon (M1 - M4)"
echo "  ./package-macos.sh x64    # Intel Macs"