using AiProject.Console.Core.Catalog;

namespace AiProject.Console.Core.Tests;

public class ServiceGroupTreeTests
{
    [Fact]
    public void SplitPath_NormalizesSlashesAndBlanks()
    {
        Assert.Equal(["其他"], ServiceGroupTree.SplitPath(""));
        Assert.Equal(["Lab"], ServiceGroupTree.SplitPath("Lab"));
        Assert.Equal(["Lab", "HL7"], ServiceGroupTree.SplitPath("Lab/HL7"));
        Assert.Equal(["Lab", "SFTP"], ServiceGroupTree.SplitPath(@"Lab\SFTP"));
        Assert.Equal(["Lab", "HL7"], ServiceGroupTree.SplitPath(" Lab // HL7 / "));
    }

    [Fact]
    public void Build_SingleLevel_KeepsFlatGroup()
    {
        var roots = ServiceGroupTree.Build(
        [
            Svc("a", "Lab"),
            Svc("b", "Lab"),
            Svc("c", "Weave"),
        ]);

        Assert.Equal(["Lab", "Weave"], roots.Select(n => n.Key));
        Assert.Equal(["a", "b"], roots[0].Services.Select(s => s.Id));
        Assert.Empty(roots[0].Children);
        Assert.Equal(2, roots[0].ServiceCount);
    }

    [Fact]
    public void Build_NestedPaths_CreatesSubgroupsInFirstSeenOrder()
    {
        var roots = ServiceGroupTree.Build(
        [
            Svc("hl7-api", "Lab/HL7"),
            Svc("sftp-api", "Lab/SFTP"),
            Svc("hl7-ui", "Lab/HL7"),
            Svc("weave", "Weave"),
        ]);

        Assert.Equal(["Lab", "Weave"], roots.Select(n => n.Key));
        var lab = roots[0];
        Assert.Empty(lab.Services);
        Assert.Equal(["Lab/HL7", "Lab/SFTP"], lab.Children.Select(n => n.Key));
        Assert.Equal(["HL7", "SFTP"], lab.Children.Select(n => n.Name));
        Assert.Equal(["hl7-api", "hl7-ui"], lab.Children[0].Services.Select(s => s.Id));
        Assert.Equal(["hl7-api", "hl7-ui", "sftp-api"], lab.Descendants().Select(s => s.Id));
        Assert.Equal(3, lab.ServiceCount);
        Assert.Equal(["Lab", "Lab/HL7", "Lab/SFTP", "Weave"], ServiceGroupTree.AllKeys(roots));
    }

    [Fact]
    public void Find_And_WalkVisible_RespectCollapse()
    {
        var roots = ServiceGroupTree.Build(
        [
            Svc("hl7-api", "Lab/HL7"),
            Svc("sftp-api", "Lab/SFTP"),
        ]);
        var hl7 = ServiceGroupTree.Find(roots, "Lab/HL7");
        Assert.NotNull(hl7);
        Assert.Equal("HL7", hl7.Name);

        var collapsed = ServiceGroupTree.WalkVisible(roots, key => key == "Lab").Select(n => n.Key);
        Assert.Equal(["Lab"], collapsed);

        var expanded = ServiceGroupTree.WalkVisible(roots, _ => false).Select(n => n.Key);
        Assert.Equal(["Lab", "Lab/HL7", "Lab/SFTP"], expanded);
    }

    [Fact]
    public void Build_InfersSubgroupsFromSharedLabelPrefix()
    {
        var roots = ServiceGroupTree.Build(
        [
            Svc("hl7-api", "Lab", "HL7 Test Bench API"),
            Svc("hl7-ui", "Lab", "HL7 Test Bench"),
            Svc("sftp-api", "Lab", "SFTP Test Bench API"),
            Svc("sftp-ui", "Lab", "SFTP Test Bench"),
        ]);

        var lab = Assert.Single(roots);
        Assert.Empty(lab.Services);
        Assert.Equal(["Lab/HL7", "Lab/SFTP"], lab.Children.Select(n => n.Key));
        Assert.Equal(["hl7-api", "hl7-ui"], lab.Children[0].Services.Select(s => s.Id));
        Assert.Equal(["sftp-api", "sftp-ui"], lab.Children[1].Services.Select(s => s.Id));
    }

    [Fact]
    public void Build_DoesNotInferWhenPrefixesAreUnique()
    {
        var roots = ServiceGroupTree.Build(
        [
            Svc("a", "Lab", "Alpha Api"),
            Svc("b", "Lab", "Beta Api"),
        ]);

        Assert.Empty(roots[0].Children);
        Assert.Equal(2, roots[0].Services.Count);
    }

    private static ServiceEntry Svc(string id, string group, string? label = null) =>
        new(id, label ?? id, id, id, 80, "", "", group);
}
