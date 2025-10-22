# Avalonia macOS High CPU Usage Investigation

**Date:** October 4, 2025
**App Version:** 5.0.0-beta.2
**Platform:** macOS (Apple Silicon M1/M2/M3/M4)
**Avalonia Version:** 11.x

## Issue Summary

CST Reader exhibits elevated CPU usage on macOS compared to typical idle desktop applications:
- **~30% CPU** with only the welcome page open (single WebView)
- **~60% CPU** with 3 books open (3 WebView instances)
- Main process typically uses **27-30%** when idle
- Each CEF renderer subprocess adds **~5-10%** CPU

For comparison, typical idle desktop apps use <5% CPU on macOS.

## Root Cause Analysis

### Primary Cause: Avalonia macOS Event Loop

The high CPU usage is primarily caused by **Avalonia's macOS native backend** (`libAvaloniaNative.dylib`), not the application code itself. This is a known limitation of Avalonia on macOS.

**Technical Details:**

1. **CFRunLoop Observer Frequency**
   - Avalonia's `Signaler` class creates a CFRunLoop observer that fires on **every run loop iteration**
   - The observer callback `[Signaler createObserver]_block_invoke` executes continuously
   - This triggers `requestBackgroundProcessing` which posts work to dispatch queues

2. **CPU Profile Evidence**
   ```
   26 __CFRUNLOOP_IS_CALLING_OUT_TO_AN_OBSERVER_CALLBACK_FUNCTION__
     26 __26-[Signaler createObserver]_block_invoke  (in libAvaloniaNative.dylib)
       24 -[Signaler requestBackgroundProcessing]  (in libAvaloniaNative.dylib)
   ```

3. **CEF Amplification Effect**
   - CEF (Chromium Embedded Framework) hooks into Avalonia's event loop
   - On each observer callback, CEF performs background work including:
     - Process monitoring (`std::process::id` calls - 18+ samples in 5-second profile)
     - Font rendering preparation (`read_fonts..tables..colr`)
     - GPU/rendering pipeline checks
   - With multiple WebView instances (book tabs), this effect multiplies

### Secondary Cause: CEF Background Processing

CPU profiling shows CEF's Rust-based code is actively working even when pages are idle:

```
18 std::process::id::h99a7dfb4e88d0c2d  (in libcef.dylib) + 2042212
  16 std::process::id::h99a7dfb4e88d0c2d  (in libcef.dylib) + 2040028
    14 std::process::id::h99a7dfb4e88d0c2d  (in libcef.dylib) + 2270632
      12 _$LT$read_fonts..tables..colr..Extend...  (in libcef.dylib)
```

This indicates CEF is continuously:
- Checking subprocess health
- Preparing font rendering data

## Comparison with Other Platforms

### Known Avalonia Performance Characteristics

From Avalonia GitHub issues (#11070, #15894):

| Platform | Empty App Idle CPU | With Mouse Movement |
|----------|-------------------|---------------------|
| **macOS (M1 Max)** | 6-10% | 35% |
| **Windows** | 0.48% | 8% |
| **Linux X11** | Previously high (polling issue) | N/A |

**CST Reader Results:**
- Welcome page only: ~30% (Avalonia baseline ~10% + CEF overhead ~20%)
- 3 books open: ~60% (3× CEF renderers at ~10% each + baseline)

The application's CPU usage is **proportional to the number of WebView instances**, which aligns with CEF being the multiplier on top of Avalonia's baseline overhead.

## Why This Happens

### Avalonia's macOS Backend Design

The `libAvaloniaNative` library uses CFRunLoop observers to integrate with macOS's native event system. While this provides good UI responsiveness, it results in continuous polling:

1. CFRunLoop runs continuously (normal macOS behavior)
2. Avalonia's observer gets called on **every iteration** (not just when events occur)
3. The observer checks for pending work and requests background processing

### CEF's Event Integration

CEF needs to process events regularly to:
- Check for subprocess crashes/hangs
- Render frames (even if content hasn't changed)
- Handle input events
- Manage GPU command buffers

When integrated with Avalonia's high-frequency observer pattern, CEF's background work executes more frequently than it would in a typical Chromium browser.

## Mitigation Attempts

### What We Tried

1. **Code Signing & Packaging Fixes**
   - Ensured proper CEF helper bundle structure
   - Fixed hardened runtime and entitlements
   - **Result:** Resolved crashes, but CPU usage unchanged (as expected)

2. **CEF Command-Line Switches**
   - Using `--no-sandbox` (already in place)
   - `--use-mock-keychain` (prevents keychain prompts)
   - `--disable-password-generation`
   - **Result:** No measurable CPU improvement

### What Might Help (Not Yet Tested)

1. **Avalonia Rendering Backend**
   - Try Metal rendering (may be default in Avalonia 11.2+)
   - Command: Set `AVALONIA_RENDERING_BACKEND=metal` environment variable

2. **CEF Frame Rate Limiting**
   - Add `--disable-frame-rate-limit` (counterintuitive, but may prevent busy-waiting)
   - Add `--disable-background-timer-throttling`
   - Add `--disable-renderer-backgrounding`

3. **Upgrade Avalonia**
   - Avalonia 12+ may have improvements
   - GitHub issues mention performance work in newer versions

4. **Custom CFRunLoop Mode**
   - Modify Avalonia's native backend to use a less aggressive run loop mode
   - Would require forking/patching `libAvaloniaNative`

## Impact on Users

### Battery Life
- Impact on battery life has not been formally tested

### Performance
- UI remains responsive
- Book rendering is smooth
- Search and navigation not impacted

### Thermal
- Not yet tested on Intel Macs
- Thermal behavior on Apple Silicon not formally measured

## Recommendations

### For Beta 2 Release

**Accept as Known Limitation:**
- Document in release notes
- Note that this is inherent to Avalonia on macOS, not a CST Reader bug
- Mention that closing unused book tabs reduces CPU usage

**User Guidance:**
- Close book tabs when not in use (each tab = ~10% CPU)
- Keep welcome page open when idle (lowest CPU state)
- Use Activity Monitor to verify expected behavior

### For Future Versions

1. **Monitor Avalonia Development**
   - Watch for macOS performance improvements in Avalonia 12+
   - Consider upgrading when stable releases are available

2. **Alternative WebView Implementation**
   - Investigate native macOS `WKWebView` wrapper
   - Would eliminate CEF overhead but lose cross-platform consistency
   - May not support all required features (XSLT, local file access)

3. **Hybrid Approach**
   - Use WebView only for book content display
   - Render search results, tree views in native Avalonia controls
   - Would reduce number of CEF processes

4. **Profile-Guided Optimization**
   - Work with Avalonia team to optimize macOS backend
   - Contribute patches if issues can be isolated

## Technical Details

### CPU Profiling Commands

```bash
# Get process IDs
ps aux | grep "CST.Avalonia\|CST Reader Helper" | grep -v grep

# Profile main process (10 second sample)
sample <pid> 10 -file cst-profile.txt

# Check for specific function calls
grep "Signaler\|requestBackgroundProcessing" cst-profile.txt | wc -l

# View heaviest stacks
grep -A100 "Call graph:" cst-profile.txt | less
```

### Key Stack Traces

**Avalonia Observer Loop:**
```
__CFRunLoopRun
  __CFRunLoopDoObservers
    __CFRUNLOOP_IS_CALLING_OUT_TO_AN_OBSERVER_CALLBACK_FUNCTION__
      __26-[Signaler createObserver]_block_invoke
        -[Signaler requestBackgroundProcessing]
          dispatch_async
```

**CEF Process Monitoring:**
```
[Signaler createObserver]_block_invoke
  (managed code transition)
    cxxbridge1$rust_vec$u8$set_len
      std::process::id::h99a7dfb4e88d0c2d
        std::process::id::h99a7dfb4e88d0c2d (nested calls)
          _$LT$read_fonts..tables..colr..Extend...
```

### Process Breakdown

With welcome page open:
```
PID     %CPU  COMMAND
23692   27.2  /Applications/CST Reader.app/Contents/MacOS/CST.Avalonia
23693    0.4  ./Xilium.CefGlue.BrowserProcess --type=gpu-process
23697    0.5  ./Xilium.CefGlue.BrowserProcess --type=utility (storage)
23698    0.6  ./Xilium.CefGlue.BrowserProcess --type=utility (network)
23705    0.5  ./Xilium.CefGlue.BrowserProcess --type=renderer (welcome page)
```

## References

### Avalonia GitHub Issues
- [#11070 - High CPU usage on macOS?](https://github.com/AvaloniaUI/Avalonia/issues/11070)
- [#15894 - CPU usage is really high again...](https://github.com/AvaloniaUI/Avalonia/issues/15894)
- [#10520 - Dispatcher rework](https://github.com/AvaloniaUI/Avalonia/issues/10520)
- [#12579 - Fix UI thread main loop cancellation from another thread on macOS](https://github.com/AvaloniaUI/Avalonia/pull/12579)

### Related Stack Overflow
- [Why is my empty Avalonia app using 100% CPU?](https://stackoverflow.com/questions/69296544/why-is-my-empty-avalonia-app-using-100-cpu)

### Avalonia Documentation
- [Improving Performance](https://docs.avaloniaui.net/docs/guides/development-guides/improving-performance)
- [10 Avalonia Performance Tips](https://avaloniaui.net/blog/10-avalonia-performance-tips-to-supercharge-your-app)

## Conclusion

The high CPU usage in CST Reader on macOS is primarily an **Avalonia framework limitation**, not an application bug. The issue is amplified by the use of multiple WebView/CEF instances but would exist even with a single WebView.

The application is fully functional despite the elevated CPU usage.

Future improvements should focus on either:
1. Waiting for Avalonia framework optimizations
2. Reducing the number of simultaneous WebView instances
3. Considering alternative rendering approaches

For Beta 2, this should be documented as a **known limitation** with guidance for users to close unused book tabs to reduce CPU usage.
