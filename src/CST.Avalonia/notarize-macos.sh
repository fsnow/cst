#!/bin/bash

# CST Avalonia macOS Notarization Script
# This script submits the app for notarization to Apple
# Prerequisites:
#   - Valid Apple Developer account
#   - App-specific password created at appleid.apple.com
#   - Developer ID certificate installed

set -e  # Exit on error

# Parse arguments
ARCH=${1:-arm64}
APP_PATH="CST Reader.app"
DMG_PATH="dist/CST-Reader-${ARCH}.dmg"

# Color output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo "CST Avalonia macOS Notarization Script"
echo "======================================"
echo ""

# Check if DMG exists
if [ ! -f "$DMG_PATH" ]; then
    echo -e "${RED}Error: DMG not found at $DMG_PATH${NC}"
    echo "Please run ./package-macos.sh $ARCH first"
    exit 1
fi

# Check for required environment variables
if [ -z "$APPLE_ID" ]; then
    echo -e "${YELLOW}APPLE_ID environment variable not set${NC}"
    echo "Please set your Apple ID:"
    echo "  export APPLE_ID='your.email@example.com'"
    exit 1
fi

if [ -z "$APPLE_APP_PASSWORD" ]; then
    echo -e "${YELLOW}APPLE_APP_PASSWORD environment variable not set${NC}"
    echo "Please create an app-specific password at https://appleid.apple.com"
    echo "Then set it:"
    echo "  export APPLE_APP_PASSWORD='xxxx-xxxx-xxxx-xxxx'"
    exit 1
fi

if [ -z "$APPLE_TEAM_ID" ]; then
    echo -e "${YELLOW}APPLE_TEAM_ID environment variable not set${NC}"
    echo "Using default from certificate: 69M77LM9K3"
    APPLE_TEAM_ID="69M77LM9K3"
fi

# Create a unique submission ID
SUBMISSION_ID="cst-reader-$(date +%Y%m%d-%H%M%S)"

echo "Submitting for notarization..."
echo "  DMG: $DMG_PATH"
echo "  Apple ID: $APPLE_ID"
echo "  Team ID: $APPLE_TEAM_ID"
echo "  Submission ID: $SUBMISSION_ID"
echo ""

# Submit for notarization using notarytool (modern method for Xcode 13+)
echo "Uploading to Apple notary service..."
xcrun notarytool submit "$DMG_PATH" \
    --apple-id "$APPLE_ID" \
    --password "$APPLE_APP_PASSWORD" \
    --team-id "$APPLE_TEAM_ID" \
    --wait \
    --output-format json > /tmp/notarization-result.json

# Check the result
STATUS=$(cat /tmp/notarization-result.json | python3 -c "import sys, json; print(json.load(sys.stdin)['status'])")

if [ "$STATUS" = "Accepted" ]; then
    echo -e "${GREEN}✅ Notarization successful!${NC}"

    # Staple the notarization ticket to the DMG
    echo "Waiting for notarization ticket to propagate..."
    sleep 10  # Give Apple's CDN time to sync

    echo "Stapling notarization ticket to DMG..."
    STAPLE_ATTEMPTS=0
    MAX_ATTEMPTS=3

    while [ $STAPLE_ATTEMPTS -lt $MAX_ATTEMPTS ]; do
        if xcrun stapler staple "$DMG_PATH" 2>/dev/null; then
            echo -e "${GREEN}✅ Stapling successful!${NC}"
            break
        else
            STAPLE_ATTEMPTS=$((STAPLE_ATTEMPTS + 1))
            if [ $STAPLE_ATTEMPTS -lt $MAX_ATTEMPTS ]; then
                echo "Stapling failed, retrying in 10 seconds... (attempt $STAPLE_ATTEMPTS/$MAX_ATTEMPTS)"
                sleep 10
            else
                echo -e "${YELLOW}⚠️  Warning: Could not staple DMG after $MAX_ATTEMPTS attempts${NC}"
                echo "The notarization was successful but stapling failed."
                echo "You can try stapling manually later with:"
                echo "  xcrun stapler staple '$DMG_PATH'"
                break
            fi
        fi
    done

    if [ $STAPLE_ATTEMPTS -lt $MAX_ATTEMPTS ]; then

        # Extract and staple the app as well
        echo "Extracting app from DMG to staple it..."
        TEMP_MOUNT="/tmp/cst-notarize-mount"
        mkdir -p "$TEMP_MOUNT"
        hdiutil attach "$DMG_PATH" -nobrowse -quiet -mountpoint "$TEMP_MOUNT"

        # Copy out the app
        cp -R "$TEMP_MOUNT/CST Reader.app" .

        # Detach DMG
        hdiutil detach "$TEMP_MOUNT" -quiet
        rmdir "$TEMP_MOUNT"

        # Staple the app
        echo "Stapling notarization ticket to app..."
        xcrun stapler staple "CST Reader.app"

        if [ $? -eq 0 ]; then
            echo -e "${GREEN}✅ App stapling successful!${NC}"
            echo ""
            echo "Notarization complete!"
            echo "===================="
            echo "Both the DMG and app are now notarized and stapled."
            echo ""
            echo "Distribution files ready:"
            echo "  - DMG: $DMG_PATH (notarized & stapled)"
            echo "  - App: CST Reader.app (notarized & stapled)"
            echo ""
            echo "Users can now install without Gatekeeper warnings!"
        else
            echo -e "${YELLOW}⚠️  Warning: Could not staple the app${NC}"
        fi
    else
        echo -e "${RED}❌ Stapling failed${NC}"
        exit 1
    fi

elif [ "$STATUS" = "Invalid" ]; then
    echo -e "${RED}❌ Notarization failed - Invalid${NC}"
    echo "Apple found issues with the app. Check the log for details:"

    # Get the submission ID for log retrieval
    SUBMISSION_UUID=$(cat /tmp/notarization-result.json | python3 -c "import sys, json; print(json.load(sys.stdin)['id'])")

    echo "Fetching detailed log..."
    xcrun notarytool log "$SUBMISSION_UUID" \
        --apple-id "$APPLE_ID" \
        --password "$APPLE_APP_PASSWORD" \
        --team-id "$APPLE_TEAM_ID"

    exit 1
else
    echo -e "${RED}❌ Notarization failed with status: $STATUS${NC}"
    cat /tmp/notarization-result.json
    exit 1
fi

# Clean up
rm -f /tmp/notarization-result.json

echo ""
echo "Next steps:"
echo "1. Test the notarized app: open 'CST Reader.app'"
echo "2. Distribute the notarized DMG: $DMG_PATH"
echo "3. Users should see no Gatekeeper warnings!"