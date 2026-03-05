namespace ImgCombiner.Services;

public interface IImageSignatureService
{
    ImageMeta ReadMetaFast(string path);

    byte[] Compute4x4Rgb(string path);   // 48 bytes
    byte[] Compute2x2Rgb(string path);   // 48 bytes
    int DistanceManhattan(byte[] a, byte[] b);

    byte[] ComputeDHash256(string path); // 32 bytes
    int HammingDistance(byte[] a, byte[] b);
}