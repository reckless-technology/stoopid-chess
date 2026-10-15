"""The application icons, drawn from the one source: packaging/icon/BlackQueen.svg.

The source is Colin M.L. Burnett's black queen from the "cburnett" set (GPLv2 or later), the SVG Lichess ships at
lichess-org/lila 7370914942f30f1a77321ff6e5bc0ad3e4dd5841, public/piece/cburnett/bQ.svg - the same artwork the 64 px
piece images were rendered from. The icon sets it, unchanged, on a rounded square of the light square colour of the
Lichess board the set comes from, laid out on Apple's macOS icon grid, so macOS 26 and later draw it as it is (not on
their grey square) and the black piece reads on a dark background:

- a 1024 px canvas, the square 824 px and centred, its corners rounded with a 185 px radius, filled #f0d9b5 with a
  slight vertical gradient, over a soft drop shadow (macOS clips it to its own continuous-corner mask);
- the queen centred on it, its drawn height 70 % of the square's.

Every size is rendered from that vector, none scaled from another, and the same artwork serves Windows, Linux and
macOS. Below 48 px (the .ico's 16, 24 and 32, the .icns's 16, 16@2x and 32) the shadow is left out, where it would
only blur the square's edge into a grey halo, and the square is snapped to whole pixels, so its edges stay crisp:

    python3 tools/make-icons.py

writes, all committed (the builds read them, and need none of the tools below):

    src/StoopidChess.Desktop/Assets/AppIcon.ico   16, 24, 32, 48, 64, 128 and 256 px: the Windows .exe's icon
                                                  (ApplicationIcon) and the window's on Windows; 32-bit BMP entries
                                                  with alpha up to 128 px, PNG at 256, as Windows expects
    src/StoopidChess.Desktop/Assets/AppIcon.png   256 px: the window's icon on Linux and macOS
    packaging/macos/StoopidChess.icns             16 to 512 pt at @1x and @2x (up to 1024 px), the macOS app's icon

It needs rsvg-convert (librsvg; brew install librsvg) and, for the .icns, macOS's iconutil. Run it again whenever the
SVG or the layout changes, and commit what it writes.
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
# Apple's macOS icon grid, in pixels of a 1024 px canvas: the square's side and its corner radius. The corners are
# circular: macOS 26 and later draw an icon as it is only when it covers their own continuous-corner mask, and then
# clip it to that mask themselves; a superellipse of exponent 5 (the usual stand-in for Apple's curve) falls inside the
# mask at the corners, and macOS put it on its grey square, while this rounded rectangle passes (checked on macOS 27).
CANVAS = 1024
SQUARE = 824
CORNER_RADIUS = 185
# The light square of the Lichess board the cburnett set is drawn for, from a touch lighter at the top to a touch
# darker at the bottom.
FILL_TOP, FILL_BOTTOM = "#f3dfbf", "#ecd3aa"
# A soft drop shadow beneath the square, as Apple's icons have: offset, blur (a standard deviation) and opacity.
SHADOW_OFFSET, SHADOW_BLUR, SHADOW_OPACITY = 10, 10, 0.3
# Below this many pixels the shadow is left out, where it would only blur the square's edge into a grey halo, and the
# square is snapped to a whole, even number of pixels so its edges fall on pixel boundaries.
SMALL_BELOW = 48
# The queen's drawn height, as a fraction of the square's.
QUEEN_HEIGHT = 0.70


def rounded_square_path(centre: float, side: float) -> str:
    """The SVG path of the square of the given side centred on the canvas, its corners rounded to the grid's radius
    (scaled with the side)."""
    radius = CORNER_RADIUS * side / SQUARE
    left, right = centre - side / 2, centre + side / 2
    top, bottom = left, right
    return (f"M{left + radius:.3f},{top:.3f} H{right - radius:.3f} "
            f"A{radius:.3f},{radius:.3f} 0 0 1 {right:.3f},{top + radius:.3f} V{bottom - radius:.3f} "
            f"A{radius:.3f},{radius:.3f} 0 0 1 {right - radius:.3f},{bottom:.3f} H{left + radius:.3f} "
            f"A{radius:.3f},{radius:.3f} 0 0 1 {left:.3f},{bottom - radius:.3f} V{top + radius:.3f} "
            f"A{radius:.3f},{radius:.3f} 0 0 1 {left + radius:.3f},{top:.3f} Z")


def drawn_bounds(svg_path: Path, work: Path) -> tuple[float, float, float, float]:
    """The box, in the SVG's own units, that the drawing actually covers (its view box has margins): measured from a
    450 px render's alpha."""
    match = re.search(r'viewBox="([-\d.]+) ([-\d.]+) ([-\d.]+) ([-\d.]+)"', svg_path.read_text(encoding="utf-8"))
    if not match:
        sys.exit(f"{svg_path} has no viewBox")
    box_x, box_y, box_width, box_height = (float(value) for value in match.groups())
    size = 450
    width, height, rgba = png_rgba(render(svg_path, size, work / "bounds.png"))
    covered = [(index // 4 % width, index // 4 // width) for index in range(3, len(rgba), 4) if rgba[index]]
    left, right = min(x for x, _ in covered), max(x for x, _ in covered) + 1
    top, bottom = min(y for _, y in covered), max(y for _, y in covered) + 1
    return (box_x + left * box_width / width, box_y + top * box_height / height,
            (right - left) * box_width / width, (bottom - top) * box_height / height)


def icon_svg(source: str, bounds: tuple[float, float, float, float], size: int) -> str:
    """The icon on the 1024 px canvas, for rendering at size pixels: the rounded square, its shadow (not at small
    sizes) and the queen centred on it."""
    centre = CANVAS / 2
    is_small = size < SMALL_BELOW
    side = SQUARE
    if is_small:
        side = round(size * SQUARE / CANVAS / 2) * 2 * CANVAS / size
    square = rounded_square_path(centre, side)
    x, y, width, height = bounds
    queen_height = side * QUEEN_HEIGHT
    queen_width = queen_height * width / height
    body = source[source.index(">", source.index("<svg")) + 1:source.rindex("</svg>")]
    shadow = ""
    if not is_small:
        shadow = (f'<filter id="shadow" x="-10%" y="-10%" width="120%" height="125%">'
                  f'<feGaussianBlur stdDeviation="{SHADOW_BLUR}"/></filter>'
                  f'<path d="{square}" fill="#000" fill-opacity="{SHADOW_OPACITY}" filter="url(#shadow)" '
                  f'transform="translate(0 {SHADOW_OFFSET})"/>')
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{CANVAS}" height="{CANVAS}" '
            f'viewBox="0 0 {CANVAS} {CANVAS}">'
            f'<defs><linearGradient id="fill" x1="0" y1="0" x2="0" y2="1">'
            f'<stop offset="0" stop-color="{FILL_TOP}"/><stop offset="1" stop-color="{FILL_BOTTOM}"/>'
            f'</linearGradient></defs>'
            f'{shadow}<path d="{square}" fill="url(#fill)"/>'
            f'<svg x="{centre - queen_width / 2:.3f}" y="{centre - queen_height / 2:.3f}" width="{queen_width:.3f}" '
            f'height="{queen_height:.3f}" viewBox="{x:.4f} {y:.4f} {width:.4f} {height:.4f}">{body}</svg>'
            f'</svg>')


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
    source = SOURCE.read_text(encoding="utf-8")
    with tempfile.TemporaryDirectory() as temporary:
        work = Path(temporary)
        bounds = drawn_bounds(SOURCE, work)

        def render_icon(size: int, out: Path) -> bytes:
            svg = work / f"icon-{size}.svg"
            svg.write_text(icon_svg(source, bounds, size), encoding="utf-8")
            return render(svg, size, out)

        images = {size: render_icon(size, work / f"ico-{size}.png") for size in ICO_SIZES}
        write_ico(images, ICO)
        print(f"{ICO.relative_to(REPO)}: {', '.join(str(size) for size in ICO_SIZES)} px")
        render_icon(WINDOW_ICON_SIZE, PNG)
        print(f"{PNG.relative_to(REPO)}: {WINDOW_ICON_SIZE} px")

        if shutil.which("iconutil") is None:
            print(f"no iconutil (macOS only): {ICNS.relative_to(REPO)} left as it is")
            return
        iconset = work / "StoopidChess.iconset"
        iconset.mkdir()
        for points in ICNS_POINT_SIZES:
            render_icon(points, iconset / f"icon_{points}x{points}.png")
            render_icon(points * 2, iconset / f"icon_{points}x{points}@2x.png")
        subprocess.run(["iconutil", "--convert", "icns", "--output", str(ICNS), str(iconset)], check=True)
        print(f"{ICNS.relative_to(REPO)}: {', '.join(f'{p}@1x, {p}@2x' for p in ICNS_POINT_SIZES)}")


if __name__ == "__main__":
    main()
