#!/usr/bin/env python3
"""
SideStack 图标生成器 —— 鲜艳几何风格的多尺寸 app.ico

设计（全部按画布比例定义，所以任何尺寸都严格对齐）：
  · 底板：圆角方块，鲜艳蓝→紫渐变（#4C8DF6 → #2B6CE8 → #7C3AED），左上柔光 + 右下压暗
  · 主体：三张颜色鲜艳的圆角卡片（珊瑚红 / 琥珀黄 / 翡翠绿）沿对角线层叠，右下角压在
          白色侧栏旁边 —— 「桌面图标被一张张收进侧边面板」，前排最大的珊瑚卡就是视觉主体
  · 每张卡片带顶部高光 + 柔和投影，做出层次，避免"平涂色块"
  · 三档尺寸策略：≥40px 细节档（高光+投影）/ 24~32px 平涂加粗档 / ≤20px 只留侧栏+两张卡
          （16px 里三张卡的缝隙只有 1px 级，砍掉一张才不糊）

用法：
    python tools/make_icon.py                 # 生成 app.ico（覆盖前先备份 app.ico.orig.bak）并输出预览
    python tools/make_icon.py --preview-only  # 只输出 .icon-preview/*.png，不动 app.ico
"""
from __future__ import annotations

import argparse
import math
import shutil
import struct
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parent.parent
ICON = ROOT / "app.ico"
PREVIEW_DIR = ROOT / ".icon-preview"

# ---------------------------------------------------------------- 底板
SS = 8                   # 超采样倍数（先画大图再缩，得到干净边缘）

TILE_RADIUS = 0.228
GRAD_TOP = (0x4C, 0x8D, 0xF6)
GRAD_MID = (0x2B, 0x6C, 0xE8)
GRAD_MID_AT = 0.52
GRAD_BOTTOM = (0x7C, 0x3A, 0xED)
HIGHLIGHT_ALPHA = 0.18   # 左上柔光
VIGNETTE_ALPHA = 0.10    # 右下压暗
EDGE_ALPHA = 0.22        # 贴边内描边

# ---------------------------------------------------------------- 侧栏面板（屏幕侧边的 Dock）
PANEL_X0, PANEL_X1 = 0.055, 0.505
PANEL_Y0, PANEL_Y1 = 0.115, 0.885
PANEL_RADIUS = 0.105
PANEL_ALPHA = 62          # 玻璃面板填充
PANEL_ALPHA_BOLD = 80     # 小尺寸面板更实一点
PANEL_EDGE_ALPHA = 120    # 面板描边（只有细节档画）

# ---------------------------------------------------------------- 三色卡片
CORAL = (0xFF, 0x5A, 0x5F)
AMBER = (0xFF, 0xC9, 0x3C)
EMERALD = (0x2B, 0xD9, 0x7C)
CARDS = [                 # (颜色, 左边缘 x, 上边缘 y, 边长)，从后排画到前排
    (EMERALD, 0.415, 0.165, 0.300),
    (AMBER, 0.310, 0.300, 0.340),
    (CORAL, 0.150, 0.400, 0.400),   # 前排最大 → 视觉主体，正好落在面板里
]
CARD_RADIUS_RATIO = 0.21  # 圆角 = 边长 * 该比例
CARD_GLOSS_ALPHA = 0.20   # 卡片顶部高光（细节档）
CARD_SHADOW_ALPHA = 0.26
CARD_SHADOW_OFFSET = 0.010
CARD_SHADOW_BLUR = 0.016
CARD_SCALE = {"detail": 1.0, "bold": 1.05, "mini": 1.08}   # 小尺寸整体放大，保住"显眼"

BARS_MAX = 20             # ≤ 该尺寸：mini 档
BOLD_MAX = 32             # ≤ 该尺寸：平涂加粗档
SIZES = [256, 128, 96, 64, 48, 40, 32, 24, 20, 16]


def style_for(size: int) -> str:
    if size <= BARS_MAX:
        return "mini"
    return "bold" if size <= BOLD_MAX else "detail"


# ---------------------------------------------------------------- 基础绘制
def _lerp(a: float, b: float, t: float) -> float:
    return a + (b - a) * t


def _mix(c1, c2, t):
    return tuple(int(round(_lerp(c1[i], c2[i], t))) for i in range(3))


def _gradient_color(t: float):
    if t <= GRAD_MID_AT:
        return _mix(GRAD_TOP, GRAD_MID, t / GRAD_MID_AT)
    return _mix(GRAD_MID, GRAD_BOTTOM, (t - GRAD_MID_AT) / (1.0 - GRAD_MID_AT))


def _vertical_gradient(size: int) -> Image.Image:
    grad = Image.new("RGB", (1, size))
    px = grad.load()
    for y in range(size):
        px[0, y] = _gradient_color(y / max(1, size - 1))
    return grad.resize((size, size), Image.Resampling.NEAREST)


def _radial_highlight(size: int, cx: float, cy: float, radius: float, alpha: float) -> Image.Image:
    img = Image.new("L", (size, size), 0)
    px = img.load()
    cx_px, cy_px, r_px = cx * size, cy * size, radius * size
    for y in range(size):
        dy = y - cy_px
        for x in range(size):
            d = math.hypot(x - cx_px, dy) / r_px
            if d < 1.0:
                px[x, y] = int(round(255 * alpha * (1.0 - d) ** 2))
    return img


def _rounded_mask(size: int, box, radius: float) -> Image.Image:
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle(box, radius=radius, fill=255)
    return mask


def _layer(size: int) -> Image.Image:
    return Image.new("RGBA", (size, size), (0, 0, 0, 0))


def _base_tile(work: int) -> Image.Image:
    """底板：鲜艳蓝紫渐变圆角方块 + 左上柔光 + 右下压暗 + 贴边内描边。"""
    img = Image.new("RGBA", (work, work), (0, 0, 0, 0))
    radius = TILE_RADIUS * work
    tile_mask = _rounded_mask(work, (0, 0, work - 1, work - 1), radius)
    img.paste(_vertical_gradient(work), (0, 0), tile_mask)

    hl = Image.composite(_radial_highlight(work, 0.24, 0.12, 0.80, HIGHLIGHT_ALPHA),
                         Image.new("L", (work, work), 0), tile_mask)
    glow = _layer(work)
    glow.paste(Image.new("RGB", (work, work), (255, 255, 255)), (0, 0), hl)
    img.alpha_composite(glow)

    vig = _radial_highlight(work, 0.88, 0.96, 0.75, VIGNETTE_ALPHA)
    dark = _layer(work)
    dark.paste(Image.new("RGB", (work, work), (0, 0, 0)), (0, 0),
               Image.composite(vig, Image.new("L", (work, work), 0), tile_mask))
    img.alpha_composite(dark)

    inset = max(1, int(0.004 * work))
    stroke = _layer(work)
    ImageDraw.Draw(stroke).rounded_rectangle(
        (inset, inset, work - 1 - inset, work - 1 - inset),
        radius=max(1.0, radius - inset),
        outline=(255, 255, 255, int(round(255 * EDGE_ALPHA))),
        width=max(1, int(0.007 * work)),
    )
    img.alpha_composite(stroke)
    return img


def _card(size: int, x: float, y: float, side: float, color, gloss: bool, shadow: bool) -> Image.Image:
    """一张卡片：投影 + 纯色圆角矩形 + 顶部高光（各自独立图层，避免 ImageDraw 覆盖）。"""
    layer = _layer(size)
    box = (x * size, y * size, (x + side) * size, (y + side) * size)
    radius = CARD_RADIUS_RATIO * side * size
    card = _layer(size)
    ImageDraw.Draw(card).rounded_rectangle(box, radius=radius, fill=tuple(color) + (255,))
    if shadow:
        off = int(CARD_SHADOW_OFFSET * size)
        sh = _layer(size)
        sh.paste(Image.new("RGB", (size, size), (0, 0, 0)), (off, off), card.getchannel("A"))
        sh = sh.filter(ImageFilter.GaussianBlur(size * CARD_SHADOW_BLUR))
        sh.putalpha(sh.getchannel("A").point(lambda a: int(a * CARD_SHADOW_ALPHA)))
        layer.alpha_composite(sh)
    layer.alpha_composite(card)
    if gloss:
        g = _layer(size)
        pad = 0.055 * side * size
        top = box[1] + pad
        ImageDraw.Draw(g).rounded_rectangle(
            (box[0] + pad, top, box[2] - pad, top + side * size * 0.26),
            radius=radius * 0.55, fill=(255, 255, 255, int(round(255 * CARD_GLOSS_ALPHA))))
        layer.alpha_composite(g)
    return layer


def render(size: int, style: str | None = None) -> Image.Image:
    """渲染单个尺寸（内部超采样后缩放）。

    构图：左侧一块玻璃面板（屏幕侧边的 Dock），三张鲜艳卡片从右上斜向流进面板，
    前排最大的珊瑚卡压在面板里 —— 既有明确主体，又一眼看出"图标被收进侧边"。
    """
    style = style or style_for(size)
    work = size * SS
    img = _base_tile(work)

    # 侧栏面板
    panel = _layer(work)
    pd = ImageDraw.Draw(panel)
    box = (PANEL_X0 * work, PANEL_Y0 * work, PANEL_X1 * work, PANEL_Y1 * work)
    alpha = PANEL_ALPHA if style == "detail" else PANEL_ALPHA_BOLD
    pd.rounded_rectangle(box, radius=PANEL_RADIUS * work, fill=(255, 255, 255, alpha))
    if style == "detail":
        pd.rounded_rectangle(box, radius=PANEL_RADIUS * work,
                             outline=(255, 255, 255, PANEL_EDGE_ALPHA),
                             width=max(1, int(0.008 * work)))
    img.alpha_composite(panel)

    # 三色卡片：小尺寸整体按画布中心放大，保证 16px 依然显眼
    scale = CARD_SCALE[style]
    for color, x, y, side in CARDS:
        s2 = side * scale
        x2 = 0.5 - (0.5 - x) * scale
        y2 = 0.5 - (0.5 - y) * scale
        img.alpha_composite(_card(work, x2, y2, s2, color,
                                  gloss=(style == "detail"),
                                  shadow=(style == "detail")))
    return img.resize((size, size), Image.Resampling.LANCZOS)


# ---------------------------------------------------------------- ICO 写入
def _bmp_frame(img: Image.Image) -> bytes:
    """32bpp BMP（DIB）帧：BITMAPINFOHEADER + BGRA 倒序行 + 全 0 AND 掩码。"""
    w, h = img.size
    px = img.convert("RGBA").load()
    rows = []
    for y in range(h - 1, -1, -1):          # DIB 自下而上
        row = bytearray()
        for x in range(w):
            r, g, b, a = px[x, y]
            row += bytes((b, g, r, a))
        rows.append(bytes(row))
    xor = b"".join(rows)
    mask_row = ((w + 31) // 32) * 4
    and_mask = bytes(mask_row * h)
    header = struct.pack("<IiiHHIIiiII", 40, w, h * 2, 1, 32, 0, len(xor) + len(and_mask), 0, 0, 0, 0)
    return header + xor + and_mask


def _png_frame(img: Image.Image) -> bytes:
    import io
    buf = io.BytesIO()
    img.save(buf, format="PNG", optimize=True)
    return buf.getvalue()


def write_ico(path: Path, frames: list[Image.Image]) -> None:
    # 大尺寸用 PNG 压缩（Vista+ 支持，省下大半体积），小尺寸用 32bpp BMP：
    # 16~48px 是资源管理器/托盘最常取用的帧，BMP 兼容性最好。
    png_min = 96
    entries, payloads = [], []
    offset = 6 + 16 * len(frames)
    for img in frames:
        size = img.size[0]
        use_png = size >= png_min
        data = _png_frame(img) if use_png else _bmp_frame(img)
        entries.append(struct.pack("<BBBBHHII",
                                   0 if size >= 256 else size,
                                   0 if size >= 256 else size,
                                   0, 0, 1, 32, len(data), offset))
        payloads.append(data)
        offset += len(data)
    blob = struct.pack("<HHH", 0, 1, len(frames)) + b"".join(entries) + b"".join(payloads)
    path.write_bytes(blob)


# ---------------------------------------------------------------- 入口
def _context_sheet(rendered: dict) -> Image.Image:
    """深色 / 浅色底上的实际观感（托盘、任务栏、开始菜单都用得到）。"""
    sizes = [16, 20, 24, 32, 48, 128]
    rows = []
    for bg in ((0x20, 0x21, 0x24, 255), (0xF3, 0xF4, 0xF6, 255)):
        w = sum(sizes) + 16 * (len(sizes) + 1)
        h = max(sizes) + 32
        row = Image.new("RGBA", (w, h), bg)
        x = 16
        for s in sizes:
            row.alpha_composite(rendered[s], (x, 16 + (max(sizes) - s) // 2))
            x += s + 16
        rows.append(row)
    W = max(r.size[0] for r in rows)
    H = sum(r.size[1] for r in rows)
    sheet = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    y = 0
    for r in rows:
        sheet.alpha_composite(r, (0, y))
        y += r.size[1]
    return sheet.convert("RGB")


def main() -> int:
    ap = argparse.ArgumentParser(description="生成 SideStack 的鲜艳几何风格 app.ico")
    ap.add_argument("--preview-only", action="store_true", help="只输出预览 PNG，不覆盖 app.ico")
    args = ap.parse_args()

    rendered = {s: render(s) for s in SIZES}

    PREVIEW_DIR.mkdir(exist_ok=True)
    rendered[256].save(PREVIEW_DIR / "icon-256.png")
    rendered[48].save(PREVIEW_DIR / "icon-48.png")
    rendered[32].save(PREVIEW_DIR / "icon-32.png")
    rendered[16].save(PREVIEW_DIR / "icon-16.png")
    # 一览图：256 / 48 / 32 / 16 并排（16px 放大 8 倍便于观察）
    sheet = Image.new("RGBA", (256 + 48 + 32 + 16 * 8 + 60, 256), (245, 246, 248, 255))
    x = 10
    for s, zoom in ((256, 1), (48, 1), (32, 1), (16, 8)):
        im = rendered[s]
        if zoom > 1:
            im = im.resize((s * zoom, s * zoom), Image.Resampling.NEAREST)
        sheet.alpha_composite(im, (x, 10 + (256 - im.size[1]) // 2))
        x += im.size[0] + 10
    sheet.convert("RGB").save(PREVIEW_DIR / "sheet.png")
    _context_sheet(rendered).save(PREVIEW_DIR / "context.png")

    print("预览已写入:", PREVIEW_DIR)
    if args.preview_only:
        return 0

    if ICON.exists():
        shutil.copy2(ICON, ICON.with_suffix(".ico.bak"))
        print("已备份原图标 ->", ICON.with_suffix(".ico.bak").name)
    write_ico(ICON, [rendered[s] for s in SIZES])
    print("已生成 %s（%d 个尺寸: %s）" % (ICON.name, len(SIZES), ", ".join(str(s) for s in SIZES)))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
