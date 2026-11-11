import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { inflateSync } from 'node:zlib';

// HANDOFF 3.1 and story 2.6: the hosted pages' logo is two PNGs, 620 x 128, 8-bit RGBA, each at most 512 KiB, with no
// text or EXIF chunk. The dot follows the name. The PNG layout is the W3C PNG Specification (https://www.w3.org/TR/png-3/):
// an 8-byte signature, then chunks of length, type, data and CRC; IDAT holds the zlib-compressed scanlines, each
// starting with a filter byte (section 9, Filtering).

type Png = {
  width: number;
  height: number;
  depth: number;
  colourType: number;
  chunkTypes: string[];
  byteLength: number;
  pixels: Uint8Array;
};

function readPng(fileName: string): Png {
  const bytes = readFileSync(join(import.meta.dirname, fileName));
  const chunkTypes: string[] = [];
  const compressed: Buffer[] = [];
  let width = 0;
  let height = 0;
  let depth = 0;
  let colourType = 0;
  for (let offset = 8; offset < bytes.length;) {
    const length = bytes.readUInt32BE(offset);
    const type = bytes.toString('latin1', offset + 4, offset + 8);
    const data = bytes.subarray(offset + 8, offset + 8 + length);
    chunkTypes.push(type);
    if (type === 'IHDR') {
      width = data.readUInt32BE(0);
      height = data.readUInt32BE(4);
      depth = data.readUInt8(8);
      colourType = data.readUInt8(9);
    }
    if (type === 'IDAT') compressed.push(data);
    offset += 12 + length;
  }

  // 8-bit RGBA: 4 bytes per pixel.
  const bytesPerPixel = 4;
  const stride = width * bytesPerPixel;
  const scanlines = inflateSync(Buffer.concat(compressed));
  const pixels = new Uint8Array(height * stride);
  for (let row = 0; row < height; row++) {
    const filter = scanlines.readUInt8(row * (stride + 1));
    for (let column = 0; column < stride; column++) {
      const filtered = scanlines.readUInt8(row * (stride + 1) + 1 + column);
      const left =
        column >= bytesPerPixel ? (pixels[row * stride + column - bytesPerPixel] ?? 0) : 0;
      const up = row > 0 ? (pixels[(row - 1) * stride + column] ?? 0) : 0;
      const upLeft =
        column >= bytesPerPixel && row > 0
          ? (pixels[(row - 1) * stride + column - bytesPerPixel] ?? 0)
          : 0;
      pixels[row * stride + column] = (filtered + predict(filter, left, up, upLeft)) & 0xff;
    }
  }
  return { width, height, depth, colourType, chunkTypes, byteLength: bytes.length, pixels };
}

function predict(filter: number, left: number, up: number, upLeft: number): number {
  switch (filter) {
    case 0:
      return 0;
    case 1:
      return left;
    case 2:
      return up;
    case 3:
      return (left + up) >> 1;
    default: {
      // Paeth predictor
      const estimate = left + up - upLeft;
      const distanceLeft = Math.abs(estimate - left);
      const distanceUp = Math.abs(estimate - up);
      const distanceUpLeft = Math.abs(estimate - upLeft);
      if (distanceLeft <= distanceUp && distanceLeft <= distanceUpLeft) return left;
      return distanceUp <= distanceUpLeft ? up : upLeft;
    }
  }
}

/**
 * The box around the fully opaque pixels of exactly this colour: the antialiased edge pixels are left out. The
 * optional columns limit it to the pixels between those x positions, both included.
 */
function boxOf(
  png: Png,
  colour: [number, number, number],
  columns = { from: 0, to: png.width - 1 },
) {
  let left = Infinity;
  let top = Infinity;
  let right = -1;
  let bottom = -1;
  for (let y = 0; y < png.height; y++) {
    for (let x = columns.from; x <= columns.to; x++) {
      const at = (y * png.width + x) * 4;
      const matches =
        png.pixels[at] === colour[0] &&
        png.pixels[at + 1] === colour[1] &&
        png.pixels[at + 2] === colour[2] &&
        png.pixels[at + 3] === 255;
      if (!matches) continue;
      left = Math.min(left, x);
      right = Math.max(right, x);
      top = Math.min(top, y);
      bottom = Math.max(bottom, y);
    }
  }
  return { left, top, right, bottom };
}

/** The box of the name's first letter, D, which has no descender: from the name's left edge to its first empty column. */
function firstLetterBox(png: Png, name: [number, number, number]) {
  const nameBox = boxOf(png, name);
  let to = nameBox.left;
  while (boxOf(png, name, { from: to + 1, to: to + 1 }).right >= 0) to++;
  return boxOf(png, name, { from: nameBox.left, to });
}

const themes = [
  { file: 'wordmark.png', name: [0x14, 0x28, 0x7a], dot: [0x2b, 0x57, 0xc9] },
  { file: 'wordmark-dark.png', name: [0xc9, 0xd6, 0xff], dot: [0x8c, 0xaa, 0xf7] },
] as const;

describe.each(themes)('$file', ({ file, name, dot }) => {
  const png = readPng(file);

  it('is 620 x 128, 8-bit RGBA, at most 512 KiB', () => {
    expect([png.width, png.height, png.depth, png.colourType]).toEqual([620, 128, 8, 6]);
    expect(png.byteLength).toBeLessThanOrEqual(512 * 1024);
  });

  it('has no text or EXIF metadata chunk', () => {
    const metadata = png.chunkTypes.filter((type) =>
      ['tEXt', 'zTXt', 'iTXt', 'eXIf'].includes(type),
    );
    expect(metadata).toEqual([]);
  });

  it('is transparent around the drawing, in all four corners', () => {
    const alphaAt = (x: number, y: number) => png.pixels[(y * png.width + x) * 4 + 3];

    expect([
      alphaAt(0, 0),
      alphaAt(png.width - 1, 0),
      alphaAt(0, png.height - 1),
      alphaAt(png.width - 1, png.height - 1),
    ]).toEqual([0, 0, 0, 0]);
  });

  it('puts the dot after the name, 32px across, whole within the canvas', () => {
    const nameBox = boxOf(png, [...name]);
    const dotBox = boxOf(png, [...dot]);

    expect(nameBox.right).toBeGreaterThan(0);
    expect(dotBox.left).toBeGreaterThan(nameBox.right);
    // The fully opaque core of a 32px dot is a little under 32px, because its edge is antialiased.
    expect(dotBox.right - dotBox.left + 1).toBeGreaterThanOrEqual(28);
    expect(dotBox.right - dotBox.left + 1).toBeLessThanOrEqual(32);
    expect(dotBox.bottom - dotBox.top + 1).toBeGreaterThanOrEqual(28);
    expect(dotBox.right).toBeLessThan(png.width);
    // Like a full stop: its foot is on the baseline, within 2px for the antialiased edge. The baseline is the foot of
    // D, which has no descender (the name's box as a whole includes the descender of "p", 14px lower).
    const baseline = firstLetterBox(png, [...name]).bottom;
    expect(dotBox.bottom).toBeLessThanOrEqual(baseline);
    expect(dotBox.bottom).toBeGreaterThanOrEqual(baseline - 2);
    expect(dotBox.top).toBeGreaterThan(nameBox.top);
  });
});
