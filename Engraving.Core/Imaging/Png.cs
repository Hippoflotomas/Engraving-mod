using System;
using System.IO;
using System.IO.Compression;
using Engraving.Core.Geometry;

namespace Engraving.Core.Imaging
{
    /// <summary>
    /// Minimal PNG codec with no dependencies (works on net48 / Unity Mono and outside the game).
    /// Decodes non-interlaced PNGs of every colour type and bit depth; encodes 8-bit RGB and 16-bit grey.
    /// </summary>
    public static class Png
    {
        static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

        /// <summary>Decodes to RGBA floats (0..1), row 0 at top.</summary>
        public static float[] DecodeRgba(byte[] data, out int width, out int height)
        {
            for (int i = 0; i < 8; i++)
                if (data[i] != Signature[i]) throw new InvalidDataException("not a PNG file");

            int pos = 8;
            width = height = 0;
            int depth = 0, colorType = 0, interlace = 0;
            byte[] palette = null, trns = null;
            var idat = new MemoryStream();
            while (pos + 8 <= data.Length)
            {
                int len = ReadInt(data, pos);
                string type = System.Text.Encoding.ASCII.GetString(data, pos + 4, 4);
                int body = pos + 8;
                switch (type)
                {
                    case "IHDR":
                        width = ReadInt(data, body);
                        height = ReadInt(data, body + 4);
                        depth = data[body + 8];
                        colorType = data[body + 9];
                        interlace = data[body + 12];
                        break;
                    case "PLTE":
                        palette = new byte[len];
                        Array.Copy(data, body, palette, 0, len);
                        break;
                    case "tRNS":
                        trns = new byte[len];
                        Array.Copy(data, body, trns, 0, len);
                        break;
                    case "IDAT":
                        idat.Write(data, body, len);
                        break;
                }
                pos = body + len + 4;
                if (type == "IEND") break;
            }
            if (interlace != 0) throw new NotSupportedException("interlaced PNGs are not supported; re-save without interlacing");

            int channels = colorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => throw new InvalidDataException("bad PNG colour type") };
            int bitsPerPixel = channels * depth;
            int stride = (width * bitsPerPixel + 7) / 8;
            int bpp = Math.Max(1, bitsPerPixel / 8);

            byte[] raw = Inflate(idat.ToArray());
            var img = new byte[stride * height];
            var prev = new byte[stride];
            var line = new byte[stride];
            int rp = 0;
            for (int y = 0; y < height; y++)
            {
                int filter = raw[rp++];
                Array.Copy(raw, rp, line, 0, stride);
                rp += stride;
                Unfilter(filter, line, prev, bpp);
                Array.Copy(line, 0, img, y * stride, stride);
                var t = prev; prev = line; line = t;
            }

            var rgba = new float[width * height * 4];
            float maxv = (1 << depth) - 1;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    float r, g, b, a = 1;
                    int o = (y * width + x) * 4;
                    switch (colorType)
                    {
                        case 0:
                        {
                            int v = Sample(img, y * stride, x, depth);
                            r = g = b = v / maxv;
                            if (trns != null && trns.Length >= 2 && v == ((trns[0] << 8) | trns[1])) a = 0;
                            break;
                        }
                        case 3:
                        {
                            int idx = Sample(img, y * stride, x, depth);
                            r = palette[idx * 3] / 255f; g = palette[idx * 3 + 1] / 255f; b = palette[idx * 3 + 2] / 255f;
                            if (trns != null && idx < trns.Length) a = trns[idx] / 255f;
                            break;
                        }
                        default:
                        {
                            float[] c = new float[channels];
                            for (int k = 0; k < channels; k++) c[k] = Sample(img, y * stride, x * channels + k, depth) / maxv;
                            if (colorType == 2) { r = c[0]; g = c[1]; b = c[2]; }
                            else if (colorType == 4) { r = g = b = c[0]; a = c[1]; }
                            else { r = c[0]; g = c[1]; b = c[2]; a = c[3]; }
                            break;
                        }
                    }
                    rgba[o] = r; rgba[o + 1] = g; rgba[o + 2] = b; rgba[o + 3] = a;
                }
            return rgba;
        }

        /// <summary>
        /// Loads a charge silhouette. Transparent images use alpha; fully opaque images are treated as
        /// dark-on-light artwork (black = charge), which suits scanned or hand-drawn engravings.
        /// </summary>
        public static AlphaMask LoadMask(byte[] png)
        {
            var rgba = DecodeRgba(png, out int w, out int h);
            var m = new AlphaMask(w, h) { Luma = new float[w * h] };
            bool anyTransparent = false;
            for (int i = 0; i < w * h; i++) if (rgba[i * 4 + 3] < 0.99f) { anyTransparent = true; break; }
            for (int i = 0; i < w * h; i++)
            {
                float l = 0.299f * rgba[i * 4] + 0.587f * rgba[i * 4 + 1] + 0.114f * rgba[i * 4 + 2];
                m.Luma[i] = l;
                m.Alpha[i] = anyTransparent ? rgba[i * 4 + 3] : 1f - l;
            }
            return m;
        }

        static int Sample(byte[] img, int rowStart, int index, int depth)
        {
            switch (depth)
            {
                case 8: return img[rowStart + index];
                case 16: return (img[rowStart + index * 2] << 8) | img[rowStart + index * 2 + 1];
                default:
                {
                    int bit = index * depth;
                    int b = img[rowStart + bit / 8];
                    int shift = 8 - depth - bit % 8;
                    return (b >> shift) & ((1 << depth) - 1);
                }
            }
        }

        static void Unfilter(int filter, byte[] line, byte[] prev, int bpp)
        {
            for (int i = 0; i < line.Length; i++)
            {
                int a = i >= bpp ? line[i - bpp] : 0;
                int b = prev[i];
                int c = i >= bpp ? prev[i - bpp] : 0;
                int add;
                switch (filter)
                {
                    case 0: add = 0; break;
                    case 1: add = a; break;
                    case 2: add = b; break;
                    case 3: add = (a + b) >> 1; break;
                    case 4:
                    {
                        int p = a + b - c;
                        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
                        add = pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
                        break;
                    }
                    default: throw new InvalidDataException("bad PNG filter");
                }
                line[i] = (byte)(line[i] + add);
            }
        }

        static byte[] Inflate(byte[] zlib)
        {
            using (var src = new MemoryStream(zlib, 2, zlib.Length - 2)) // skip zlib header
            using (var ds = new DeflateStream(src, CompressionMode.Decompress))
            using (var dst = new MemoryStream())
            {
                ds.CopyTo(dst);
                return dst.ToArray();
            }
        }

        static int ReadInt(byte[] d, int p) => (d[p] << 24) | (d[p + 1] << 16) | (d[p + 2] << 8) | d[p + 3];

        // ------------------------------------------------------------------ encoding

        /// <summary>16-bit greyscale, values 0..1, row 0 at top. Use for heightmaps.</summary>
        public static byte[] EncodeGray16(float[] v, int w, int h)
        {
            var raw = new byte[(w * 2 + 1) * h];
            int p = 0;
            for (int y = 0; y < h; y++)
            {
                raw[p++] = 0;
                for (int x = 0; x < w; x++)
                {
                    int q = (int)Math.Round(M.Clamp01(v[y * w + x]) * 65535);
                    raw[p++] = (byte)(q >> 8);
                    raw[p++] = (byte)q;
                }
            }
            return Encode(raw, w, h, 16, 0);
        }

        /// <summary>8-bit RGB from interleaved floats 0..1, row 0 at top.</summary>
        public static byte[] EncodeRgb8(float[] rgb, int w, int h)
        {
            var raw = new byte[(w * 3 + 1) * h];
            int p = 0;
            for (int y = 0; y < h; y++)
            {
                raw[p++] = 0;
                for (int i = 0; i < w * 3; i++)
                    raw[p++] = (byte)Math.Round(M.Clamp01(rgb[y * w * 3 + i]) * 255);
            }
            return Encode(raw, w, h, 8, 2);
        }

        static byte[] Encode(byte[] raw, int w, int h, int depth, int colorType)
        {
            using (var ms = new MemoryStream())
            {
                ms.Write(Signature, 0, 8);
                var ihdr = new byte[13];
                WriteInt(ihdr, 0, w); WriteInt(ihdr, 4, h);
                ihdr[8] = (byte)depth; ihdr[9] = (byte)colorType;
                Chunk(ms, "IHDR", ihdr);

                byte[] deflated;
                using (var z = new MemoryStream())
                {
                    using (var ds = new DeflateStream(z, CompressionLevel.Optimal, true)) ds.Write(raw, 0, raw.Length);
                    deflated = z.ToArray();
                }
                uint adler = Adler32(raw);
                var zlib = new byte[deflated.Length + 6];
                zlib[0] = 0x78; zlib[1] = 0x9C;
                Array.Copy(deflated, 0, zlib, 2, deflated.Length);
                WriteInt(zlib, zlib.Length - 4, (int)adler);
                Chunk(ms, "IDAT", zlib);
                Chunk(ms, "IEND", new byte[0]);
                return ms.ToArray();
            }
        }

        static void Chunk(Stream s, string type, byte[] body)
        {
            var len = new byte[4];
            WriteInt(len, 0, body.Length);
            s.Write(len, 0, 4);
            var tb = System.Text.Encoding.ASCII.GetBytes(type);
            s.Write(tb, 0, 4);
            s.Write(body, 0, body.Length);
            uint crc = Crc(tb, 0xFFFFFFFFu);
            crc = Crc(body, crc) ^ 0xFFFFFFFFu;
            var cb = new byte[4];
            WriteInt(cb, 0, (int)crc);
            s.Write(cb, 0, 4);
        }

        static void WriteInt(byte[] d, int p, int v)
        {
            d[p] = (byte)(v >> 24); d[p + 1] = (byte)(v >> 16); d[p + 2] = (byte)(v >> 8); d[p + 3] = (byte)v;
        }

        static uint[] _crcTable;
        static uint Crc(byte[] buf, uint crc)
        {
            if (_crcTable == null)
            {
                _crcTable = new uint[256];
                for (uint n = 0; n < 256; n++)
                {
                    uint c = n;
                    for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                    _crcTable[n] = c;
                }
            }
            foreach (byte b in buf) crc = _crcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc;
        }

        static uint Adler32(byte[] d)
        {
            uint a = 1, b = 0;
            foreach (byte x in d) { a = (a + x) % 65521; b = (b + a) % 65521; }
            return (b << 16) | a;
        }
    }
}
