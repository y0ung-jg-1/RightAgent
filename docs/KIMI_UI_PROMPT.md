# Prompt for Kimi Code: RightAgent UI refinement

Use this prompt only after the current solution builds and the core acceptance tests pass:

```text
You are refining the WinUI 3 settings UI of RightAgent, a Windows 11 developer utility.

Repository: D:\myprojects\RightAgent
Target: Windows 11 x64, C# WinUI 3, .NET 10, Windows App SDK 2.3.x.

Your allowed edit scope is strictly:
- RightAgent.App/**
- documentation or screenshots specifically about the UI

Do not edit:
- RightAgent.Core/**
- RightAgent.Native.Core/**
- RightAgent.Shell/**
- RightAgent.Launcher/**
- RightAgent.Package/Package.appxmanifest
- RightAgent.Package/RightAgent.Package.wapproj
- docs/SETTINGS_SCHEMA.md
- the schemaVersion 1 JSON field names or action values
- the launcher arguments: --agent <id> --cwd <absolute-directory>

Read README.md, docs/ARCHITECTURE.md, docs/SETTINGS_SCHEMA.md, and the existing RightAgent.App code before changing anything.

Product behavior that must remain:
1. Switch between a grouped menu (“使用 RightAgent 打开 / Open with RightAgent”) and a direct single-Agent menu.
2. Enable, rename, add, delete, and reorder arbitrary agents.
3. Edit terminalCommand and http/https URL actions.
4. Choose a custom local PNG/JPG/BMP/ICO that is normalized to ICO under LocalState/Icons.
5. Choose system, zh-CN, or en-US language and an optional Windows Terminal profile.
6. Show a live context-menu preview and save settings atomically through RightAgent.Core.

Design direction:
- Native Windows 11 settings utility, not a web dashboard.
- Calm, minimal, single-column information hierarchy with system Mica/card conventions where supported.
- Use only ThemeResource colors so light, dark, and high contrast remain correct.
- Keep one obvious primary action: Save settings.
- Use Segoe Fluent Icons or other local vector assets; never use emoji as structural icons.
- All interactive targets must be at least 44x44 effective pixels, with visible keyboard focus and useful AutomationProperties.Name values.
- Preserve full keyboard operation; do not require drag-only reordering. Up/down buttons may remain even if drag-and-drop is added.
- Use native controls and 150–250 ms purposeful transitions only; respect reduced motion and avoid decorative animation.
- Long names/commands must wrap or expose full text through a tooltip; do not silently truncate editable values.
- Keep the current neutral development icons. Do not invent or redraw vendor logos.

Implementation requirements:
- Keep view state in the existing view models or improve it within RightAgent.App only.
- Do not introduce Electron, WebView, Node, React, a service, telemetry, network image loading, or a new settings format.
- Do not make RightAgent resident after its window closes.
- Validate errors next to the relevant editor where practical and keep an accessible summary InfoBar.
- Preserve custom icon copying and all bilingual strings.

Verification:
- Build RightAgent.App and the WAP package in Debug x64.
- Run existing managed and native tests without changing their expectations.
- Exercise light/dark/high contrast, 100/150/200% scaling, keyboard-only navigation, Narrator names, empty state, 3 built-ins, and at least 12 custom agents.
- Provide before/after screenshots and a concise list of files changed. If a core/manifest change seems necessary, stop and explain it instead of making it.
```
