using Microsoft.VisualBasic.FileIO;
using System.IO;

namespace ImgCombiner.Services;

public sealed class RecycleBinService : IRecycleBinService
{
    public void SendToRecycleBin(string path)
    {
        if (File.Exists(path))
        {
            FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        }
        else if (Directory.Exists(path))
        {
            FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        }
    }
}