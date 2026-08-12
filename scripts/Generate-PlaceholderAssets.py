"""Generate deterministic, neutral placeholder icons for development builds.

These are intentionally not vendor logos. Replace them only with approved official
assets after reviewing each vendor's brand terms.

Note: the RightAgent app logo ("rightagent") is a designed release asset and is NOT
regenerated here — see docs/BRAND_ASSETS.md for its source of truth.
"""

from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parents[1]
PACKAGE_ASSETS = ROOT / "RightAgent.Package" / "Assets"
AGENT_ASSETS = PACKAGE_ASSETS / "Agents"
FONT = Path("C:/Windows/Fonts/segoeuib.ttf")


def font(size: int) -> ImageFont.FreeTypeFont | ImageFont.ImageFont:
    return ImageFont.truetype(str(FONT), size) if FONT.exists() else ImageFont.load_default()


def rounded_icon(size: int, color: str, letter: str) -> Image.Image:
    scale = 4
    canvas = Image.new("RGBA", (size * scale, size * scale), (0, 0, 0, 0))
    draw = ImageDraw.Draw(canvas)
    inset = max(1, round(size * 0.04)) * scale
    radius = round(size * 0.22) * scale
    draw.rounded_rectangle(
        (inset, inset, size * scale - inset, size * scale - inset),
        radius=radius,
        fill=color,
    )
    face = font(round(size * 0.52) * scale)
    bounds = draw.textbbox((0, 0), letter, font=face)
    width = bounds[2] - bounds[0]
    height = bounds[3] - bounds[1]
    draw.text(
        ((size * scale - width) / 2, (size * scale - height) / 2 - bounds[1]),
        letter,
        fill="white",
        font=face,
    )
    return canvas.resize((size, size), Image.Resampling.LANCZOS)


def save_ico(path: Path, color: str, letter: str) -> None:
    source = rounded_icon(256, color, letter)
    source.save(path, format="ICO", sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])


def main() -> None:
    PACKAGE_ASSETS.mkdir(parents=True, exist_ok=True)
    AGENT_ASSETS.mkdir(parents=True, exist_ok=True)

    identities = {
        "claude": ("#B45309", "C"),
        "codex": ("#0F766E", "X"),
        "kimi": ("#6D28D9", "K"),
    }
    for name, (color, letter) in identities.items():
        save_ico(AGENT_ASSETS / f"{name}.ico", color, letter)


if __name__ == "__main__":
    main()
