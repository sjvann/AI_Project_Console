using System.Text.Json.Nodes;
using AiProject.Console.Core.Runtime;
using AiProject.Console.Core.Util;

namespace AiProject.Console.Core.Deploy;

public sealed class GcpConfig
{
    public string ProjectId { get; set; } = "";
    public string Zone { get; set; } = "";
    public string Instance { get; set; } = "";
    public string Host { get; set; } = "";
    public string Workflow { get; set; } = "deploy-gcp.yml";

    public bool IsComplete() =>
        !string.IsNullOrWhiteSpace(ProjectId) && !string.IsNullOrWhiteSpace(Zone) && !string.IsNullOrWhiteSpace(Instance);

    public JsonObject AsObject() => new()
    {
        ["projectId"] = ProjectId,
        ["zone"] = Zone,
        ["instance"] = Instance,
        ["host"] = Host,
        ["workflow"] = Workflow,
    };

    public static GcpConfig FromMapping(JsonNode? raw)
    {
        var obj = JsonUtil.Obj(raw);
        if (obj is null)
            return new GcpConfig();
        return new GcpConfig
        {
            ProjectId = JsonUtil.Pick(JsonUtil.Str(obj["projectId"]), JsonUtil.Str(obj["project_id"])),
            Zone = JsonUtil.Str(obj["zone"]),
            Instance = JsonUtil.Str(obj["instance"]),
            Host = JsonUtil.Str(obj["host"]),
            Workflow = JsonUtil.Pick(JsonUtil.Str(obj["workflow"]), "deploy-gcp.yml"),
        };
    }
}

public sealed class OnpremConfig
{
    public string Protocol { get; set; } = "ssh";
    public string Host { get; set; } = "";
    public string Port { get; set; } = "22";
    public string User { get; set; } = "";
    public string RemotePath { get; set; } = "";
    public string OpenUrl { get; set; } = "";
    public string KeyPath { get; set; } = "";

    public bool IsComplete()
    {
        var proto = string.IsNullOrWhiteSpace(Protocol) ? "ssh" : Protocol.Trim();
        return proto == "smb"
            ? !string.IsNullOrWhiteSpace(Host) || !string.IsNullOrWhiteSpace(RemotePath)
            : !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(User);
    }

    public JsonObject AsObject() => new()
    {
        ["protocol"] = string.IsNullOrWhiteSpace(Protocol) ? "ssh" : Protocol.Trim(),
        ["host"] = Host,
        ["port"] = Port,
        ["user"] = User,
        ["remotePath"] = RemotePath,
        ["openUrl"] = OpenUrl,
        ["keyPath"] = KeyPath,
    };

    public static OnpremConfig FromMapping(JsonNode? raw)
    {
        var obj = JsonUtil.Obj(raw);
        if (obj is null)
            return new OnpremConfig();
        var proto = JsonUtil.Pick(JsonUtil.Str(obj["protocol"]), "ssh");
        if (!DeployTargets.Protocols.ContainsKey(proto))
            proto = "ssh";
        var port = JsonUtil.Str(obj["port"]);
        if (string.IsNullOrEmpty(port))
            port = DeployTargets.DefaultPort(proto);
        return new OnpremConfig
        {
            Protocol = proto,
            Host = JsonUtil.Str(obj["host"]),
            Port = port,
            User = JsonUtil.Str(obj["user"]),
            RemotePath = JsonUtil.Pick(JsonUtil.Str(obj["remotePath"]), JsonUtil.Str(obj["remote_path"])),
            OpenUrl = JsonUtil.Pick(JsonUtil.Str(obj["openUrl"]), JsonUtil.Str(obj["open_url"])),
            KeyPath = JsonUtil.Pick(JsonUtil.Str(obj["keyPath"]), JsonUtil.Str(obj["key_path"])),
        };
    }
}

public sealed class AzureConfig
{
    public string SubscriptionId { get; set; } = "";
    public string ResourceGroup { get; set; } = "";
    public string AppName { get; set; } = "";
    public string Region { get; set; } = "";
    public string OpenUrl { get; set; } = "";
    public string Workflow { get; set; } = "deploy-azure.yml";

    public bool IsComplete() =>
        !string.IsNullOrWhiteSpace(SubscriptionId)
        && !string.IsNullOrWhiteSpace(ResourceGroup)
        && !string.IsNullOrWhiteSpace(AppName);

    public JsonObject AsObject() => new()
    {
        ["subscriptionId"] = SubscriptionId,
        ["resourceGroup"] = ResourceGroup,
        ["appName"] = AppName,
        ["region"] = Region,
        ["openUrl"] = OpenUrl,
        ["workflow"] = string.IsNullOrEmpty(Workflow) ? "deploy-azure.yml" : Workflow,
    };

    public static AzureConfig FromMapping(JsonNode? raw)
    {
        var obj = JsonUtil.Obj(raw);
        if (obj is null)
            return new AzureConfig();
        return new AzureConfig
        {
            SubscriptionId = JsonUtil.Pick(JsonUtil.Str(obj["subscriptionId"]), JsonUtil.Str(obj["subscription_id"])),
            ResourceGroup = JsonUtil.Pick(JsonUtil.Str(obj["resourceGroup"]), JsonUtil.Str(obj["resource_group"])),
            AppName = JsonUtil.Pick(JsonUtil.Str(obj["appName"]), JsonUtil.Str(obj["app_name"])),
            Region = JsonUtil.Str(obj["region"]),
            OpenUrl = JsonUtil.Pick(JsonUtil.Str(obj["openUrl"]), JsonUtil.Str(obj["open_url"]), JsonUtil.Str(obj["host"])),
            Workflow = JsonUtil.Pick(JsonUtil.Str(obj["workflow"]), "deploy-azure.yml"),
        };
    }
}

public static class DeployTargets
{
    public const string None = "none";
    public const string Gcp = "gcp";
    public const string Onprem = "onprem";
    public const string Azure = "azure";

    public static readonly IReadOnlyList<(string Key, string Label)> All =
    [
        (None, "不下發（僅本機）"),
        (Gcp, "Google Cloud (GCP)"),
        (Onprem, "自家機房／遠端伺服器"),
        (Azure, "Microsoft Azure"),
    ];

    public static readonly Dictionary<string, string> Protocols = new()
    {
        ["ssh"] = "SSH／SCP／rsync",
        ["winrm"] = "WinRM（PowerShell 遠端）",
        ["iis"] = "IIS 網站",
        ["smb"] = "檔案分享（SMB／網路磁碟）",
    };

    public static readonly Dictionary<string, string> Hints = new()
    {
        [None] = "此專案只在本機執行，不發佈到雲端或遠端伺服器。",
        [Gcp] = "部署到 GCP Compute Engine。需填 Project ID、Zone、Instance。",
        [Onprem] = "發佈到公司或自家機房的另一台伺服器（SSH、WinRM、IIS 或網路磁碟）。",
        [Azure] = "部署到 Azure App Service／同類資源。需填訂閱、資源群組與應用程式名稱。",
    };

    public static string TargetLabel(string key) => All.FirstOrDefault(t => t.Key == key).Label ?? All[0].Label;
    public static string ProtocolLabel(string key) => Protocols.GetValueOrDefault(key, Protocols["ssh"]);
    public static string DefaultPort(string proto) => proto switch
    {
        "winrm" => "5985",
        "iis" => "80",
        "smb" => "445",
        _ => "22",
    };
}

public sealed class DeployConfig
{
    public string Target { get; set; } = DeployTargets.None;
    public GcpConfig Gcp { get; set; } = new();
    public OnpremConfig Onprem { get; set; } = new();
    public AzureConfig Azure { get; set; } = new();

    public string NormalizedTarget()
    {
        var key = string.IsNullOrWhiteSpace(Target) ? DeployTargets.None : Target.Trim();
        return DeployTargets.All.Any(t => t.Key == key) ? key : DeployTargets.None;
    }

    public bool IsComplete() => NormalizedTarget() switch
    {
        DeployTargets.None => true,
        DeployTargets.Gcp => Gcp.IsComplete(),
        DeployTargets.Onprem => Onprem.IsComplete(),
        DeployTargets.Azure => Azure.IsComplete(),
        _ => false,
    };

    public string PublicUrl() => NormalizedTarget() switch
    {
        DeployTargets.Gcp => Gcp.Host.Trim(),
        DeployTargets.Onprem => JsonUtil.Pick(Onprem.OpenUrl, Onprem.Host),
        DeployTargets.Azure => Azure.OpenUrl.Trim(),
        _ => "",
    };

    public JsonObject AsObject() => new()
    {
        ["target"] = NormalizedTarget(),
        ["gcp"] = Gcp.AsObject(),
        ["onprem"] = Onprem.AsObject(),
        ["azure"] = Azure.AsObject(),
    };
}

public static class DeployConfigResolver
{
    public static DeployConfig FromMapping(JsonNode? raw, JsonNode? gcpFallback = null)
    {
        var obj = JsonUtil.Obj(raw) ?? new JsonObject();
        var gcp = GcpConfig.FromMapping(JsonUtil.Obj(obj["gcp"]) ?? gcpFallback);
        var onprem = OnpremConfig.FromMapping(obj["onprem"]);
        var azure = AzureConfig.FromMapping(obj["azure"]);
        var target = JsonUtil.Str(obj["target"]);
        if (string.IsNullOrEmpty(target))
        {
            if (!string.IsNullOrEmpty(gcp.ProjectId) || !string.IsNullOrEmpty(gcp.Instance))
                target = DeployTargets.Gcp;
            else if (!string.IsNullOrEmpty(onprem.Host) || !string.IsNullOrEmpty(onprem.RemotePath))
                target = DeployTargets.Onprem;
            else if (!string.IsNullOrEmpty(azure.SubscriptionId) || !string.IsNullOrEmpty(azure.AppName))
                target = DeployTargets.Azure;
            else
                target = DeployTargets.None;
        }
        if (DeployTargets.All.All(t => t.Key != target))
            target = DeployTargets.None;
        return new DeployConfig { Target = target, Gcp = gcp, Onprem = onprem, Azure = azure };
    }

    public static DeployConfig FromManifest(ProjectCatalog? catalog)
    {
        if (catalog is null)
            return new DeployConfig();
        var doc = catalog.Manifest;
        var merged = JsonUtil.Obj(doc["deploy"]) is JsonObject d ? d.DeserializeClone() : new JsonObject();
        if (merged["onprem"] is null && doc["onprem"] is JsonObject onprem)
            merged["onprem"] = onprem.DeserializeClone();
        if (merged["azure"] is null && doc["azure"] is JsonObject azure)
            merged["azure"] = azure.DeserializeClone();
        return FromMapping(merged, doc["gcp"]);
    }

    public static DeployConfig FromLocal(string? root)
    {
        if (root is null)
            return new DeployConfig();
        var data = ConsoleSettingsStore.Load();
        var byRoot = JsonUtil.Obj(data["deployByProject"]) ?? new JsonObject();
        var raw = byRoot[Path.GetFullPath(root)];
        var gcpBy = JsonUtil.Obj(data["gcpByProject"]);
        var gcpRaw = gcpBy?[Path.GetFullPath(root)];
        if (raw is not JsonObject && gcpRaw is JsonObject)
            return FromMapping(new JsonObject { ["target"] = DeployTargets.Gcp, ["gcp"] = gcpRaw }, gcpRaw);
        return FromMapping(raw, gcpRaw);
    }

    public static DeployConfig Resolve(ProjectCatalog? catalog)
    {
        var root = catalog?.Root;
        var local = FromLocal(root);
        var manifest = FromManifest(catalog);
        string target;
        if (HasLocalDeploy(root))
            target = string.IsNullOrEmpty(local.Target) ? DeployTargets.None : local.Target;
        else
            target = !string.IsNullOrEmpty(local.Target) && local.Target != DeployTargets.None
                ? local.Target
                : (string.IsNullOrEmpty(manifest.Target) ? DeployTargets.None : manifest.Target);
        return new DeployConfig
        {
            Target = target,
            Gcp = MergeGcp(local.Gcp, manifest.Gcp),
            Onprem = MergeOnprem(local.Onprem, manifest.Onprem),
            Azure = MergeAzure(local.Azure, manifest.Azure),
        };
    }

    public static void SaveLocal(string root, DeployConfig cfg)
    {
        var data = ConsoleSettingsStore.Load();
        var byRoot = JsonUtil.Obj(data["deployByProject"]) ?? new JsonObject();
        byRoot[Path.GetFullPath(root)] = cfg.AsObject();
        data["deployByProject"] = byRoot;
        var gcpBy = JsonUtil.Obj(data["gcpByProject"]) ?? new JsonObject();
        gcpBy[Path.GetFullPath(root)] = cfg.Gcp.AsObject();
        data["gcpByProject"] = gcpBy;
        ConsoleSettingsStore.Save(data);
    }

    public static string WriteManifest(ProjectCatalog catalog, DeployConfig cfg)
    {
        var path = Path.Combine(catalog.Root, AppInfo.ManifestName);
        var doc = JsonUtil.LoadObject(path);
        doc["deploy"] = new JsonObject { ["target"] = cfg.NormalizedTarget() };
        doc["gcp"] = cfg.Gcp.AsObject();
        doc["onprem"] = cfg.Onprem.AsObject();
        doc["azure"] = cfg.Azure.AsObject();
        if (doc["name"] is null)
            doc["name"] = catalog.Name;
        JsonUtil.SaveObject(path, doc);
        return path;
    }

    public static bool WorkflowExists(ProjectCatalog? catalog, GcpConfig? cfg = null)
    {
        if (catalog is null)
            return false;
        cfg ??= Resolve(catalog).Gcp;
        return File.Exists(Path.Combine(catalog.Root, ".github", "workflows", string.IsNullOrEmpty(cfg.Workflow) ? "deploy-gcp.yml" : cfg.Workflow));
    }

    public static bool OpenHost(DeployConfig cfg)
    {
        var url = cfg.PublicUrl();
        if (string.IsNullOrEmpty(url))
            return false;
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            url = (LooksLikeLan(url) ? "http://" : "https://") + url;
        CliUtil.OpenUrl(url);
        return true;
    }

    public static string StatusReport(ProjectCatalog? catalog)
    {
        var cfg = Resolve(catalog);
        var lines = new List<string> { "部署設定", "" };
        if (catalog is null)
        {
            lines.Add("尚未選擇專案。");
            return string.Join('\n', lines);
        }
        var target = cfg.NormalizedTarget();
        lines.Add($"專案目錄：{catalog.Root}");
        lines.Add($"發佈目標：{DeployTargets.TargetLabel(target)}");
        lines.Add("");
        if (target == DeployTargets.None)
        {
            lines.Add("此專案只在本機執行，不發佈到雲端或遠端伺服器。");
            return string.Join('\n', lines);
        }
        if (target == DeployTargets.Gcp)
        {
            var g = cfg.Gcp;
            lines.AddRange(
            [
                $"projectId：{OrUnset(g.ProjectId)}",
                $"zone：{OrUnset(g.Zone)}",
                $"instance：{OrUnset(g.Instance)}",
                $"host：{OrUnset(g.Host)}",
                $"workflow：{g.Workflow}",
                $"workflow 檔案：{(WorkflowExists(catalog, g) ? "存在" : "找不到")}",
            ]);
        }
        else if (target == DeployTargets.Onprem)
        {
            var o = cfg.Onprem;
            lines.AddRange(
            [
                $"協定：{DeployTargets.ProtocolLabel(o.Protocol)}",
                $"主機：{OrUnset(o.Host)}",
                $"連接埠：{OrUnset(o.Port, "（預設）")}",
                $"帳號：{OrUnset(o.User)}",
                $"遠端路徑：{OrUnset(o.RemotePath)}",
                $"SSH 金鑰：{(string.IsNullOrEmpty(o.KeyPath) ? "（未設定，使用預設 agent）" : o.KeyPath)}",
                $"對外網址：{OrUnset(o.OpenUrl)}",
            ]);
        }
        else if (target == DeployTargets.Azure)
        {
            var a = cfg.Azure;
            lines.AddRange(
            [
                $"subscriptionId：{OrUnset(a.SubscriptionId)}",
                $"resourceGroup：{OrUnset(a.ResourceGroup)}",
                $"appName：{OrUnset(a.AppName)}",
                $"region：{OrUnset(a.Region)}",
                $"對外網址：{OrUnset(a.OpenUrl)}",
                $"workflow：{a.Workflow}",
            ]);
        }
        lines.Add("");
        lines.Add(cfg.IsComplete() ? "設定齊全。" : "設定未齊全：請先完成「部署設定…」。");
        return string.Join('\n', lines);
    }

    public static string CiHint(ProjectCatalog? catalog)
    {
        var cfg = Resolve(catalog);
        var target = cfg.NormalizedTarget();
        if (target == DeployTargets.None)
            return "目前選擇不下發。若之後要發佈，請在「部署設定…」改選目標。";
        if (target == DeployTargets.Gcp)
        {
            var wf = string.IsNullOrEmpty(cfg.Gcp.Workflow) ? "deploy-gcp.yml" : cfg.Gcp.Workflow;
            var lines = new List<string>
            {
                "GCP CI 部署說明", "",
                "本控制台不內建 GCP 直推腳本。建議：",
                $"1. 確認 .github/workflows/{wf} 存在",
                "2. 完成 GCP 部署設定",
                "3. 以 git tag 或 gh workflow 觸發", "",
            };
            if (catalog is not null && WorkflowExists(catalog, cfg.Gcp))
            {
                lines.Add($"已找到 workflow：.github/workflows/{wf}");
                if (CliUtil.CommandExists("gh"))
                    lines.Add($"  gh workflow run {wf}");
            }
            else
                lines.Add($"尚未找到 .github/workflows/{wf}");
            return string.Join('\n', lines);
        }
        if (target == DeployTargets.Onprem)
        {
            var o = cfg.Onprem;
            var proto = string.IsNullOrEmpty(o.Protocol) ? "ssh" : o.Protocol;
            var port = string.IsNullOrEmpty(o.Port) ? DeployTargets.DefaultPort(proto) : o.Port;
            var lines = new List<string>
            {
                "自家機房／遠端伺服器部署說明", "",
                "本控制台先保存連線設定，實際發佈請用既有腳本或 CI。", "",
                $"協定：{DeployTargets.ProtocolLabel(proto)}",
                $"主機：{OrUnset(o.Host)}",
                $"連接埠：{OrUnset(port, "（預設）")}",
                $"帳號：{OrUnset(o.User)}",
                $"遠端路徑：{OrUnset(o.RemotePath)}", "",
            };
            if (proto == "ssh")
            {
                var dest = !string.IsNullOrEmpty(o.User) && !string.IsNullOrEmpty(o.Host) ? $"{o.User}@{o.Host}" : "user@host";
                var path = string.IsNullOrEmpty(o.RemotePath) ? "/opt/app" : o.RemotePath;
                var key = string.IsNullOrEmpty(o.KeyPath) ? "" : $"-i {o.KeyPath} ";
                var extra = !string.IsNullOrEmpty(port) && port != "22" ? $"-P {port} " : "";
                lines.Add("常見作法：建置後以 scp／rsync 複製，再於遠端重啟服務。");
                lines.Add($"  scp {extra}{key}<artifact> {dest}:{path}");
                lines.Add($"  ssh {key}{dest} systemctl restart <service>");
            }
            else if (proto == "winrm")
                lines.Add("常見作法：WinRM／PowerShell Remoting 複製檔案並重啟 IIS 或 Windows 服務。");
            else if (proto == "iis")
                lines.Add("常見作法：dotnet publish 後以 msdeploy 或手動複製到 IIS 網站實體路徑。");
            else
                lines.Add("常見作法：對應到 UNC 路徑後複製發佈輸出。");
            return string.Join('\n', lines);
        }
        var a = cfg.Azure;
        var awf = string.IsNullOrEmpty(a.Workflow) ? "deploy-azure.yml" : a.Workflow;
        return string.Join('\n',
        [
            "Azure 部署說明", "",
            "本控制台不內建 Azure 直推腳本。建議以 GitHub Actions 或 Azure DevOps 發佈。",
            $"subscription：{OrUnset(a.SubscriptionId)}",
            $"resourceGroup：{OrUnset(a.ResourceGroup)}",
            $"appName：{OrUnset(a.AppName)}",
            $"workflow：{awf}",
        ]);
    }

    private static bool HasLocalDeploy(string? root)
    {
        if (root is null)
            return false;
        var data = ConsoleSettingsStore.Load();
        var byRoot = JsonUtil.Obj(data["deployByProject"]);
        return byRoot?[Path.GetFullPath(root)] is JsonObject;
    }

    private static GcpConfig MergeGcp(GcpConfig local, GcpConfig manifest) => new()
    {
        ProjectId = JsonUtil.Pick(local.ProjectId, manifest.ProjectId),
        Zone = JsonUtil.Pick(local.Zone, manifest.Zone),
        Instance = JsonUtil.Pick(local.Instance, manifest.Instance),
        Host = JsonUtil.Pick(local.Host, manifest.Host),
        Workflow = JsonUtil.Pick(local.Workflow, manifest.Workflow, "deploy-gcp.yml"),
    };

    private static OnpremConfig MergeOnprem(OnpremConfig local, OnpremConfig manifest)
    {
        var proto = JsonUtil.Pick(local.Protocol, manifest.Protocol, "ssh");
        return new OnpremConfig
        {
            Protocol = proto,
            Host = JsonUtil.Pick(local.Host, manifest.Host),
            Port = JsonUtil.Pick(local.Port, manifest.Port, DeployTargets.DefaultPort(proto)),
            User = JsonUtil.Pick(local.User, manifest.User),
            RemotePath = JsonUtil.Pick(local.RemotePath, manifest.RemotePath),
            OpenUrl = JsonUtil.Pick(local.OpenUrl, manifest.OpenUrl),
            KeyPath = JsonUtil.Pick(local.KeyPath, manifest.KeyPath),
        };
    }

    private static AzureConfig MergeAzure(AzureConfig local, AzureConfig manifest) => new()
    {
        SubscriptionId = JsonUtil.Pick(local.SubscriptionId, manifest.SubscriptionId),
        ResourceGroup = JsonUtil.Pick(local.ResourceGroup, manifest.ResourceGroup),
        AppName = JsonUtil.Pick(local.AppName, manifest.AppName),
        Region = JsonUtil.Pick(local.Region, manifest.Region),
        OpenUrl = JsonUtil.Pick(local.OpenUrl, manifest.OpenUrl),
        Workflow = JsonUtil.Pick(local.Workflow, manifest.Workflow, "deploy-azure.yml"),
    };

    private static bool LooksLikeLan(string host)
    {
        var text = host.Trim();
        if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            text = Uri.TryCreate(text, UriKind.Absolute, out var u) ? (u.Host ?? text) : text;
        if (text is "localhost" or "127.0.0.1")
            return true;
        var parts = text.Split('.');
        if (parts.Length == 4 && parts.All(p => int.TryParse(p, out var n) && n is >= 0 and <= 255))
        {
            var a = int.Parse(parts[0]);
            var b = int.Parse(parts[1]);
            return a == 10 || (a == 192 && b == 168) || (a == 172 && b is >= 16 and <= 31);
        }
        return false;
    }

    private static string OrUnset(string value, string unset = "（未設定）") =>
        string.IsNullOrEmpty(value) ? unset : value;
}

internal static class JsonObjectClone
{
    public static JsonObject DeserializeClone(this JsonObject obj) =>
        JsonNode.Parse(obj.ToJsonString()) as JsonObject ?? new JsonObject();
}
