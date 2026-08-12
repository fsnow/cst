using System;
using System.Runtime.InteropServices;
using System.Text;

namespace CST.Avalonia.Services.Ai.Credentials;

/// <summary>
/// Generic-password items in the macOS Keychain, via Security.framework. (#579)
///
/// <para><b>The modern <c>SecItem*</c> API, not the far simpler <c>SecKeychain*</c> one.</b> The legacy calls
/// would be a third of this code, but they have been deprecated since 10.10; a credential store is exactly the
/// component that must not stop working on an OS release, and this one has to outlive several.</para>
///
/// <para><b>The <c>kSec*</c> keys are read with <c>dlsym</c> rather than hardcoded.</b> They are
/// <c>CFStringRef</c> globals whose underlying string values ("svce", "acct", "v_Data") are stable in practice
/// and undocumented in principle. Loading the real symbols costs a few lines and removes a dependency on
/// something Apple never promised.</para>
///
/// <para><b>Nothing here logs.</b> Not the value, not a prefix, not a length — the caller reports outcomes.</para>
/// </summary>
internal static class MacOsKeychain
{
    private const string SecurityFramework =
        "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundation =
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    private const int ErrSecSuccess = 0;
    private const int ErrSecItemNotFound = -25300;
    private const int ErrSecDuplicateItem = -25299;

    // ---- CoreFoundation ------------------------------------------------------------------------------------

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr cf);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFStringCreateWithBytes(
        IntPtr alloc, byte[] bytes, long numBytes, uint encoding, [MarshalAs(UnmanagedType.I1)] bool isExternal);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFDataCreate(IntPtr alloc, byte[] bytes, long length);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFDataGetBytePtr(IntPtr data);

    [DllImport(CoreFoundation)]
    private static extern long CFDataGetLength(IntPtr data);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFDictionaryCreate(
        IntPtr alloc, IntPtr[] keys, IntPtr[] values, long numValues,
        IntPtr keyCallBacks, IntPtr valueCallBacks);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFBooleanGetTypeID();   // forces the library to load before dlopen

    private const uint KCFStringEncodingUtf8 = 0x08000100;

    // ---- Security ------------------------------------------------------------------------------------------

    [DllImport(SecurityFramework)]
    private static extern int SecItemAdd(IntPtr attributes, IntPtr result);

    [DllImport(SecurityFramework)]
    private static extern int SecItemCopyMatching(IntPtr query, out IntPtr result);

    [DllImport(SecurityFramework)]
    private static extern int SecItemUpdate(IntPtr query, IntPtr attributesToUpdate);

    [DllImport(SecurityFramework)]
    private static extern int SecItemDelete(IntPtr query);

    // ---- Constant lookup -----------------------------------------------------------------------------------

    [DllImport("libdl")]
    private static extern IntPtr dlopen(string path, int mode);

    [DllImport("libdl")]
    private static extern IntPtr dlsym(IntPtr handle, string symbol);

    private const int RtldNow = 2;

    private static readonly Lazy<Constants?> Symbols = new(LoadConstants);

    private sealed record Constants(
        IntPtr Class, IntPtr GenericPassword, IntPtr Service, IntPtr Account,
        IntPtr ValueData, IntPtr ReturnData, IntPtr MatchLimit, IntPtr MatchLimitOne,
        IntPtr Accessible, IntPtr AfterFirstUnlock, IntPtr True);

    private static Constants? LoadConstants()
    {
        if (!OperatingSystem.IsMacOS()) return null;

        var security = dlopen(SecurityFramework, RtldNow);
        var cf = dlopen(CoreFoundation, RtldNow);
        if (security == IntPtr.Zero || cf == IntPtr.Zero) return null;

        // Each symbol is a POINTER TO a CFStringRef, so it needs one dereference. Reading the symbol address
        // itself would hand SecItem* a pointer to a pointer and every call would fail with a type error.
        IntPtr Deref(IntPtr handle, string symbol)
        {
            var address = dlsym(handle, symbol);
            return address == IntPtr.Zero ? IntPtr.Zero : Marshal.ReadIntPtr(address);
        }

        var constants = new Constants(
            Deref(security, "kSecClass"),
            Deref(security, "kSecClassGenericPassword"),
            Deref(security, "kSecAttrService"),
            Deref(security, "kSecAttrAccount"),
            Deref(security, "kSecValueData"),
            Deref(security, "kSecReturnData"),
            Deref(security, "kSecMatchLimit"),
            Deref(security, "kSecMatchLimitOne"),
            Deref(security, "kSecAttrAccessible"),
            // Not ...WhenUnlocked: surface B can be invoked from a session that is unlocked but whose keychain
            // state we do not control, and AfterFirstUnlock is the weakest accessibility that still keeps the
            // key off a locked, powered-down machine.
            Deref(security, "kSecAttrAccessibleAfterFirstUnlock"),
            Deref(cf, "kCFBooleanTrue"));

        // Any missing symbol means the assumptions here no longer hold; report unavailable rather than build a
        // half-populated query that would fail obscurely at every call site.
        foreach (var value in new[]
                 {
                     constants.Class, constants.GenericPassword, constants.Service, constants.Account,
                     constants.ValueData, constants.ReturnData, constants.MatchLimit, constants.MatchLimitOne,
                     constants.Accessible, constants.AfterFirstUnlock, constants.True,
                 })
        {
            if (value == IntPtr.Zero) return null;
        }

        return constants;
    }

    internal static bool IsAvailable => OperatingSystem.IsMacOS() && Symbols.Value is not null;

    // ---- Operations ----------------------------------------------------------------------------------------

    /// <summary>The stored secret, or null when there is no item (or the platform is unavailable).</summary>
    internal static string? Find(string service, string account)
    {
        if (Symbols.Value is not { } k) return null;

        var query = IntPtr.Zero;
        var result = IntPtr.Zero;
        var strings = new StringHandles();
        try
        {
            query = CreateDictionary(
                new[] { k.Class, k.Service, k.Account, k.ReturnData, k.MatchLimit },
                new[]
                {
                    k.GenericPassword,
                    strings.Add(service),
                    strings.Add(account),
                    k.True,
                    k.MatchLimitOne,
                });
            if (query == IntPtr.Zero) return null;

            var status = SecItemCopyMatching(query, out result);
            if (status != ErrSecSuccess || result == IntPtr.Zero) return null;

            var length = CFDataGetLength(result);
            if (length <= 0) return null;

            var bytes = new byte[length];
            Marshal.Copy(CFDataGetBytePtr(result), bytes, 0, (int)length);
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            if (result != IntPtr.Zero) CFRelease(result);
            if (query != IntPtr.Zero) CFRelease(query);
            strings.Dispose();
        }
    }

    /// <summary>Store or replace a secret. Returns false when the platform cannot.</summary>
    internal static bool Save(string service, string account, string secret)
    {
        if (Symbols.Value is not { } k) return false;

        var attributes = IntPtr.Zero;
        var query = IntPtr.Zero;
        var update = IntPtr.Zero;
        var strings = new StringHandles();
        var data = IntPtr.Zero;
        try
        {
            var secretBytes = Encoding.UTF8.GetBytes(secret);
            data = CFDataCreate(IntPtr.Zero, secretBytes, secretBytes.Length);
            if (data == IntPtr.Zero) return false;

            var serviceRef = strings.Add(service);
            var accountRef = strings.Add(account);

            attributes = CreateDictionary(
                new[] { k.Class, k.Service, k.Account, k.ValueData, k.Accessible },
                new[] { k.GenericPassword, serviceRef, accountRef, data, k.AfterFirstUnlock });
            if (attributes == IntPtr.Zero) return false;

            var status = SecItemAdd(attributes, IntPtr.Zero);
            if (status == ErrSecSuccess) return true;
            if (status != ErrSecDuplicateItem) return false;

            // Already present: SecItemAdd will not overwrite, so update in place. Delete-then-add would leave a
            // window with no key at all if the add failed.
            query = CreateDictionary(
                new[] { k.Class, k.Service, k.Account },
                new[] { k.GenericPassword, serviceRef, accountRef });
            update = CreateDictionary(new[] { k.ValueData }, new[] { data });
            if (query == IntPtr.Zero || update == IntPtr.Zero) return false;

            return SecItemUpdate(query, update) == ErrSecSuccess;
        }
        finally
        {
            if (update != IntPtr.Zero) CFRelease(update);
            if (query != IntPtr.Zero) CFRelease(query);
            if (attributes != IntPtr.Zero) CFRelease(attributes);
            if (data != IntPtr.Zero) CFRelease(data);
            strings.Dispose();
        }
    }

    /// <summary>Remove a secret. Removing one that is not there counts as success.</summary>
    internal static bool Delete(string service, string account)
    {
        if (Symbols.Value is not { } k) return false;

        var query = IntPtr.Zero;
        var strings = new StringHandles();
        try
        {
            query = CreateDictionary(
                new[] { k.Class, k.Service, k.Account },
                new[] { k.GenericPassword, strings.Add(service), strings.Add(account) });
            if (query == IntPtr.Zero) return false;

            var status = SecItemDelete(query);
            return status is ErrSecSuccess or ErrSecItemNotFound;
        }
        finally
        {
            if (query != IntPtr.Zero) CFRelease(query);
            strings.Dispose();
        }
    }

    // ---- Helpers -------------------------------------------------------------------------------------------

    /// <summary>
    /// A CFDictionary with the standard callbacks. Passing null callbacks means CoreFoundation neither retains
    /// nor releases the members, so ownership stays with the caller — which is what the explicit
    /// <c>CFRelease</c>s in every <c>finally</c> above are managing.
    /// </summary>
    private static IntPtr CreateDictionary(IntPtr[] keys, IntPtr[] values)
    {
        foreach (var value in values)
            if (value == IntPtr.Zero)
                return IntPtr.Zero;

        return CFDictionaryCreate(IntPtr.Zero, keys, values, keys.Length, IntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>Tracks the CFStrings a call creates so every one of them is released exactly once.</summary>
    private sealed class StringHandles : IDisposable
    {
        private readonly System.Collections.Generic.List<IntPtr> _handles = new();

        internal IntPtr Add(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            var handle = CFStringCreateWithBytes(
                IntPtr.Zero, bytes, bytes.Length, KCFStringEncodingUtf8, false);
            if (handle != IntPtr.Zero) _handles.Add(handle);
            return handle;
        }

        public void Dispose()
        {
            foreach (var handle in _handles) CFRelease(handle);
            _handles.Clear();
        }
    }
}
