namespace S7.NetCore.Tests.VirtualPlc;

[Collection(VirtualPlcUnitCollection.Name)]
[Trait("Category", "VirtualPlc")]
public sealed class ServerMigrationTests
{
    private static readonly string Root = FindRepositoryRoot();

    [Fact]
    public void S_D03_DefaultEndpointAndNineEnvironmentVariables()
    {
        string endpoint = File.ReadAllText(
            Path.Combine(Root, "tests", "S7.NetCore.Tests", "VirtualPlc", "VirtualPlcEndpoint.cs"));
        string settings = File.ReadAllText(
            Path.Combine(Root, "tests", "S7.NetCore.Tests", "VirtualPlc", "Server", "VirtualPlcServerSettings.cs"));

        Assert.Contains("new(\"127.0.0.1\", 1102", endpoint, StringComparison.Ordinal);

        foreach (string variable in new[]
        {
            "S7_VIRTUAL_PLC_HOST",
            "S7_VIRTUAL_PLC_PORT",
            "S7_VIRTUAL_PLC_CPU",
            "S7_VIRTUAL_PLC_RACK",
            "S7_VIRTUAL_PLC_SLOT",
            "S7_VIRTUAL_PLC_ALLOW_NON_LOOPBACK",
            "S7_VIRTUAL_PLC_SERVER",
            "S7_VIRTUAL_PLC_SERVER_LIBRARY",
            "S7_VIRTUAL_PLC_SERVER_MAX_CLIENTS"
        })
        {
            Assert.Contains(variable, settings, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void S_M01_NativeAssetsBelongToTestBridgeOnly()
    {
        string testProject = File.ReadAllText(
            Path.Combine(Root, "tests", "S7.NetCore.Tests", "S7.NetCore.Tests.csproj"));
        string nativeProject = File.ReadAllText(
            Path.Combine(Root, "tests", "S7.NetCore.Tests.Native", "S7.NetCore.Tests.Native.csproj"));

        Assert.Contains("<Compile Remove=\"snap7.net.cs\" />", testProject, StringComparison.Ordinal);
        Assert.Contains(
            "..\\S7.NetCore.Tests.Native\\S7.NetCore.Tests.Native.csproj",
            testProject,
            StringComparison.Ordinal);
        Assert.Contains(
            "..\\S7.NetCore.Tests\\snap7.net.cs",
            nativeProject,
            StringComparison.Ordinal);

        foreach (string file in Directory.EnumerateFiles(
            Path.Combine(Root, "src", "S7.NetCore"),
            "*.cs",
            SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            string content = File.ReadAllText(file);
            Assert.DoesNotContain("DllImport", content, StringComparison.Ordinal);
            Assert.DoesNotContain("LibraryImport", content, StringComparison.Ordinal);
            Assert.DoesNotContain("S7.NetCore.Tests.Native", content, StringComparison.Ordinal);
        }

        Assert.True(
            File.Exists(Path.Combine(AppContext.BaseDirectory, "snap7.dll")),
            "Windows 测试输出缺少 snap7.dll。");
    }

    [Fact]
    public void S_M02_BuildIsolationKeepsProductionWarningsAsErrors()
    {
        string rootProps = File.ReadAllText(Path.Combine(Root, "Directory.Build.props"));
        string nativeProps = File.ReadAllText(
            Path.Combine(Root, "tests", "S7.NetCore.Tests.Native", "Directory.Build.props"));

        Assert.Contains("<TreatWarningsAsErrors>true</TreatWarningsAsErrors>", rootProps, StringComparison.Ordinal);
        Assert.Contains("<TreatWarningsAsErrors>false</TreatWarningsAsErrors>", nativeProps, StringComparison.Ordinal);
        Assert.Contains("<Nullable>disable</Nullable>", nativeProps, StringComparison.Ordinal);
        Assert.Contains("<EnableNETAnalyzers>false</EnableNETAnalyzers>", nativeProps, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
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

        throw new InvalidOperationException("未能定位仓库根。");
    }
}