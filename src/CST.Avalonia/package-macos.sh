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

# Copy all published files to MacOS directory
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
echo "  ./package-macos.sh arm64  # Apple Silicon (M1/M2/M3)"
echo "  ./package-macos.sh x64    # Intel Macs"