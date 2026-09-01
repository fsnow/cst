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
/// <para><b>Minimum macOS, from the SDK headers rather than memory.</b> The four functions are
/// <c>API_AVAILABLE(macos(10.6))</c>, but the binding constraint is <c>kSecClassGenericPassword</c> at
/// <b>10.7</b> — so this code needs 10.7, comfortably under the app's 10.15 floor. Worth stating because the
/// gap is one release wide and easy to mis-remember as 10.6 throughout.</para>
///
/// <para><b>This targets the FILE-BASED (login) keychain, deliberately.</b> <c>SecItem</c> routes to the
/// data-protection keychain only when the query carries <c>kSecUseDataProtectionKeychain</c> or
/// <c>kSecAttrSynchronizable</c>; with neither, it talks to the file-based one
/// (Apple, TN3137 / DTS "On Mac Keychains"). The data-protection keychain "is only available to code that can
/// carry an entitlement", and its access groups are determined by code signing, so a plain <c>dotnet run</c>
/// build could not reach a key the signed <c>.app</c> stored. What the file-based keychain buys is that a
/// development build can store and read <b>its own</b> keys without a signed bundle. Apple says the file-based
/// implementation is on the road to deprecation; revisiting is #609.</para>
///
/// <para><b>It does NOT let a development build and a signed build share keys.</b> This doc used to say so, and
/// it was the stated reason for the choice. The file-based keychain gates on a per-item ACL, and an ACL records
/// the binary that created the item — <c>apphost</c>, ad-hoc signed, no team for a <c>dotnet run</c> build
/// against <c>com.cst.avalonia</c>, Developer ID for the installed app. Each is foreign to the other. The
/// difference from the data-protection keychain is not that sharing works; it is that the failure arrives as a
/// modal password dialog rather than a clean denial, one per read. Installing a signed build over a machine
/// that had been running development builds produced eight such dialogs at launch (#609, #925).</para>
///
/// <para><b>Hence no <c>kSecAttrAccessible</c>.</b> Accessibility classes are the data-protection keychain's
/// access model; the file-based keychain uses ACLs instead, and the attribute is accepted and ignored there.
/// Passing it would read like a protection this code does not actually provide.</para>
///
/// <para><b>Nothing here logs.</b> Not the value, not a prefix, not a length — the caller reports outcomes.</para>
/// </summary>
internal static class MacOsKeychain
{
    private const string SecurityFramework =
        "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundation =
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    internal const int ErrSecSuccess = 0;
    internal const int ErrSecItemNotFound = -25300;
    private const int ErrSecDuplicateItem = -25299;

    // The three ways a READ can fail while the item is sitting right there. Distinguished because the remedy
    // differs from "no key stored": the reader authorizes once, or unlocks, and the same key works. Collapsing
    // them into not-found is #926 — it told the maintainer to re-enter keys he already had.
    //
    // internal so the tests can both name them in Classify's cases and pin their VALUES against SecBase.h.
    // Naming them alone would not catch a wrong number - the test and the code would move together - and a
    // wrong number here is silent: the status never matches, so the read falls to the default and a genuinely
    // absent key starts reporting itself unreadable.
    internal const int ErrSecAuthFailed = -25293;              // authorization declined
    internal const int ErrSecUserCanceled = -128;              // the reader dismissed the prompt
    internal const int ErrSecInteractionNotAllowed = -25308;   // locked, and no UI available to ask

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
        IntPtr True);

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
            Deref(cf, "kCFBooleanTrue"));

        // Any missing symbol means the assumptions here no longer hold; report unavailable rather than build a
        // half-populated query that would fail obscurely at every call site.
        foreach (var value in new[]
                 {
                     constants.Class, constants.GenericPassword, constants.Service, constants.Account,
                     constants.ValueData, constants.ReturnData, constants.MatchLimit, constants.MatchLimitOne,
                     constants.True,
                 })
        {
            if (value == IntPtr.Zero) return null;
        }

        return constants;
    }

    internal static bool IsAvailable => OperatingSystem.IsMacOS() && Symbols.Value is not null;

    /// <summary>
    /// What an unsuccessful <c>SecItemCopyMatching</c> status means for the reader. (#926)
    ///
    /// <para><b>Separated from <see cref="Find"/> so it can be tested at all.</b> Everything around it is a
    /// P/Invoke into Security.framework, which no unit test can drive — and this mapping is the entire defect
    /// #926 fixes, so leaving it inside the native call would leave the one line that matters unguarded. A
    /// mutation flipping <c>errSecAuthFailed</c> back to "not stored" killed no test until this moved out.</para>
    ///
    /// <para><b>Unrecognised statuses are Unreadable, not NotStored.</b> An unanticipated failure is not
    /// evidence that an item is absent, and claiming absence we cannot see is precisely the harm here: it is
    /// what tells a reader to re-enter a key that is present and correct. The conservative direction is to
    /// under-claim.</para>
    /// </summary>
    internal static CredentialState Classify(int status) => status switch
    {
        ErrSecSuccess => CredentialState.Found,
        ErrSecItemNotFound => CredentialState.NotStored,
        _ => CredentialState.Unreadable,
    };

    // ---- Operations ----------------------------------------------------------------------------------------

    /// <summary>
    /// One stored secret, and what happened when we asked for it. (#926)
    ///
    /// <para><b>Every status is classified, not just success.</b> This is the one operation of the three here
    /// whose answer reaches the reader, and it used to be the only one that did not discriminate — while
    /// <see cref="Save"/> acts on <c>errSecDuplicateItem</c> and <see cref="Delete"/> accepts
    /// <c>errSecItemNotFound</c>. An item whose ACL names a different binary — the ordinary case after a
    /// development build and a signed build have both written keys — answers <c>errSecAuthFailed</c>, which
    /// read as "no key stored" and sent the reader off to re-enter one.</para>
    /// </summary>
    internal static CredentialRead Find(string service, string account)
    {
        if (Symbols.Value is not { } k) return CredentialRead.Unavailable;

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
            if (query == IntPtr.Zero) return CredentialRead.Unavailable;

            var status = SecItemCopyMatching(query, out result);
            if (status != ErrSecSuccess) return new CredentialRead(Classify(status), null);

            // Success with nothing to copy. Not an item that is missing — one whose value we failed to
            // receive, which is the same class of "cannot read it" as a declined authorization.
            if (result == IntPtr.Zero) return CredentialRead.Unreadable;

            var length = CFDataGetLength(result);
            if (length <= 0) return CredentialRead.Unreadable;

            var bytes = new byte[length];
            Marshal.Copy(CFDataGetBytePtr(result), bytes, 0, (int)length);
            return CredentialRead.Found(Encoding.UTF8.GetString(bytes));
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
                new[] { k.Class, k.Service, k.Account, k.ValueData },
                new[] { k.GenericPassword, serviceRef, accountRef, data });
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
