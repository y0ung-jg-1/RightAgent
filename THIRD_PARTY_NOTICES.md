# Third-Party Notices

RightAgent itself is licensed under the MIT License in `LICENSE`. This file
describes third-party material distributed with RightAgent; it does not grant
permission to use any third-party trademark.

## Bundled Microsoft and .NET components

The self-contained Windows package includes the following redistributable
components. Their original license and third-party notice files are shipped
verbatim under `ThirdPartyNotices/`:

| Component | Version | Packaged notices |
| --- | --- | --- |
| Microsoft Windows App SDK | 2.3.1 | `WindowsAppSDK/LICENSE.txt`, `WindowsAppSDK/NOTICE.txt` |
| Microsoft Windows App SDK WinUI | 2.3.0 | `WindowsAppSDKWinUI/LICENSE.txt`, `WindowsAppSDKWinUI/NOTICE.txt` |
| Microsoft Windows App SDK ML | 2.1.74 | `WindowsAppSDKML/LICENSE.txt`, `WindowsAppSDKML/THIRD_PARTY_NOTICES.txt` |
| Microsoft .NET runtime for Windows x64 | 10.0.11 | `DotNETRuntime/LICENSE.txt`, `DotNETRuntime/THIRD_PARTY_NOTICES.txt` |
| Microsoft WebView2 SDK components | 1.0.3719.77 | `WebView2/LICENSE.txt`, `WebView2/NOTICE.txt` |
| System.Numerics.Tensors | 9.0.0 | `SystemNumericsTensors/LICENSE.txt`, `SystemNumericsTensors/THIRD_PARTY_NOTICES.txt` |

Those files include the notices for transitive native and managed components
distributed by the corresponding packages. They must be reviewed and refreshed
whenever one of the versions above changes.

## Windows Community Toolkit

The settings app uses `CommunityToolkit.WinUI.Controls.SettingsControls`
8.2.251219 (`SettingsCard` and `SettingsExpander`) under the MIT License.
The original license text is in `ThirdPartyNotices/CommunityToolkit/LICENSE.md`.

## Installer technology

The GitHub Release Setup executable is generated with WiX Toolset 7.0.0
(per-machine Burn Setup.exe, per-user UserSetup.exe, and the embedded MSI). WiX Toolset is Copyright (c) .NET Foundation
and contributors and is available from
[wixtoolset.org](https://wixtoolset.org/) under the Microsoft Reciprocal
License. This acknowledgment is included for clarity; WiX is a build tool and
is not a resident RightAgent runtime dependency.

## Lobe Icons

The following checked-in agent glyphs were adapted from the
`@lobehub/icons-static-svg` package in the
[Lobe Icons](https://github.com/lobehub/lobe-icons) project. The original five
glyphs were retrieved on 2026-08-12; the Cursor glyph was retrieved from
package version 1.94.0 on 2026-08-13:

- `RightAgent.App/Assets/Agents/claude.svg`
- `RightAgent.App/Assets/Agents/codex.svg`
- `RightAgent.App/Assets/Agents/kimi.svg`
- `RightAgent.App/Assets/Agents/grok.svg`
- `RightAgent.App/Assets/Agents/opencode.svg`
- `RightAgent.App/Assets/Agents/cursor.svg`
- the corresponding generated ICO files under
  `RightAgent.Package/Assets/Agents/`

Lobe Icons is licensed under the MIT License:

```text
MIT License

Copyright (c) 2023 LobeHub

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Trademarks

Claude, Codex, Kimi, Grok, OpenCode, Cursor, and their respective logos are
trademarks or brand assets of their respective owners. Their names identify
compatible third-party tools only. RightAgent is an independent project and is
not affiliated with, endorsed by, or sponsored by those owners.

The Lobe Icons MIT license covers copyright in the icon collection; it does
not replace the separate trademark and brand-usage requirements documented in
`docs/BRAND_ASSETS.md`.
