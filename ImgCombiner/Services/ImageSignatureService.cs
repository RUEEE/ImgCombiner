using ImageMagick;
using ImgCombiner.Services;
using System.Numerics;

namespace ImgCombiner.Services;

public sealed class ImageSignatureService : IImageSignatureService
{
    public ImageMeta ReadMetaFast(string path)
    {
        var info = new MagickImageInfo(path); // 类似 ping：只读元信息
        var w = info.Width;
        var h = info.Height;
        var ratio = h == 0 ? 0 : (double)w / h;
        return new ImageMeta((int)w, (int)h, ratio);
    }

    public byte[] Compute4x4Rgb(string path)
    {
        using var image = new MagickImage(path);
        image.FilterType = FilterType.Box;
        image.Resize(new MagickGeometry(4, 4) { IgnoreAspectRatio = true });

        var pixels = image.GetPixels();
        var vec = new byte[16 * 3];
        int idx = 0;

        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
            {
                var p = pixels.GetPixel(x, y);
                vec[idx++] = (byte)p.GetChannel(0); // R
                vec[idx++] = (byte)p.GetChannel(1); // G
                vec[idx++] = (byte)p.GetChannel(2); // B
            }

        return vec;
    }

    public byte[] Compute2x2Rgb(string path)
    {
        using var image = new MagickImage(path);
        image.FilterType = FilterType.Box;
        image.Resize(new MagickGeometry(2, 2) { IgnoreAspectRatio = true });

        var pixels = image.GetPixels();
        var vec = new byte[4 * 3];
        int idx = 0;

        for (int y = 0; y < 2; y++)
            for (int x = 0; x < 2; x++)
            {
                var p = pixels.GetPixel(x, y);
                vec[idx++] = (byte)p.GetChannel(0); // R
                vec[idx++] = (byte)p.GetChannel(1); // G
                vec[idx++] = (byte)p.GetChannel(2); // B
            }
        return vec;
    }


    public int DistanceManhattan(byte[] a, byte[] b)
    {
        int sum = 0;
        for (int i = 0; i < a.Length; i++)
            sum += Math.Abs(a[i] - b[i]);
        return sum;
    }

    // dHash: resize to 17x16 gray, compare horizontal adjacent pixels -> 16*16 bits = 256 bits
    public byte[] ComputeDHash256(string path)
    {
        using var image = new MagickImage(path);

        // 转灰度并缩放
        image.ColorType = ColorType.Grayscale;
        image.FilterType = FilterType.Box;
        image.Resize(new MagickGeometry(17, 16) { IgnoreAspectRatio = true });

        var pixels = image.GetPixels();

        // 256 bits -> 32 bytes
        var hash = new byte[32];
        int bitIndex = 0;

        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                // 取亮度（灰度图只要一个通道）
                var left = pixels.GetPixel(x, y).GetChannel(0);
                var right = pixels.GetPixel(x + 1, y).GetChannel(0);

                bool bit = left < right; // dHash 定义
                if (bit)
                {
                    int byteIndex = bitIndex >> 3;
                    int offset = 7 - (bitIndex & 7);
                    hash[byteIndex] |= (byte)(1 << offset);
                }
                bitIndex++;
            }
        }

        return hash;
    }

    public int HammingDistance(byte[] a, byte[] b)
    {
        int dist = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dist += BitOperations.PopCount((uint)(a[i] ^ b[i]));
        }
        return dist;
    }
}