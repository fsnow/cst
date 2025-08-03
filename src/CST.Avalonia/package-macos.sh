#!/bin/bash

# CST Avalonia macOS Packaging Script
# This script builds and packages the CST Avalonia application for macOS

set -e  # Exit on error

echo "CST Avalonia macOS Packaging Script"
echo "=================================="

# Configuration
APP_NAME="CST"
BUNDLE_NAME="CST.app"
PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BUILD_DIR="$PROJECT_DIR/bin/Release/net9.0/osx-arm64"
PUBLISH_DIR="$PROJECT_DIR/bin/Release/net9.0/osx-arm64/publish"
XSL_SOURCE_DIR="$PROJECT_DIR/../Cst4/Xsl"

# Clean previous builds
echo "Cleaning previous builds..."
rm -rf "$BUNDLE_NAME"
rm -rf "$BUILD_DIR"
rm -rf "$PUBLISH_DIR"

# Build the application
echo "Building CST Avalonia for macOS ARM64..."
dotnet publish -c Release -r osx-arm64 --self-contained -p:PublishSingleFile=false

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
echo "Bundle: $BUNDLE_NAME"
echo "Size: $(du -sh "$BUNDLE_NAME" | cut -f1)"
echo "XSL files: $(ls -1 "$BUNDLE_NAME/Contents/Resources/Xsl/"*.xsl 2>/dev/null | wc -l)"
echo ""
echo "To test the application, run:"
echo "  open $BUNDLE_NAME"
echo ""
echo "To enable debug logging, edit the launch script:"
echo "  Right-click $BUNDLE_NAME → Show Package Contents → Contents/MacOS/CST"
echo "  Uncomment: export CST_LOG_LEVEL=debug"
echo ""
echo "To distribute, you can create a DMG or compress the .app bundle."