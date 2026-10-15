"""The application icons, drawn from the one source: packaging/icon/BlackQueen.svg.

The source is Colin M.L. Burnett's black queen from the "cburnett" set (GPLv2 or later), the SVG Lichess ships at
lichess-org/lila 7370914942f30f1a77321ff6e5bc0ad3e4dd5841, public/piece/cburnett/bQ.svg - the same artwork the 64 px
piece images and the old 48 px icon were rendered from. Every size is rendered from the vector, none scaled from
another:

    python3 tools/make-icons.py

writes, all committed (the builds read them, and need none of the tools below):

    src/StoopidChess.Desktop/Assets/AppIcon.ico   16, 24, 32, 48, 64, 128 and 256 px: the Windows .exe's icon
                                                  (ApplicationIcon) and the window's on Windows; 32-bit BMP entries
                                                  with alpha up to 128 px, PNG at 256, as Windows expects
    src/StoopidChess.Desktop/Assets/AppIcon.png   256 px: the window's icon on Linux and macOS
    packaging/macos/StoopidChess.icns             16 to 512 px at @1x and @2x (1024 px), the macOS app's icon, with
                                                  the margin of Apple's icon grid (an 824 px body on 1024)

It needs rsvg-convert (librsvg; brew install librsvg) and, for the .icns, macOS's iconutil. Run it again whenever the
SVG changes, and commit what it writes.
"""

from __future__ import annotations

import re
import shutil
import struct
import subprocess
import sys
import tempfile
import zlib
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
SOURCE = REPO / "packaging" / "icon" / "BlackQueen.svg"
ICO = REPO / "src" / "StoopidChess.Desktop" / "Assets" / "AppIcon.ico"
PNG = REPO / "src" / "StoopidChess.Desktop" / "Assets" / "AppIcon.png"
ICNS = REPO / "packaging" / "macos" / "StoopidChess.icns"

ICO_SIZES = (16, 24, 32, 48, 64, 128, 256)
WINDOW_ICON_SIZE = 256
# iconutil's names: each point size at @1x and @2x.
ICNS_POINT_SIZES = (16, 32, 128, 256, 512)
# Apple's macOS icon grid: the body is 824 px of a 1024 px canvas, 100 px of margin on every side.
MACOS_BODY_FRACTION = 824 / 1024


def padded_svg(svg: str, body_fraction: float) -> str:
    """The SVG with its view box widened about its centre, so the drawing fills body_fraction of the canvas."""
    match = re.search(r'viewBox="([-\d.]+) ([-\d.]+) ([-\d.]+) ([-\d.]+)"', svg)
    if not match:
        sys.exit(f"{SOURCE} has no viewBox")
    x, y, width, height = (float(value) for value in match.groups())
    new_width, new_height = width / body_fraction, height / body_fraction
    box = (f'viewBox="{x - (new_width - width) / 2:.4f} {y - (new_height - height) / 2:.4f} '
           f'{new_width:.4f} {new_height:.4f}"')
    return svg[:match.start()] + box + svg[match.end():]


def render(svg_path: Path, size: int, out: Path) -> bytes:
    """The SVG rendered to a size x size PNG with a transparent background; its bytes."""
    subprocess.run(["rsvg-convert", "--width", str(size), "--height", str(size), "--keep-aspect-ratio",
                    "--output", str(out), str(svg_path)], check=True)
    return out.read_bytes()


def png_rgba(data: bytes) -> tuple[int, int, bytes]:
    """Width, height and the straight-alpha RGBA rows of an 8-bit RGBA, non-interlaced PNG (what rsvg-convert writes)."""
    if data[:8] != b"\x89PNG\r\n\x1a\n":
        sys.exit("not a PNG")
    position, idat, header = 8, b"", None
    while position < len(data):
        length, kind = struct.unpack(">I4s", data[position:position + 8])
        body = data[position + 8:position + 8 + length]
        if kind == b"IHDR":
            header = struct.unpack(">IIBBBBB", body)
        elif kind == b"IDAT":
            idat += body
        position += 12 + length
    if header is None:
        sys.exit("a PNG with no IHDR")
    width, height, depth, color_type, _, _, interlace = header
    if (depth, color_type, interlace) != (8, 6, 0):
        sys.exit(f"a PNG of depth {depth}, color type {color_type}, interlace {interlace}: only 8-bit RGBA is read")
    raw = zlib.decompress(idat)
    stride = width * 4
    rows: list[bytearray] = []
    previous = bytearray(stride)
    for row_index in range(height):
        start = row_index * (stride + 1)
        kind, row = raw[start], bytearray(raw[start + 1:start + 1 + stride])
        for i in range(stride):
            left = row[i - 4] if i >= 4 else 0
            up = previous[i]
            up_left = previous[i - 4] if i >= 4 else 0
            if kind == 1:
                row[i] = (row[i] + left) & 0xFF
            elif kind == 2:
                row[i] = (row[i] + up) & 0xFF
            elif kind == 3:
                row[i] = (row[i] + (left + up) // 2) & 0xFF
            elif kind == 4:
                estimate = left + up - up_left
                distances = (abs(estimate - left), abs(estimate - up), abs(estimate - up_left))
                predictor = (left, up, up_left)[distances.index(min(distances))]
                row[i] = (row[i] + predictor) & 0xFF
            elif kind != 0:
                sys.exit(f"PNG filter {kind}")
        rows.append(row)
        previous = row
    return width, height, b"".join(rows)


def ico_bitmap(png: bytes) -> bytes:
    """An ICO entry's 32-bit BMP: a BITMAPINFOHEADER of twice the height, BGRA rows bottom up, then the AND mask."""
    width, height, rgba = png_rgba(png)
    header = struct.pack("<IiiHHIIiiII", 40, width, height * 2, 1, 32, 0, 0, 0, 0, 0, 0)
    pixels = bytearray()
    mask = bytearray()
    mask_stride = ((width + 31) // 32) * 4
    for y in reversed(range(height)):
        row = rgba[y * width * 4:(y + 1) * width * 4]
        mask_row = bytearray(mask_stride)
        for x in range(width):
            r, g, b, a = row[x * 4:x * 4 + 4]
            pixels += bytes((b, g, r, a))
            if a == 0:
                mask_row[x // 8] |= 0x80 >> (x % 8)
        mask += mask_row
    return header + bytes(pixels) + bytes(mask)


def write_ico(images: dict[int, bytes], out: Path) -> None:
    """A multi-size .ico: BMP entries below 256 px, the 256 px one PNG-compressed."""
    entries = [(size, images[size] if size >= 256 else ico_bitmap(images[size])) for size in sorted(images)]
    directory = struct.pack("<HHH", 0, 1, len(entries))
    offset = 6 + 16 * len(entries)
    blobs = b""
    for size, blob in entries:
        dimension = 0 if size >= 256 else size  # 0 means 256 in an ICONDIRENTRY
        directory += struct.pack("<BBBBHHII", dimension, dimension, 0, 0, 1, 32, len(blob), offset)
        offset += len(blob)
        blobs += blob
    out.write_bytes(directory + blobs)


def main() -> None:
    if shutil.which("rsvg-convert") is None:
        sys.exit("rsvg-convert is not on the PATH (brew install librsvg)")
    svg = SOURCE.read_text(encoding="utf-8")
    with tempfile.TemporaryDirectory() as temporary:
        work = Path(temporary)
        full = work / "full.svg"
        full.write_text(svg, encoding="utf-8")
        macos = work / "macos.svg"
        macos.write_text(padded_svg(svg, MACOS_BODY_FRACTION), encoding="utf-8")

        images = {size: render(full, size, work / f"ico-{size}.png") for size in ICO_SIZES}
        write_ico(images, ICO)
        print(f"{ICO.relative_to(REPO)}: {', '.join(str(size) for size in ICO_SIZES)} px")
        render(full, WINDOW_ICON_SIZE, PNG)
        print(f"{PNG.relative_to(REPO)}: {WINDOW_ICON_SIZE} px")

        if shutil.which("iconutil") is None:
            print(f"no iconutil (macOS only): {ICNS.relative_to(REPO)} left as it is")
            return
        iconset = work / "StoopidChess.iconset"
        iconset.mkdir()
        for points in ICNS_POINT_SIZES:
            render(macos, points, iconset / f"icon_{points}x{points}.png")
            render(macos, points * 2, iconset / f"icon_{points}x{points}@2x.png")
        subprocess.run(["iconutil", "--convert", "icns", "--output", str(ICNS), str(iconset)], check=True)
        print(f"{ICNS.relative_to(REPO)}: {', '.join(f'{p}@1x, {p}@2x' for p in ICNS_POINT_SIZES)}")


if __name__ == "__main__":
    main()
