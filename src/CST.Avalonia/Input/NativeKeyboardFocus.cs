using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Serilog;

namespace CST.Avalonia.Input;

/// <summary>
/// Takes the operating system's keyboard focus back from an embedded browser, so a control focused in Avalonia
/// actually receives the keys typed next.
///
/// <para>[observed] A book's CEF browser is a native child view. While the reader is in the text, that view is the
/// window's first responder (macOS), so every keystroke goes to Chromium. <c>Control.Focus()</c> moves only
/// Avalonia's own focus; it does not change the first responder. A shortcut handled from inside the browser (⌘F
/// for Find in Page) could therefore show the find box focused while the next keystrokes still went to the book.
/// [fsnow], testing beta 8: "⌘F for Find in Page does not put the focus into the text box. I can't just ⌘F and
/// start typing." [observed] A click does move native focus (AppKit makes the clicked view first responder), which
/// is why clicking into the box and then typing was never affected.</para>
///
/// <para>macOS only. On Windows the same gap is plausible (the browser is a child HWND) but unverified.</para>
/// </summary>
public static class NativeKeyboardFocus
{
    /// <summary>Make <paramref name="visual"/>'s window view the native first responder. A no-op elsewhere.</summary>
    public static void TakeFromEmbeddedBrowser(Visual visual)
    {
        if (!OperatingSystem.IsMacOS()) return;
        if (TopLevel.GetTopLevel(visual)?.TryGetPlatformHandle() is not IMacOSTopLevelPlatformHandle handle) return;

        try
        {
            // Read inside the try: NSWindow and NSView are live calls into the native window.
            var window = handle.NSWindow;
            var view = handle.NSView;
            if (window == IntPtr.Zero || view == IntPtr.Zero) return;

            if (!objc_msgSend_bool(window, sel_registerName("makeFirstResponder:"), view))
                Log.ForContext(typeof(NativeKeyboardFocus))
                    .Warning("The window refused to make its view first responder; keys stay with the browser");
        }
        catch (Exception ex)
        {
            Log.ForContext(typeof(NativeKeyboardFocus)).Warning(ex, "Could not take keyboard focus from the browser");
        }
    }

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr sel_registerName(string name);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool objc_msgSend_bool(IntPtr receiver, IntPtr selector, IntPtr arg);
}
