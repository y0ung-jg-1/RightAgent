# Brand asset policy

## RightAgent app logo

The app logo is an original RightAgent design, created 2026-08-12: a white context-menu
card whose first item is an AI sparkle, opened by a mouse pointer — "AI agents in the
right-click menu" — on a blue gradient tile (`#58A6F2` → `#2563EB`). It is not derived
from any vendor mark.

Source of truth: `RightAgent.App/Assets/Agents/rightagent.svg` (64px grid, `rx=14` tile
matching the agent icon family). Derived release assets live in `RightAgent.Package/Assets/`
(`Agents/rightagent.ico` with the same 4% inset as the agent tiles, `Square44x44Logo.png`,
`Square150x150Logo.png`, `StoreLogo.png`, `Wide310x150Logo.png`).

## Built-in agent icons

The six checked-in agent icons were adapted from
[@lobehub/icons](https://github.com/lobehub/lobe-icons) (MIT license; source
package `@lobehub/icons-static-svg`). The original five were retrieved on
2026-08-12; Cursor was retrieved from package version 1.94.0 on 2026-08-13. The
full copyright notice is preserved in
[`THIRD_PARTY_NOTICES.md`](../THIRD_PARTY_NOTICES.md).

The MIT license covers the icon collection's copyright. It does not grant
trademark permission from the vendors represented by the glyphs. For the v1
GitHub release, the project owner decided on 2026-08-12 to retain the current
Lobe-derived assets and accepts responsibility for that separate trademark
assessment. They identify compatible third-party tools only and do not imply
affiliation, sponsorship, or endorsement.

| Agent | Current checked-in asset | Vendor source reviewed | Public-release status |
| --- | --- | --- | --- |
| Claude Code | Lobe Icons glyph on a RightAgent tile | Anthropic product and legal pages; no public logo-use guide or downloadable asset was located | Retained for v1 by project decision; not represented as an official asset |
| Codex | Lobe Icons color glyph on a RightAgent tile | [OpenAI brand guidelines](https://openai.com/brand/) | Retained for v1 by project decision; not represented as an official asset |
| Kimi | Lobe Icons color glyph on a RightAgent tile | [Moonshot AI KIMI Branding Guide](https://moonshotai.github.io/Branding-Guide/) | Retained for v1 by project decision; not represented as an official asset |
| Grok | Lobe Icons glyph on a RightAgent tile | [xAI brand guidelines](https://x.ai/legal/brand-guidelines) | Retained for v1 by project decision; not represented as an official asset |
| OpenCode | Lobe Icons glyph on a RightAgent tile | [OpenCode brand page](https://opencode.ai/brand) | Retained for v1 by project decision; not represented as an official asset |
| Cursor Agent | Lobe Icons glyph on a RightAgent tile | [Cursor](https://cursor.com/) | Retained for v1 by project decision; not represented as an official asset |

For a future switch to vendor-provided official assets:

1. Obtain each asset from the vendor's official brand or press resource and
   retain the downloaded source file or an immutable source URL.
2. Record the source URL, retrieval date, license/permission, required clear space, allowed colors, and modification restrictions.
3. Keep original proportions and do not combine a vendor mark with the RightAgent identity.
4. Produce local SVG/PNG for WinUI and multi-resolution ICO for Explorer; do not fetch images at runtime.
5. If permission is unclear, use the neutral placeholder or let the user supply
   a custom local icon.

Do not describe a Lobe Icons glyph as an "official vendor asset". Lobe Icons is
the copyright source for the current files; vendor brand clearance is a
separate requirement.

Regenerate the neutral development agent placeholders (never the app logo) with:

```powershell
python .\scripts\Generate-PlaceholderAssets.py
```

The generator is a development convenience; Python is not a RightAgent runtime dependency.
