namespace ImgCombiner.Services;

public readonly record struct ImageMeta(int Width, int Height, double Ratio);

public sealed class ImageSignature
{
    public string Path { get; }
    public ImageMeta Meta { get; }

    // lazy computed
    public byte[]? Block4x4Rgb { get; set; }   // 48 bytes
    public byte[]? DHash256 { get; set; }      // 32 bytes = 256 bits

    public ImageSignature(string path, ImageMeta meta)
    {
        Path = path;
        Meta = meta;
    }
}