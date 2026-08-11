# Brand asset policy

The checked-in C/X/K monograms are neutral development placeholders. They are not the Claude, OpenAI/Codex, or Kimi logos and must not be described as official marks.

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
