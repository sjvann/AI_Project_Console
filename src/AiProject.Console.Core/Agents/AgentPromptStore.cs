namespace AiProject.Console.Core.Agents;

public static class AgentPromptStore
{
    public static string Write(string root, string prompt)
    {
        var dir = Path.Combine(Path.GetFullPath(root), AppInfo.RuntimeDirName, "agent-prompts");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"prompt-{DateTime.Now:yyyyMMdd-HHmmss}.md");
        File.WriteAllText(path, prompt ?? "");
        return path;
    }
}
