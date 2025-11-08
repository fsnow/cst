# CST Reader: Strategic Technology Options Analysis

**Date:** November 7, 2025
**Context:** Reevaluating technology stack due to CEF/WebView issues
**Constraint:** Must use only FREE/Open Source components
**Goal:** Find optimal cross-platform solution that leverages existing C# assets

---

## Executive Summary

You have **valuable C# assets:**
- Mature script converters (14 Pali scripts)
- Working Lucene.NET 4.8 search implementation
- TEI XML processing code

You chose Avalonia for cross-platform .NET desktop UI, but hit a wall with CEF/WebView rendering and ControlRecycling crashes.

**Key Question:** Should you stay with Avalonia (and solve the rendering problem), or switch to a different free technology stack?

This document analyzes **6 viable options**, all free/open-source, that can use your C# code.

---

## Option Comparison Matrix

| Option | Linux Desktop | Windows | macOS | Free | C# Reuse | WebView Quality | Maturity | Dev Effort |
|--------|--------------|---------|-------|------|----------|-----------------|----------|------------|
| **Avalonia + Custom Rendering** | ✅ Yes | ✅ Yes | ✅ Yes | ✅ | 100% | N/A (native) | ✅ High | ⚠️ 3-6 months |
| **Electron.NET** | ✅ Yes | ✅ Yes | ✅ Yes | ✅ | 100% backend | ✅ Excellent | ✅ High | ⚠️ 2-3 months |
| **.NET MAUI + Blazor Hybrid** | ❌ No | ✅ Yes | ✅ Yes | ✅ | 100% | ✅ Good | ⚠️ Medium | ✅ 1-2 months |
| **Blazor Server (Web App)** | ✅ Yes* | ✅ Yes* | ✅ Yes* | ✅ | 100% | ✅ Excellent | ✅ High | ✅ 1-2 months |
| **WPF + WebView2** | ❌ No | ✅ Yes | ❌ No | ✅ | 100% | ✅ Excellent | ✅ Very High | ✅ 2-4 weeks |
| **Tauri + C# Backend** | ✅ Yes | ✅ Yes | ✅ Yes | ✅ | 90% backend | ✅ Excellent | ⚠️ Medium | ⚠️ 2-4 months |

\* Blazor Server: Any browser, but requires server

---

## Option 1: Stay with Avalonia + Custom Native Rendering

### Architecture
```
Avalonia Desktop App (Native Controls)
├─ Dock.Avalonia (keep existing)
├─ BookRenderControl (custom, replace WebView)
│   └─ Direct rendering with Avalonia TextLayout
├─ SearchViewModel (keep, Lucene.NET)
└─ Script Converters (keep)
```

### What Changes
- **Remove:** CefGlue WebView, all HTML/XSL/JavaScript
- **Build:** Custom text rendering control (3-6 months)
- **Keep:** Everything else (90% of codebase)

### Pros
- ✅ **Stay cross-platform** (Windows/macOS/Linux)
- ✅ **Keep all existing code** except rendering layer
- ✅ **Smallest app size** (~20 MB)
- ✅ **Best performance** (native rendering)
- ✅ **Complete control** over features
- ✅ **Solves ControlRecycling forever**

### Cons
- ❌ **3-6 months development** (text selection is complex)
- ❌ **High technical risk** (complex scripts, RTL, ligatures)
- ❌ **Ongoing maintenance** complexity

### Development Roadmap
See [ALTERNATIVE_RENDERING_ENGINES.md](ALTERNATIVE_RENDERING_ENGINES.md) - Phase 2 implementation plan.

### Recommendation
**Best long-term solution if you're committed to Avalonia.** High initial investment pays off with complete control and best performance.

---

## Option 2: Electron.NET

### Architecture
```
Electron.NET Desktop App
├─ Electron Window (HTML/CSS/JS UI)
│   ├─ Book rendering (HTML from XSL)
│   ├─ Tab UI (HTML/CSS)
│   └─ Search UI (HTML/CSS)
└─ ASP.NET Core Backend (C#)
    ├─ Lucene.NET search
    ├─ Script converters
    ├─ XML → HTML transformation
    └─ API for frontend
```

### What Changes
- **Replace:** Avalonia XAML → HTML/CSS/JavaScript
- **Replace:** Dock.Avalonia → Custom HTML tabs/docking (or use library like Golden Layout)
- **Keep:** All C# backend code (Lucene.NET, script converters, XML processing)
- **Add:** Frontend-backend API layer

### Pros
- ✅ **Fully cross-platform** (Windows/macOS/Linux)
- ✅ **Keep all C# backend** (Lucene.NET, script converters)
- ✅ **WebView just works** (Chromium built-in)
- ✅ **Rich HTML/CSS ecosystem** (lots of UI libraries)
- ✅ **Actively maintained** (ElectronNET.Core modernized in 2025)
- ✅ **No rendering complexity** (use existing HTML/XSL)

### Cons
- ❌ **Rewrite entire UI** (XAML → HTML/CSS/JS)
- ❌ **Large app size** (~150-200 MB, similar to CEF)
- ❌ **Frontend-backend split** (API layer needed)
- ❌ **Lose Avalonia benefits** (native controls, data binding)

### Comparison to Current Approach
- **Similar:** Both use Chromium, both have HTML rendering
- **Different:** Electron.NET is designed for this architecture, CEF in Avalonia is a hack

### Development Effort
- **Weeks 1-2:** Setup Electron.NET, create project structure
- **Weeks 3-4:** Backend API (expose Lucene.NET, script converters via REST/SignalR)
- **Weeks 5-6:** HTML/CSS UI for book display
- **Weeks 7-8:** Search UI, tabs, docking
- **Weeks 9-10:** Integration, testing

**Total: 2-3 months**

### When to Choose This
- If you're okay with HTML/CSS/JS frontend
- If you value "WebView just works" over native feel
- If you want rich UI library ecosystem

### Example Projects
- Visual Studio Code (Electron)
- Many Electron apps with .NET backends via IPC/REST

---

## Option 3: .NET MAUI + Blazor Hybrid

### Architecture
```
.NET MAUI Desktop App
├─ BlazorWebView (HTML/CSS rendering)
│   └─ Blazor Components (C#!)
│       ├─ Book display
│       ├─ Search UI
│       └─ Tab management
└─ C# Backend (same process)
    ├─ Lucene.NET
    ├─ Script converters
    └─ XML processing
```

### What Changes
- **Replace:** Avalonia → MAUI
- **Replace:** XAML → Blazor components (still C#, not JavaScript!)
- **Keep:** All backend C# code

### Pros
- ✅ **Write UI in C#** (Blazor components, not JavaScript)
- ✅ **WebView built-in** (WebView2 on Windows, WKWebView on Mac)
- ✅ **Microsoft official** (long-term support)
- ✅ **Share UI code** with potential future web version
- ✅ **Keep all backend code**
- ✅ **Native controls available** (can mix Blazor + MAUI controls)

### Cons
- ❌ **NO LINUX DESKTOP** (Windows + macOS only)
- ❌ **MAUI maturity concerns** (still evolving, bugs)
- ❌ **No AvalonDock equivalent** (would need to build or use HTML-based docking)
- ⚠️ **Learning curve** (Blazor component model)

### Critical Blocker: Linux Support
MAUI officially supports:
- ✅ Windows 10+ (WinUI 3)
- ✅ macOS 10.13+ (Mac Catalyst)
- ✅ Android, iOS
- ❌ **Linux desktop NOT supported**

Microsoft has stated: "Linux support is not planned from Microsoft."

### When to Choose This
- **If you can drop Linux support**
- If you want to write UI in C# instead of JavaScript
- If you want Microsoft's official cross-platform framework

### Development Effort
- **Weeks 1-2:** Setup MAUI project, BlazorWebView
- **Weeks 3-4:** Blazor components for book display
- **Weeks 5-6:** Search UI, navigation
- **Weeks 7-8:** Tab management, testing

**Total: 1-2 months**

---

## Option 4: Blazor Server (Pure Web App)

### Architecture
```
User's Browser (any platform)
    ↓ (SignalR)
ASP.NET Core Server
├─ Blazor Server App (C# UI)
│   ├─ Book display components
│   ├─ Search UI
│   └─ Tab management
├─ Lucene.NET search
├─ Script converters
└─ XML processing
```

### What It Is
A **web application** that users access via browser. UI is written in C# (Blazor), runs on server, updates browser via SignalR.

### What Changes
- **Replace:** Desktop app → Web app
- **Replace:** Avalonia XAML → Blazor components (C#)
- **Keep:** All backend code
- **Add:** Server hosting requirement

### Pros
- ✅ **True cross-platform** (any browser, any OS)
- ✅ **Write UI in C#** (not JavaScript)
- ✅ **No installation** (just browse to URL)
- ✅ **Easy updates** (server-side only)
- ✅ **Keep all backend code**
- ✅ **WebView not needed** (browser IS the view)
- ✅ **Fastest development** (simplest architecture)

### Cons
- ❌ **Requires server** (self-hosted or cloud)
- ❌ **Network required** (can't work offline)
- ❌ **Not a "desktop app"** (different user experience)
- ⚠️ **Multi-user or single-user?** (design question)
- ⚠️ **File storage** (books in Application Support → server storage?)

### Deployment Options
1. **Self-hosted:** User runs server locally (localhost:5000)
   - Feels like desktop app (no network needed)
   - Automatically start server on system startup

2. **Cloud-hosted:** Single server for all users
   - Requires network
   - Multi-user considerations

3. **Hybrid:** Offline-capable PWA
   - Progressive Web App with offline support
   - Best of both worlds

### When to Choose This
- If you're open to web app instead of desktop app
- If you value fastest development time
- If "access from any device" is appealing
- If you can self-host locally for single-user

### Development Effort
- **Weeks 1-2:** Setup Blazor Server project
- **Weeks 3-4:** Book display components
- **Weeks 5-6:** Search UI, tabs
- **Week 7:** Local hosting setup
- **Week 8:** Testing

**Total: 1-2 months** (fastest option)

### Example: CST Reader as Web App
```
User starts app → Launches ASP.NET Core server (background) → Opens browser to localhost:5000
User sees: Same UI as desktop app, works identically
Benefit: Works on iPad, Linux, ChromeOS, anything with a browser
```

---

## Option 5: WPF + WebView2 (Windows Only)

### Architecture
```
WPF Desktop App (Windows Only)
├─ AvalonDock (free WPF docking library)
├─ WebView2 (Microsoft Edge, free)
│   └─ Book rendering (HTML)
├─ Lucene.NET search
└─ Script converters
```

### What Changes
- **Replace:** Avalonia → WPF
- **Replace:** Dock.Avalonia → AvalonDock
- **Replace:** CefGlue → WebView2
- **Keep:** All other C# code

### Pros
- ✅ **Mature ecosystem** (WPF is 15+ years old)
- ✅ **WebView2 is excellent** (modern Edge, actively maintained)
- ✅ **AvalonDock is free** (popular WPF docking library)
- ✅ **Best Windows integration** (native feel)
- ✅ **Fastest development** (proven patterns)
- ✅ **Smallest app size** (WebView2 uses OS-installed Edge)

### Cons
- ❌ **WINDOWS ONLY** (no macOS, no Linux)
- ❌ **You're on macOS** (can't even develop on your machine!)

### When to Choose This
- **If you can drop macOS and Linux support**
- If Windows-only is acceptable (perhaps 90% of users?)
- If you want fastest, most stable path

### Development Effort
- **Week 1:** Setup WPF + WebView2
- **Week 2:** AvalonDock integration
- **Weeks 3-4:** Port views, view models
- **Week 5:** Testing

**Total: 1 month** (fastest, but Windows-only)

### Critical Decision
This requires **dropping macOS and Linux**. Is that acceptable?

---

## Option 6: Tauri + C# Backend

### Architecture
```
Tauri Desktop App
├─ Rust Frontend (manages window)
│   └─ WebView (HTML/CSS/JS)
└─ C# Backend (sidecar process)
    ├─ gRPC/REST API
    ├─ Lucene.NET
    ├─ Script converters
    └─ XML processing
```

### What It Is
Tauri is like Electron, but uses Rust + OS-native WebViews instead of Chromium. C# backend runs as separate process.

### What Changes
- **Replace:** Avalonia → Tauri/HTML frontend
- **Add:** Rust layer (minimal, mostly configuration)
- **Add:** C# backend as sidecar process
- **Add:** gRPC/REST API between Rust and C#
- **Keep:** All C# backend code

### Pros
- ✅ **Fully cross-platform** (Windows/macOS/Linux)
- ✅ **Smallest app size** (~10-20 MB, uses OS WebView)
- ✅ **Keep all C# backend**
- ✅ **Modern, fast** (Rust is performant)
- ✅ **Active development** (Tauri 2.0 in 2024)

### Cons
- ❌ **Community bridges** (not official C# support)
- ❌ **Learn Rust** (at least basic configuration)
- ❌ **More complex architecture** (three layers: Rust, WebView, C#)
- ❌ **IPC overhead** (communication between processes)
- ⚠️ **Less mature** than Electron

### Community C# Integration
Several community projects enable C# backends:
- TauriDotNetBridge
- tauri-sharp (uses gRPC)
- Custom sidecar approach

### When to Choose This
- If you want smallest app size
- If you're willing to learn basic Rust
- If you like cutting-edge technology
- If Electron.NET feels too heavy

### Development Effort
- **Weeks 1-2:** Learn Tauri basics, setup C# sidecar
- **Weeks 3-4:** gRPC API between Rust and C#
- **Weeks 5-8:** HTML/CSS/JS frontend
- **Weeks 9-10:** Integration, testing

**Total: 2-4 months**

---

## Strategic Decision Framework

### Question 1: Must you support Linux desktop?

**YES** → Options: Avalonia, Electron.NET, Blazor Server, Tauri
**NO** → Also consider: MAUI, WPF

### Question 2: What's your timeline?

**Fast (1-2 months)** → Blazor Server, MAUI (if no Linux), WPF (if Windows-only)
**Medium (2-3 months)** → Electron.NET
**Long (3-6 months)** → Avalonia + Custom Rendering, Tauri

### Question 3: Desktop app vs Web app?

**Must be desktop** → Avalonia, Electron.NET, MAUI, WPF, Tauri
**Web app acceptable** → Blazor Server (fastest, simplest)

### Question 4: How important is app size?

**Critical (<30 MB)** → Avalonia custom, Tauri, WPF+WebView2
**Important (<100 MB)** → MAUI
**Acceptable (150-200 MB)** → Electron.NET

### Question 5: UI Technology preference?

**Native controls (XAML)** → Avalonia, MAUI, WPF
**C# UI (not JavaScript)** → Blazor (MAUI or Server)
**HTML/CSS/JS** → Electron.NET, Tauri

### Question 6: Risk tolerance?

**Low risk (mature)** → WPF (Windows-only), Electron.NET
**Medium risk** → MAUI, Blazor Server
**High risk (cutting edge)** → Tauri, Avalonia custom rendering

---

## Recommended Decision Tree

### Scenario A: "I must support Linux desktop"

**Quick fix (1-2 months):**
→ **Blazor Server** (web app, works on all browsers)
- Fastest development
- Works everywhere (via browser)
- Can self-host locally

**Medium effort (2-3 months):**
→ **Electron.NET** (desktop app)
- Rewrite UI in HTML/CSS/JS
- Keep all C# backend
- Proven, stable

**Long-term best (3-6 months):**
→ **Avalonia + Custom Rendering** (stay your course)
- Solve rendering problem once and for all
- Best performance, smallest size
- Complete control

### Scenario B: "I can drop Linux, focus on Windows + macOS"

**Fastest (1-2 months):**
→ **.NET MAUI + Blazor Hybrid**
- Microsoft official
- Write UI in C# (Blazor)
- Keep all backend code

**Medium (2-3 months):**
→ **Electron.NET** (still viable, adds Linux later if needed)

### Scenario C: "I can drop macOS + Linux, Windows-only"

**Fastest, most stable (1 month):**
→ **WPF + WebView2**
- Mature ecosystem
- Excellent WebView2
- Free AvalonDock
- Best Windows experience

### Scenario D: "I want to rethink the desktop app model"

→ **Blazor Server** (web app)
- Accessible from any device
- Easiest maintenance
- Could still package as "desktop" with self-hosted server

---

## My Recommendation

Given your constraints:
1. **Must be free**
2. **Have valuable C# code to reuse**
3. **Cross-platform desired** (you're on macOS)
4. **Hitting CEF/Avalonia pain**

### Short-term (Beta 3 - next 2 months):

**Option A: Blazor Server (Web App)**
- ✅ Fastest development (1-2 months)
- ✅ Works on all platforms (via browser)
- ✅ Write UI in C# (not JavaScript)
- ✅ Keep all backend code
- ✅ Can self-host for "desktop feel"
- ⚠️ Different user experience (web, not desktop)

**Option B: Electron.NET (Desktop App)**
- ✅ True desktop app
- ✅ Cross-platform (Linux included)
- ✅ Keep all C# backend
- ⚠️ Rewrite UI in HTML/CSS/JS (2-3 months)
- ⚠️ Large app size (like current CEF)

### Long-term (v1.0 - 6+ months):

**Avalonia + Custom Native Rendering**
- If you're committed to Avalonia and true desktop app
- 3-6 months investment
- Best long-term solution
- See [ALTERNATIVE_RENDERING_ENGINES.md](ALTERNATIVE_RENDERING_ENGINES.md)

---

## Critical Questions to Answer

Before deciding, answer these:

### 1. Platform Priority
- **Must-have:** Windows, macOS, Linux?
- **Or acceptable:** Windows + macOS only? (enables MAUI)
- **Or acceptable:** Windows only? (enables WPF, fastest)

### 2. App Type
- **Must be desktop app?** (installable, offline, native feel)
- **Or web app acceptable?** (browser-based, self-hosted option)

### 3. Development Timeline
- **Need Beta 3 in 2 months?** → Rules out Avalonia custom rendering
- **Can wait 6 months for v1.0?** → All options viable

### 4. UI Technology
- **Prefer native XAML?** → Stay with Avalonia or WPF/MAUI
- **Okay with HTML/CSS/JS?** → Electron.NET, Tauri
- **Want C# UI (not JS)?** → Blazor (MAUI or Server)

### 5. App Size
- **Critical (<30 MB)?** → Rules out Electron.NET
- **Acceptable (150-200 MB)?** → Electron.NET viable

---

## Next Steps

1. **Answer the critical questions above**

2. **Based on answers, narrow to 1-2 options**

3. **Create proof-of-concept (1 week each):**
   - Load one book
   - Render with search highlights
   - Test on all target platforms
   - Evaluate user experience

4. **Make final decision based on POC results**

5. **Commit and execute**

---

## Personal Take

If I were in your position, I'd seriously consider:

### Path 1: Blazor Server (Web App)
**Why:**
- Fastest to ship (1-2 months)
- Works everywhere (any browser, any OS)
- Write UI in C#, not JavaScript
- Keep all backend code
- Can self-host for "desktop-like" experience
- Could always package as desktop later (via MAUI or Electron.NET)

**Tradeoff:**
- Not a traditional desktop app
- Requires server (but can run locally)

### Path 2: Electron.NET
**Why:**
- True desktop app
- WebView problem solved forever
- Keep all C# backend
- Proven, stable

**Tradeoff:**
- Rewrite UI (2-3 months)
- Large app size (but you have that now with CEF)

### Path 3: Stay with Avalonia + Custom Rendering
**Why:**
- Best long-term solution
- Complete control
- Native performance
- Already invested in Avalonia

**Tradeoff:**
- 3-6 months investment
- High technical complexity

---

## Conclusion

**There is no perfect answer**, but there are several viable free alternatives.

The real question is: **What's most important to you?**
- Speed to ship? → Blazor Server or MAUI (if drop Linux)
- Desktop app feel? → Electron.NET or stay with Avalonia
- App size? → Avalonia custom rendering or Tauri
- Simplicity? → Blazor Server or WPF (if Windows-only)

**My suggestion:** Build 2-3 small POCs (1 week each) with your top choices. See which feels right. You'll know pretty quickly which direction resonates with your goals and constraints.

Want me to help you create a POC for any of these options?
