using System.Text.Json.Nodes;
using AiProject.Console.Core;
using AiProject.Console.Core.Deploy;

namespace AiProject.Console.Core.Tests;

public class DeployConfigTests
{
    [Fact]
    public void Machine_RoundTrip_KeepsBindPortAndOpenUrl()
    {
        var cfg = new DeployConfig
        {
            Target = DeployTargets.Machine,
            Machine = new MachineConfig
            {
                Bind = "0.0.0.0",
                Port = "5100",
                OpenUrl = "http://192.168.1.20:5100",
            },
        };

        var again = DeployConfigResolver.FromMapping(cfg.AsObject());
        Assert.Equal(DeployTargets.Machine, again.NormalizedTarget());
        Assert.Equal("0.0.0.0", again.Machine.Bind);
        Assert.Equal("5100", again.Machine.Port);
        Assert.Equal("http://192.168.1.20:5100", again.Machine.OpenUrl);
        Assert.True(again.IsComplete());
        Assert.Equal("http://192.168.1.20:5100", again.PublicUrl());
    }

    [Theory]
    [InlineData("127.0.0.1", "5100", "http://192.168.1.20:5100")]
    [InlineData("localhost", "", "http://192.168.1.20:5100")]
    [InlineData("0.0.0.0", "5100", "http://localhost:5100")]
    [InlineData("0.0.0.0", "5100", "http://127.0.0.1:5100")]
    [InlineData("0.0.0.0", "5100", "")]
    [InlineData("", "5100", "http://192.168.1.20:5100")]
    [InlineData("0.0.0.0", "abc", "http://192.168.1.20:5100")]
    public void Machine_RejectsLoopbackAndBadPort(string bind, string port, string openUrl)
    {
        var cfg = new DeployConfig
        {
            Target = DeployTargets.Machine,
            Machine = new MachineConfig { Bind = bind, Port = port, OpenUrl = openUrl },
        };
        Assert.False(cfg.IsComplete());
    }

    [Fact]
    public void Machine_AllowsPublicNameWithoutPort()
    {
        var cfg = new DeployConfig
        {
            Target = DeployTargets.Machine,
            Machine = new MachineConfig
            {
                Bind = "192.168.1.20",
                OpenUrl = "https://demo.example.com",
            },
        };
        Assert.True(cfg.IsComplete());
    }

    [Fact]
    public void Machine_InfersTargetWhenOnlyMachineBlockIsFilled()
    {
        var raw = JsonNode.Parse("""
        {
          "machine": { "bind": "0.0.0.0", "openUrl": "http://192.168.1.20:5100" }
        }
        """)!;
        var cfg = DeployConfigResolver.FromMapping(raw);
        Assert.Equal(DeployTargets.Machine, cfg.Target);
        Assert.Equal("0.0.0.0", cfg.Machine.Bind);
    }

    [Fact]
    public void Machine_DoesNotOverrideExplicitNoneOrGcp()
    {
        var none = DeployConfigResolver.FromMapping(JsonNode.Parse("""
        {
          "target": "none",
          "machine": { "bind": "0.0.0.0", "openUrl": "http://192.168.1.20:5100" }
        }
        """)!);
        Assert.Equal(DeployTargets.None, none.NormalizedTarget());

        var gcp = DeployConfigResolver.FromMapping(JsonNode.Parse("""
        {
          "gcp": { "projectId": "p1", "instance": "vm1" },
          "machine": { "openUrl": "http://192.168.1.20:5100" }
        }
        """)!);
        Assert.Equal(DeployTargets.Gcp, gcp.Target);
    }

    [Fact]
    public void Machine_AcceptsListenAlias()
    {
        var cfg = DeployConfigResolver.FromMapping(JsonNode.Parse("""
        {
          "target": "machine",
          "machine": { "listen": "0.0.0.0", "open_url": "http://10.0.0.8:8080" }
        }
        """)!);
        Assert.Equal("0.0.0.0", cfg.Machine.Bind);
        Assert.Equal("http://10.0.0.8:8080", cfg.Machine.OpenUrl);
        Assert.True(cfg.IsComplete());
    }

    [Fact]
    public void Machine_StatusAndHint_DescribeThisComputer()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-console-machine-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var catalog = new ProjectCatalog
            {
                Root = root,
                Name = "demo",
                Services = [],
                Projects = [],
                StartOrder = [],
                Frontend = "",
                Manifest = new JsonObject
                {
                    ["deploy"] = new JsonObject { ["target"] = "machine" },
                    ["machine"] = new JsonObject
                    {
                        ["bind"] = "0.0.0.0",
                        ["port"] = "5100",
                        ["openUrl"] = "http://192.168.1.20:5100",
                    },
                },
                Scan = new ScanResult(root, []),
            };

            var status = DeployConfigResolver.StatusView(catalog);
            Assert.Equal("ok", status.Tone);
            Assert.Equal("本機對外", status.Headline);
            Assert.Contains("192.168.1.20", status.Text);
            Assert.Equal("open-deploy", status.PrimaryAction);

            var hint = DeployConfigResolver.CiHintView(catalog);
            Assert.Contains("ASPNETCORE_URLS=http://0.0.0.0:5100", hint.Text);
            Assert.Contains("不代開防火牆", hint.Text);
            Assert.Equal("open-deploy", hint.PrimaryAction);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Machine_WriteManifest_RoundTrips()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-console-machine-write-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var catalog = new ProjectCatalog
            {
                Root = root,
                Name = "demo",
                Services = [],
                Projects = [],
                StartOrder = [],
                Frontend = "",
                Manifest = new JsonObject(),
                Scan = new ScanResult(root, []),
            };
            var cfg = new DeployConfig
            {
                Target = DeployTargets.Machine,
                Machine = new MachineConfig
                {
                    Bind = "+",
                    Port = "8080",
                    OpenUrl = "https://demo.example.com",
                },
            };
            DeployConfigResolver.WriteManifest(catalog, cfg);
            var written = new ProjectCatalog
            {
                Root = root,
                Name = "demo",
                Services = [],
                Projects = [],
                StartOrder = [],
                Frontend = "",
                Manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "ai-project.json")))!.AsObject(),
                Scan = new ScanResult(root, []),
            };
            var loaded = DeployConfigResolver.FromManifest(written);
            Assert.Equal(DeployTargets.Machine, loaded.Target);
            Assert.Equal("+", loaded.Machine.Bind);
            Assert.Equal("8080", loaded.Machine.Port);
            Assert.Equal("https://demo.example.com", loaded.Machine.OpenUrl);
            Assert.True(loaded.IsComplete());
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
