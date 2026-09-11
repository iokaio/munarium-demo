# SPDX-License-Identifier: Apache-2.0
"""Render captured native worker status, without invented application output."""
from pathlib import Path
import sys
import textwrap
from PIL import Image, ImageDraw, ImageFont

lines = [part for line in Path(sys.argv[1]).read_text().splitlines() for part in (textwrap.wrap(line, 102) or [""])]
image = Image.new("RGB", (1360, max(480, 90 + 28 * len(lines))), "#142332")
draw = ImageDraw.Draw(image)
font = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf", 20)
for number, line in enumerate(lines):
    draw.text((30, 35 + 28 * number), line, font=font, fill="#72e0bb" if number == 0 or "State:" in line else "#e1eaf0")
image.save(sys.argv[2])
