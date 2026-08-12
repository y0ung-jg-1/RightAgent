# Brand asset policy

The five built-in agent icons (Claude, Codex, Kimi, Grok, OpenCode) use the official brand glyphs from [@lobehub/icons](https://github.com/lobehub/lobe-icons) (MIT license; source package `@lobehub/icons-static-svg`, monochrome `currentColor` glyphs), retrieved 2026-08-12. Each glyph is presented unmodified on RightAgent's rounded tile background so both light and dark themes stay legible:

- Claude: white glyph on the brand clay `#D97757`.
- Codex: white glyph on near-black `#1A1A1A` (brand mark is monochrome).
- Kimi: black glyph on white with a hairline border (brand color `#000`).
- Grok: white glyph on near-black `#1A1A1A` (brand color `#000`).
- OpenCode: black glyph on white with a hairline border (brand color `#000`).

Before a public build, verify usage against each vendor's brand terms (Anthropic/Claude, OpenAI/Codex, Moonshot/Kimi, xAI/Grok, OpenCode) as required below.

Before a public build:

1. Obtain each asset from the vendor's official brand or press resource.
2. Record the source URL, retrieval date, license/permission, required clear space, allowed colors, and modification restrictions.
3. Keep original proportions and do not combine a vendor mark with the RightAgent identity.
4. Produce local SVG/PNG for WinUI and multi-resolution ICO for Explorer; do not fetch images at runtime.
5. If permission is unclear, keep the neutral placeholder or let the user supply a custom local icon.

OpenAI assets and terms should be reviewed from the [official OpenAI brand page](https://openai.com/brand/). Equivalent official sources are required for Claude and Kimi before replacing their placeholders.

Regenerate the neutral development assets with:

```powershell
python .\scripts\Generate-PlaceholderAssets.py
```

The generator is a development convenience; Python is not a RightAgent runtime dependency.
