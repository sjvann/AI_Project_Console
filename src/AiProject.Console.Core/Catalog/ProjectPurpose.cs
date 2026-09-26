using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using AiProject.Console.Core.Util;

namespace AiProject.Console.Core.Catalog;

/// <summary>
/// 每個掃描到的專案一句產品功能描述。清單可覆寫；否則只讀專案檔或套件清單上的 description 欄。
/// 沒有該欄就留空，不讀 README、也不把檔案讀完——大型工作區會因此在開啟時停住。
/// 開啟或重掃工作區時寫入 <c>.ai_project/product-purposes.md</c>。
/// </summary>
public static class ProjectPurpose
{
    public const string FileName = "product-purposes.md";
    public const string SourceManifest = "清單";
    public const string SourceProjectFile = "專案檔";
    public const string SourcePackageJson = "package.json";
    public const string SourcePyproject = "pyproject.toml";
    public const string SourceCargo = "Cargo.toml";
    public const string SourcePom = "pom.xml";

    /// <summary>超過此大小視為沒有一句用途，避免為了找描述把整份檔讀進記憶體。</summary>
    const int MaxDescriptionFileBytes = 1024 * 1024;

    public static string RelPath => AppInfo.RuntimeDirName + "/" + FileName;

    public static IReadOnlyList<ProjectInfo> Fill(
        string root,
        JsonObject manifest,
        IReadOnlyList<ProjectInfo> projects)
    {
        ArgumentNullException.ThrowIfNull(projects);
        var declared = ReadManifest(manifest);
        var filled = new List<ProjectInfo>(projects.Count);
        foreach (var p in projects)
        {
            var key = ServiceCatalogBuilder.ProjectKey(p.RelDir);
            if (declared.TryGetValue(key, out var fromList)
                && !SameAsName(fromList, p.Name))
            {
                filled.Add(p with { Description = fromList, DescriptionSource = SourceManifest });
                continue;
            }

            var fromFile = ServiceCatalogBuilder.ShortPurpose(p.Description);
            if (!string.IsNullOrEmpty(fromFile) && !SameAsName(fromFile, p.Name))
            {
                filled.Add(p with { Description = fromFile, DescriptionSource = SourceProjectFile });
                continue;
            }

            var (sidecar, source) = ReadSidecar(root, p);
            if (!string.IsNullOrEmpty(sidecar) && !SameAsName(sidecar, p.Name))
            {
                filled.Add(p with { Description = sidecar, DescriptionSource = source });
                continue;
            }

            filled.Add(p with { Description = "", DescriptionSource = "" });
        }
        return filled;
    }

    public static string Render(IReadOnlyList<ProjectInfo> projects, string workspaceName)
    {
        var name = string.IsNullOrWhiteSpace(workspaceName) ? "工作區" : workspaceName.Trim();
        var list = projects ?? [];
        var have = list.Count(p => !string.IsNullOrEmpty(p.Description));
        var missing = list.Where(p => string.IsNullOrEmpty(p.Description)).ToList();
        var sb = new StringBuilder();
        sb.AppendLine("# 產品功能描述");
        sb.AppendLine();
        sb.AppendLine("此檔由 AI_Project 控制台在開啟或重新掃描工作區時產生，下次會覆寫，請不要手改。");
        sb.AppendLine("權威來源：各專案檔的 `Description`／套件 `description`，或工作區 `ai-project.json` 的 `projects`。沒有該欄就留空，不讀 README。");
        sb.AppendLine();
        sb.AppendLine($"工作區：**{name}**。已有描述 {have}／{list.Count}。");
        sb.AppendLine();
        foreach (var g in list.GroupBy(p => string.IsNullOrEmpty(p.Group) ? "其他" : p.Group, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine("## " + g.Key);
            sb.AppendLine();
            sb.AppendLine("| 專案 | 類型 | 用途 | 來源 |");
            sb.AppendLine("|------|------|------|------|");
            foreach (var p in g.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                var kind = p.IsTest ? "測試" : (p.IsExecutable || p.IsWeb ? "核心" : "函式庫");
                var purpose = string.IsNullOrEmpty(p.Description) ? "（缺）" : p.Description;
                var source = string.IsNullOrEmpty(p.DescriptionSource) ? "—" : p.DescriptionSource;
                sb.AppendLine($"| {Cell(p.Name)} | {kind} | {Cell(purpose)} | {source} |");
            }
            sb.AppendLine();
        }

        if (missing.Count > 0)
        {
            sb.AppendLine("## 尚未提供");
            sb.AppendLine();
            sb.AppendLine("下列專案沒有一句用途。請在專案檔加 `Description`（或 `package.json`／`pyproject.toml` 的 description），或在 `ai-project.json` 的 `projects` 寫上。");
            sb.AppendLine();
            foreach (var p in missing)
                sb.AppendLine($"- `{p.RelDir}`（{p.Name}）");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    public static string? TryWrite(ProjectCatalog catalog)
    {
        if (catalog is null || string.IsNullOrWhiteSpace(catalog.Root))
            return null;
        try
        {
            var dir = Path.Combine(catalog.Root, AppInfo.RuntimeDirName);
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, FileName);
            var text = Render(catalog.Projects, catalog.Name);
            if (File.Exists(path))
            {
                var previous = File.ReadAllText(path, Encoding.UTF8);
                if (string.Equals(previous, text, StringComparison.Ordinal))
                    return RelPath;
            }
            File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return RelPath;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal static Dictionary<string, string> ReadManifest(JsonObject? manifest)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (manifest is null)
            return map;
        var node = manifest["projects"] ?? manifest["projectPurposes"] ?? manifest["purposes"];
        if (node is JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (item is not JsonObject obj)
                    continue;
                var path = JsonUtil.Pick(JsonUtil.Str(obj["path"]), JsonUtil.Str(obj["project"]), JsonUtil.Str(obj["dir"]));
                var desc = ServiceCatalogBuilder.ShortPurpose(JsonUtil.Str(obj["description"]));
                if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(desc))
                    continue;
                map.TryAdd(ServiceCatalogBuilder.ProjectKey(path), desc);
            }
        }
        else if (node is JsonObject obj)
        {
            foreach (var kv in obj)
            {
                var desc = ServiceCatalogBuilder.ShortPurpose(JsonUtil.Str(kv.Value));
                if (string.IsNullOrEmpty(kv.Key) || string.IsNullOrEmpty(desc))
                    continue;
                map.TryAdd(ServiceCatalogBuilder.ProjectKey(kv.Key), desc);
            }
        }
        return map;
    }

    static (string Text, string Source) ReadSidecar(string root, ProjectInfo project)
    {
        var dir = ProjectDirectory(root, project);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            return ("", "");

        var pkg = ReadJsonDescription(Path.Combine(dir, "package.json"));
        if (!string.IsNullOrEmpty(pkg))
            return (pkg, SourcePackageJson);

        var py = ReadTomlDescription(Path.Combine(dir, "pyproject.toml"), "project");
        if (!string.IsNullOrEmpty(py))
            return (py, SourcePyproject);

        var cargo = ReadTomlDescription(Path.Combine(dir, "Cargo.toml"), "package");
        if (!string.IsNullOrEmpty(cargo))
            return (cargo, SourceCargo);

        var pom = ReadPomDescription(Path.Combine(dir, "pom.xml"));
        if (!string.IsNullOrEmpty(pom))
            return (pom, SourcePom);

        return ("", "");
    }

    static string ProjectDirectory(string root, ProjectInfo project)
    {
        if (!string.IsNullOrEmpty(project.Csproj))
        {
            var fromFile = Path.GetDirectoryName(project.Csproj);
            if (!string.IsNullOrEmpty(fromFile) && Directory.Exists(fromFile))
                return fromFile;
        }
        if (string.IsNullOrEmpty(project.RelDir))
            return Path.GetFullPath(root);
        return Path.GetFullPath(Path.Combine(root, project.RelDir.Replace('/', Path.DirectorySeparatorChar)));
    }

    static bool CanReadDescriptionFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists && info.Length <= MaxDescriptionFileBytes;
        }
        catch (Exception)
        {
            return false;
        }
    }

    static string ReadJsonDescription(string path)
    {
        if (!CanReadDescriptionFile(path))
            return "";
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("description", out var n) && n.ValueKind == JsonValueKind.String)
                return ServiceCatalogBuilder.ShortPurpose(n.GetString());
        }
        catch (Exception)
        {
            return "";
        }
        return "";
    }

    static string ReadTomlDescription(string path, string section)
    {
        if (!CanReadDescriptionFile(path))
            return "";
        try
        {
            var inSection = false;
            foreach (var raw in File.ReadLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';'))
                    continue;
                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    if (inSection)
                        return "";
                    var name = line.Trim('[', ']').Trim();
                    inSection = string.Equals(name, section, StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                if (!inSection)
                    continue;
                if (!line.StartsWith("description", StringComparison.OrdinalIgnoreCase))
                    continue;
                var eq = line.IndexOf('=');
                if (eq < 0)
                    continue;
                return ServiceCatalogBuilder.ShortPurpose(Unquote(line[(eq + 1)..].Trim()));
            }
        }
        catch (Exception)
        {
            return "";
        }
        return "";
    }

    static string ReadPomDescription(string path)
    {
        if (!CanReadDescriptionFile(path))
            return "";
        try
        {
            var doc = XDocument.Load(path);
            var desc = doc.Root?.Elements().FirstOrDefault(e =>
                string.Equals(e.Name.LocalName, "description", StringComparison.OrdinalIgnoreCase));
            return ServiceCatalogBuilder.ShortPurpose(desc?.Value);
        }
        catch (Exception)
        {
            return "";
        }
    }

    static string Unquote(string value)
    {
        var s = value.Trim();
        if (s.Length >= 6 && s.StartsWith("\"\"\"", StringComparison.Ordinal) && s.EndsWith("\"\"\"", StringComparison.Ordinal))
            return s[3..^3].Trim();
        if (s.Length >= 2 && ((s[0] == '"' && s[^1] == '"') || (s[0] == '\'' && s[^1] == '\'')))
            return s[1..^1].Trim();
        return s;
    }

    static bool SameAsName(string purpose, string name) =>
        purpose.Equals(name, StringComparison.OrdinalIgnoreCase);

    static string Cell(string value) =>
        (value ?? "").Replace("|", "\\|", StringComparison.Ordinal).Replace("\r", " ").Replace("\n", " ");
}
