namespace S7.NetCore.Tests.VirtualPlc.Server;

internal sealed record VirtualPlcPresenceProbe(
    bool Available,
    VirtualPlcSkipReason Reason,
    string Detail,
    string? Path);

internal static class VirtualPlcLibraryPresence
{
    private const string LibraryFileName = "snap7.dll";

    internal static VirtualPlcPresenceProbe Probe()
    {
        string outputPath = Path.Combine(AppContext.BaseDirectory, LibraryFileName);
        string? root = FindRepositoryRoot();
        string projectPath = root is null
            ? "<未找到仓库根>"
            : Path.Combine(root, "tests", "S7.NetCore.Tests", LibraryFileName);

        try
        {
            if (File.Exists(outputPath))
            {
                return new VirtualPlcPresenceProbe(true, default, string.Empty, outputPath);
            }

            if (root is not null && File.Exists(projectPath))
            {
                return new VirtualPlcPresenceProbe(true, default, string.Empty, projectPath);
            }
        }
        catch (Exception exception)
        {
            return new VirtualPlcPresenceProbe(
                false,
                VirtualPlcSkipReason.LibraryMissing,
                $"探测原生库时发生异常：{exception.Message}。查找位置：{outputPath}；{projectPath}。",
                null);
        }

        return new VirtualPlcPresenceProbe(
            false,
            VirtualPlcSkipReason.LibraryMissing,
            $"未找到原生库 {LibraryFileName}。查找位置：{outputPath}；{projectPath}。"
            + "请将 snap7.dll 复制到测试输出目录。",
            null);
    }

    private static string? FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "S7.NetCore.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}