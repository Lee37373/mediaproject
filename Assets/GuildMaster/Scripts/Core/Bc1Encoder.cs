using UnityEngine;

namespace GuildMaster.Core
{
    /// <summary>
    /// BC1 (DXT1) compression of opaque pixels, for textures painted at runtime (P4-81: the 512 map's
    /// ground is 1 GB as RGBA32, 128 MB as BC1). Each 4×4 block keeps two 5:6:5 end colours and picks
    /// one of four colours on the line between them per pixel. End colours are the corners of the
    /// block's colour bounding box on the diagonal the colours follow — cartoon ground (a fill and an
    /// outline per block) comes through exact but for the 5:6:5 rounding. Pure and thread-safe.
    ///
    /// Rows run as Unity lays out texture data: the first row is the bottom one, in the pixels and in
    /// each block alike, so the output loads with <see cref="Texture2D.LoadRawTextureData(byte[])"/>
    /// into a <see cref="TextureFormat.DXT1"/> texture the same way up as <see cref="Texture2D.SetPixels32(Color32[])"/>.
    /// </summary>
    public static class Bc1Encoder
    {
        const int BlockBytes = 8;

        /// <summary>Bytes of BC1 data for an image this size (both multiples of 4).</summary>
        public static int EncodedSize(int width, int height) => width / 4 * (height / 4) * BlockBytes;

        /// <summary>
        /// Compresses <paramref name="width"/>×<paramref name="height"/> pixels (row-major, bottom row
        /// first; both sizes multiples of 4) into <paramref name="output"/> (at least <see cref="EncodedSize"/> bytes).
        /// </summary>
        public static void Encode(Color32[] pixels, int width, int height, byte[] output)
        {
            var r = new int[16];
            var g = new int[16];
            var b = new int[16];
            int o = 0;
            for (int by = 0; by < height; by += 4)
            for (int bx = 0; bx < width; bx += 4)
            {
                for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                {
                    var c = pixels[(by + y) * width + bx + x];
                    int k = y * 4 + x;
                    r[k] = c.r; g[k] = c.g; b[k] = c.b;
                }
                EncodeBlock(r, g, b, output, o);
                o += BlockBytes;
            }
        }

        static void EncodeBlock(int[] r, int[] g, int[] b, byte[] output, int o)
        {
            int rMin = 255, gMin = 255, bMin = 255, rMax = 0, gMax = 0, bMax = 0, rSum = 0, gSum = 0, bSum = 0;
            for (int i = 0; i < 16; i++)
            {
                if (r[i] < rMin) rMin = r[i]; if (r[i] > rMax) rMax = r[i];
                if (g[i] < gMin) gMin = g[i]; if (g[i] > gMax) gMax = g[i];
                if (b[i] < bMin) bMin = b[i]; if (b[i] > bMax) bMax = b[i];
                rSum += r[i]; gSum += g[i]; bSum += b[i];
            }
            // Which diagonal of the box the colours follow: red and blue against green.
            int covRg = 0, covBg = 0;
            for (int i = 0; i < 16; i++)
            {
                int dg = g[i] * 16 - gSum;
                covRg += (r[i] * 16 - rSum) * dg;
                covBg += (b[i] * 16 - bSum) * dg;
            }
            // No inset: a block of two colours (a fill and its outline) keeps both exactly.
            if (covRg < 0) (rMin, rMax) = (rMax, rMin);
            if (covBg < 0) (bMin, bMax) = (bMax, bMin);

            int c0 = To565(rMax, gMax, bMax), c1 = To565(rMin, gMin, bMin);
            if (c0 < c1) (c0, c1) = (c1, c0);
            uint indices = 0;
            if (c0 != c1)
            {
                // Four-colour mode (c0 > c1): c0, c1, 2/3 c0 + 1/3 c1, 1/3 c0 + 2/3 c1.
                Expand(c0, out int r0, out int g0, out int b0);
                Expand(c1, out int r1, out int g1, out int b1);
                int r2 = (2 * r0 + r1) / 3, g2 = (2 * g0 + g1) / 3, b2 = (2 * b0 + b1) / 3;
                int r3 = (r0 + 2 * r1) / 3, g3 = (g0 + 2 * g1) / 3, b3 = (b0 + 2 * b1) / 3;
                for (int i = 0; i < 16; i++)
                {
                    int best = 0, bestD = Dist(r[i], g[i], b[i], r0, g0, b0);
                    int d = Dist(r[i], g[i], b[i], r1, g1, b1); if (d < bestD) { bestD = d; best = 1; }
                    d = Dist(r[i], g[i], b[i], r2, g2, b2); if (d < bestD) { bestD = d; best = 2; }
                    d = Dist(r[i], g[i], b[i], r3, g3, b3); if (d < bestD) best = 3;
                    indices |= (uint)best << (2 * i);
                }
            }
            // c0 == c1: every pixel takes c0 (index 0), so the three-colour mode it implies never shows.
            output[o] = (byte)c0; output[o + 1] = (byte)(c0 >> 8);
            output[o + 2] = (byte)c1; output[o + 3] = (byte)(c1 >> 8);
            output[o + 4] = (byte)indices; output[o + 5] = (byte)(indices >> 8);
            output[o + 6] = (byte)(indices >> 16); output[o + 7] = (byte)(indices >> 24);
        }

        static int Dist(int r, int g, int b, int r2, int g2, int b2)
        {
            int dr = r - r2, dg = g - g2, db = b - b2;
            return dr * dr + dg * dg + db * db;
        }

        static int To565(int r, int g, int b) => ((r * 31 + 127) / 255 << 11) | ((g * 63 + 127) / 255 << 5) | (b * 31 + 127) / 255;

        static void Expand(int c, out int r, out int g, out int b)
        {
            int r5 = (c >> 11) & 31, g6 = (c >> 5) & 63, b5 = c & 31;
            r = (r5 << 3) | (r5 >> 2);
            g = (g6 << 2) | (g6 >> 4);
            b = (b5 << 3) | (b5 >> 2);
        }

        /// <summary>Decompresses BC1 data back to pixels (tests: what the GPU will show).</summary>
        public static Color32[] Decode(byte[] data, int width, int height)
        {
            var pixels = new Color32[width * height];
            var pr = new int[4];
            var pg = new int[4];
            var pb = new int[4];
            int o = 0;
            for (int by = 0; by < height; by += 4)
            for (int bx = 0; bx < width; bx += 4)
            {
                int c0 = data[o] | data[o + 1] << 8, c1 = data[o + 2] | data[o + 3] << 8;
                uint indices = (uint)(data[o + 4] | data[o + 5] << 8 | data[o + 6] << 16 | data[o + 7] << 24);
                Expand(c0, out pr[0], out pg[0], out pb[0]);
                Expand(c1, out pr[1], out pg[1], out pb[1]);
                if (c0 > c1)
                {
                    pr[2] = (2 * pr[0] + pr[1]) / 3; pg[2] = (2 * pg[0] + pg[1]) / 3; pb[2] = (2 * pb[0] + pb[1]) / 3;
                    pr[3] = (pr[0] + 2 * pr[1]) / 3; pg[3] = (pg[0] + 2 * pg[1]) / 3; pb[3] = (pb[0] + 2 * pb[1]) / 3;
                }
                else
                {
                    pr[2] = (pr[0] + pr[1]) / 2; pg[2] = (pg[0] + pg[1]) / 2; pb[2] = (pb[0] + pb[1]) / 2;
                    pr[3] = pg[3] = pb[3] = 0;
                }
                for (int i = 0; i < 16; i++)
                {
                    int k = (int)(indices >> (2 * i)) & 3;
                    pixels[(by + i / 4) * width + bx + i % 4] = new Color32((byte)pr[k], (byte)pg[k], (byte)pb[k], 255);
                }
                o += BlockBytes;
            }
            return pixels;
        }
    }
}
