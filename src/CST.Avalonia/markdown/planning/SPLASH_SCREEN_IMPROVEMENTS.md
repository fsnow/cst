# Splash Screen UI Improvements

**Date**: December 2024  
**Status**: TODO for tomorrow

## Current Issues

The splash screen is now functional on macOS but has several UI issues that need to be addressed:

1. **Status text position**: Currently in the middle, should be at the bottom
2. **Progress bar obstruction**: The progress bar is obscuring the splash screen image
3. **Size too small**: Both the window and image need to be larger for better visibility
4. **Timeout too short**: 60 seconds is insufficient for downloading and indexing all files

## Required Changes

### 1. Move Status Text to Bottom
- Relocate the status label to the bottom of the splash screen
- Ensure it doesn't overlap with the image

### 2. Remove Progress Bar
- The progress bar is currently covering part of the image
- Either remove it entirely or find a better placement that doesn't obstruct the image

### 3. Increase Window and Image Size
- Make the splash screen window larger (current: 400x300)
- Scale the image appropriately to fill the larger window
- Consider a size around 600x450 or larger

### 4. Remove Timeout
- Current 60-second safety timer is too restrictive
- Full download and indexing can take several minutes
- Either remove the timeout entirely or increase to 5+ minutes

## Implementation Notes

- Changes primarily in `/Views/SplashScreen.axaml` for layout
- Adjust window dimensions in XAML
- Modify `/Views/SplashScreen.axaml.cs` to remove/adjust timer
- Ensure changes work correctly on macOS

## Testing Scenarios

1. Test with no files (full download needed)
2. Test with partial files (some downloads + indexing)
3. Test with all files present (minimal work)
4. Verify appearance on macOS with new layout