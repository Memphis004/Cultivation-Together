"""Build a contact sheet; labels/crosshairs are review-only, never final art."""
import argparse
from pathlib import Path
from PIL import Image, ImageDraw


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', required=True)
    parser.add_argument('--columns', type=int, default=0, help='0 places every candidate in one row')
    parser.add_argument('candidates', nargs='+', help='SEED=path or DISCIPLE:SEED=path')
    args = parser.parse_args()
    width, height, header = 384, 576, 40
    columns = args.columns or len(args.candidates)
    if columns < 1:
        parser.error('--columns must be positive')
    rows = (len(args.candidates) + columns - 1) // columns
    sheet = Image.new('RGB', (width * columns, (height + header) * rows), '#202020')
    draw = ImageDraw.Draw(sheet)
    for index, candidate in enumerate(args.candidates):
        label, source = candidate.split('=', 1)
        image = Image.open(source).convert('RGB')
        if image.size != (1024, 1536):
            raise ValueError(f'{source}: expected 1024x1536, got {image.size}')
        left = (index % columns) * width
        top = (index // columns) * (height + header)
        sheet.paste(image.resize((width, height), Image.Resampling.LANCZOS), (left, top + header))
        x, y = left + width * 0.5, top + header + height * (488 / 1536)
        draw.line((x - 20, y, x + 20, y), fill='#00FFFF', width=2)
        draw.line((x, y - 20, x, y + 20), fill='#00FFFF', width=2)
        draw.ellipse((x - 5, y - 5, x + 5, y + 5), outline='#FFFF00', width=2)
        disciple, seed = label.split(':', 1) if ':' in label else ('d000', label)
        draw.text((left + 10, top + 6), f'{disciple} | seed {seed}', fill='white')
        draw.text((left + 10, top + 22), 'Target head: x=50%, top=31.7708%', fill='#00FFFF')
    output = Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(output)
    print(f'{output}: {sheet.width}x{sheet.height}; review overlay only')


if __name__ == '__main__':
    main()
