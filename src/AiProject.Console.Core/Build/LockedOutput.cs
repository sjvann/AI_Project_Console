namespace AiProject.Console.Core.Build;

/// <summary>
/// Windows 無法覆寫正在載入的 DLL。建置前把被鎖檔改名，複製就能成功；
/// 行程仍使用舊檔，下次啟動才吃新組件。
/// </summary>
public static class LockedOutput
{
    public const string ReplacedExtension = ".replaced";

    public static int Unlock(IEnumerable<string> paths)
    {
        var n = 0;
        foreach (var path in paths)
        {
            if (TryUnlock(path))
                n++;
        }
        return n;
    }

    public static bool TryUnlock(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;
        if (!IsLocked(path))
            return false;

        var dest = path + "." + Guid.NewGuid().ToString("N") + ReplacedExtension;
        try
        {
            File.Move(path, dest);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static void Sweep(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return;
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(directory, "*" + ReplacedExtension);
        }
        catch (IOException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        foreach (var file in files)
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // 仍被目前行程對應著，下次再清
            }
            catch (UnauthorizedAccessException)
            {
                // ignore
            }
        }
    }

    static bool IsLocked(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }
}
