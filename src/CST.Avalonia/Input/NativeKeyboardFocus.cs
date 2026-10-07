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
/// <para>[observed] Windows has the same gap: the browser is a child HWND that keeps the Win32 keyboard focus.
/// [fsnow], testing beta 8 on Merlin: "Find in Page requires an extra click to get focus on Windows." There the
/// top-level window takes it back with <c>SetFocus</c>. [observed] Merlin, 2026-10-07: the focus was held by CEF's
/// <c>Chrome_WidgetWin_0</c>. [fsnow], testing this there: "That change looks good." Ctrl+O and Ctrl+Shift+F go
/// through here too, but were only tried with it in place, so whether Windows ever had their gap is unknown.</para>
/// </summary>
public static class NativeKeyboardFocus
{
    /// <summary>
    /// Give <paramref name="visual"/>'s top-level window the native keyboard focus: its view becomes first
    /// responder on macOS, its HWND takes the Win32 focus on Windows. A no-op elsewhere.
    /// </summary>
    public static void TakeFromEmbeddedBrowser(Visual visual)
    {
        if (OperatingSystem.IsWindows())
        {
            TakeFromEmbeddedBrowserWindows(visual);
            return;
        }

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

    private static void TakeFromEmbeddedBrowserWindows(Visual visual)
    {
        try
        {
            var hwnd = TopLevel.GetTopLevel(visual)?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (hwnd == IntPtr.Zero) return;

            var holder = GetFocus();
            if (holder == hwnd) return;

            if (SetFocus(hwnd) == IntPtr.Zero && Marshal.GetLastWin32Error() != 0)
            {
                Log.ForContext(typeof(NativeKeyboardFocus))
                    .Warning("SetFocus refused (error {Error}); keys stay with {Holder}",
                        Marshal.GetLastWin32Error(), ClassNameOf(holder));
                return;
            }

            Log.ForContext(typeof(NativeKeyboardFocus))
                .Debug("Took the keyboard from {Holder}", ClassNameOf(holder));
        }
        catch (Exception ex)
        {
            Log.ForContext(typeof(NativeKeyboardFocus)).Warning(ex, "Could not take keyboard focus from the browser");
        }
    }

    private static string ClassNameOf(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return "(none)";
        var buffer = new System.Text.StringBuilder(256);
        return GetClassName(hwnd, buffer, buffer.Capacity) > 0 ? buffer.ToString() : "(unknown)";
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetFocus();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern IntPtr sel_registerName(string name);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool objc_msgSend_bool(IntPtr receiver, IntPtr selector, IntPtr arg);
}
