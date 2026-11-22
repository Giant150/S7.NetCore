namespace S7.NetCore.Tests.VirtualPlc;

[Collection(VirtualPlcUnitCollection.Name)]
[Trait("Category", "VirtualPlc")]
public sealed class ServerMigrationTests
{
    private static readonly string Root = FindRepositoryRoot();

    [Fact]
    public void S_D03_DefaultEndpointAndEightEnvironmentVariables()
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
            "S7_VIRTUAL_PLC_SERVER_MAX_CLIENTS"
        })
        {
            Assert.Contains(variable, settings, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("S7_VIRTUAL_PLC_SERVER_LIBRARY", settings, StringComparison.Ordinal);
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

        string linuxAsset = "lib" + "snap7";
        Assert.True(
            !testProject.Contains(linuxAsset, StringComparison.Ordinal),
            $"S_M01 FAIL: 测试项目 csproj 仍包含 {linuxAsset}.so。");
        Assert.True(
            !nativeProject.Contains("__MonoCS__", StringComparison.Ordinal),
            "S_M01 FAIL: 桥接项目 csproj 仍包含 __MonoCS__。");

        string platformUnsupported = "Platform" + "Unsupported";
        foreach (string file in Directory.EnumerateFiles(
            Path.Combine(Root, "tests", "S7.NetCore.Tests", "VirtualPlc", "Server"),
            "*.cs",
            SearchOption.AllDirectories))
        {
            string content = File.ReadAllText(file);
            string relative = Path.GetRelativePath(Root, file);
            Assert.True(
                !content.Contains("NativeLibrary", StringComparison.Ordinal),
                $"S_M01 FAIL: {relative} 含 NativeLibrary。");
            Assert.True(
                !content.Contains("SetDllImportResolver", StringComparison.Ordinal),
                $"S_M01 FAIL: {relative} 含 SetDllImportResolver。");
            Assert.True(
                !content.Contains(platformUnsupported, StringComparison.Ordinal),
                $"S_M01 FAIL: {relative} 含 {platformUnsupported}。");
        }
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