using GuildMaster.Core;
using NUnit.Framework;
using UnityEngine;

namespace GuildMaster.Tests
{
    /// <summary>BC1 compression of the runtime-painted ground (P4-81).</summary>
    public class Bc1EncoderTests
    {
        static Color32 C(int r, int g, int b) => new((byte)r, (byte)g, (byte)b, 255);

        static int MaxError(Color32[] a, Color32[] b)
        {
            int worst = 0;
            for (int i = 0; i < a.Length; i++)
                worst = Mathf.Max(worst, Mathf.Abs(a[i].r - b[i].r), Mathf.Abs(a[i].g - b[i].g), Mathf.Abs(a[i].b - b[i].b));
            return worst;
        }

        static Color32[] RoundTrip(Color32[] pixels, int w, int h)
        {
            var data = new byte[Bc1Encoder.EncodedSize(w, h)];
            Bc1Encoder.Encode(pixels, w, h, data);
            return Bc1Encoder.Decode(data, w, h);
        }

        [Test]
        public void FlatGround_AndAFillWithAnOutline_ComeThroughAlmostExact()
        {
            // The painted ground is flat colours with dark outlines: two colours per block at most, mostly.
            const int w = 16, h = 16;
            var grass = C(0x7C, 0xCF, 0x4F);
            var outline = C(0x2C, 0x5C, 0x93);
            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                pixels[y * w + x] = (x + y) % 7 == 0 ? outline : grass;
            // Only the 5:6:5 rounding is lost (about 4 levels of 255 in red and blue, 2 in green).
            Assert.That(MaxError(pixels, RoundTrip(pixels, w, h)), Is.LessThanOrEqualTo(5));
        }

        [Test]
        public void ABlendBetweenTwoColours_StaysClose()
        {
            // The painter's anti-aliased edges blend two colours: one line of colours, which BC1 holds well.
            const int w = 32, h = 8;
            var a = C(0x7C, 0xCF, 0x4F);
            var b = C(0xF2, 0xD8, 0x8F);
            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                pixels[y * w + x] = Color32.Lerp(a, b, x / (w - 1f));
            Assert.That(MaxError(pixels, RoundTrip(pixels, w, h)), Is.LessThanOrEqualTo(8));
        }

        [Test]
        public void ATwoWayGradient_StaysWithinBc1sLimit()
        {
            // Colours changing two ways in one block do not lie on one line: BC1's own limit, rare on this ground.
            const int w = 32, h = 32;
            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                pixels[y * w + x] = C(x * 8, y * 8, 255 - x * 4);
            Assert.That(MaxError(pixels, RoundTrip(pixels, w, h)), Is.LessThanOrEqualTo(24));
        }

        [Test]
        public void BlocksStayWhereTheyWere_BottomRowFirst()
        {
            // A dark bottom-left block and a light top-right one: rows and blocks keep their place.
            const int w = 8, h = 8;
            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                pixels[y * w + x] = x < 4 && y < 4 ? C(10, 20, 30) : x >= 4 && y >= 4 ? C(240, 230, 220) : C(120, 120, 120);
            var back = RoundTrip(pixels, w, h);
            Assert.That(back[0].r, Is.LessThan(40), "bottom-left");
            Assert.That(back[w * h - 1].r, Is.GreaterThan(200), "top-right");
            Assert.That(back[w - 1].r, Is.InRange(100, 140), "bottom-right");
        }

        [Test]
        public void UnityReadsTheDataTheSameWayUp()
        {
            // What the GPU shows (Unity decompresses on read): the encoder's row order is Unity's.
            const int w = 8, h = 8;
            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                pixels[y * w + x] = C(y * 30, x * 30, 128);
            var data = new byte[Bc1Encoder.EncodedSize(w, h)];
            Bc1Encoder.Encode(pixels, w, h, data);
            var tex = new Texture2D(w, h, TextureFormat.DXT1, false);
            try
            {
                tex.LoadRawTextureData(data);
                tex.Apply(false, false);
                var shown = tex.GetPixels32();
                var decoded = Bc1Encoder.Decode(data, w, h);
                Assert.That(MaxError(shown, decoded), Is.LessThanOrEqualTo(2));
                Assert.That(shown[0].r, Is.LessThan(shown[w * (h - 1)].r), "row 0 is the bottom");
            }
            finally
            {
                Object.DestroyImmediate(tex);
            }
        }
    }
}
