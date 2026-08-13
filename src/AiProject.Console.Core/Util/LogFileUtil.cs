using System.Text;

namespace AiProject.Console.Core.Util;

public static class LogFileUtil
{
    public const FileShare Share = FileShare.ReadWrite | FileShare.Delete;

    public static FileStream OpenAppend(string path) =>
        new(path, FileMode.Append, FileAccess.Write, Share, bufferSize: 4096, FileOptions.SequentialScan);

    public static byte[] ReadAllBytes(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, Share, bufferSize: 4096, FileOptions.SequentialScan);
        var length = fs.Length;
        if (length == 0)
            return [];
        if (length > int.MaxValue)
            throw new IOException("Log file too large.");
        var data = new byte[(int)length];
        var offset = 0;
        while (offset < data.Length)
        {
            var read = fs.Read(data, offset, data.Length - offset);
            if (read == 0)
                break;
            offset += read;
        }
        if (offset < data.Length)
            Array.Resize(ref data, offset);
        return data;
    }
}
