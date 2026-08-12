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

The five built-in agent icons (Claude, Codex, Kimi, Grok, OpenCode) use the official brand glyphs from [@lobehub/icons](https://github.com/lobehub/lobe-icons) (MIT license; source package `@lobehub/icons-static-svg`), retrieved 2026-08-12. Glyphs are presented unmodified on a rounded tile so both light and dark themes stay legible:

- Claude: Claude Code glyph in brand clay `#D97757` on a white tile with a hairline border.
- Codex: official color gradient mark (blue-violet `#B1A7FF` → `#3941FF`) on its own white tile.
- Kimi: white `K` with the brand blue dot `#1783FF` (color variant) on a near-black `#1A1A1A` tile.
- Grok: white swirl glyph on a black tile (brand color `#000`).
- OpenCode: black frame glyph on a white tile with a hairline border (brand color `#000`).

Before a public build, verify usage against each vendor's brand terms (Anthropic/Claude, OpenAI/Codex, Moonshot/Kimi, xAI/Grok, OpenCode) as required below.

Before a public build:

1. Obtain each asset from the vendor's official brand or press resource.
2. Record the source URL, retrieval date, license/permission, required clear space, allowed colors, and modification restrictions.
3. Keep original proportions and do not combine a vendor mark with the RightAgent identity.
4. Produce local SVG/PNG for WinUI and multi-resolution ICO for Explorer; do not fetch images at runtime.
5. If permission is unclear, keep the neutral placeholder or let the user supply a custom local icon.

OpenAI assets and terms should be reviewed from the [official OpenAI brand page](https://openai.com/brand/). Equivalent official sources are required for Claude and Kimi before replacing their placeholders.

Regenerate the neutral development agent placeholders (never the app logo) with:

```powershell
python .\scripts\Generate-PlaceholderAssets.py
```

The generator is a development convenience; Python is not a RightAgent runtime dependency.
