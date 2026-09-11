using System.Reflection;

namespace AiProject.Console.Core.Stack;

internal static class McpServerIcons
{
    const string PngResource = "AiProject.Console.Core.Stack.mcp-icon.png";
    const string SvgResource = "AiProject.Console.Core.Brand.logo.svg";

    internal static object[] ForInitialize() =>
    [
        DataUriIcon(PngResource, "image/png", "64x64"),
        DataUriIcon(SvgResource, "image/svg+xml", "any"),
    ];

    static object DataUriIcon(string resource, string mimeType, string size)
    {
        var bytes = Read(resource);
        return new
        {
            src = $"data:{mimeType};base64,{Convert.ToBase64String(bytes)}",
            mimeType,
            sizes = new[] { size },
        };
    }

    static byte[] Read(string resource)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException("缺少內嵌品牌圖：" + resource);
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
