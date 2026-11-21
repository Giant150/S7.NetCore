using System.Reflection;
using System.Security.Cryptography;
using S7.NetCore;

namespace S7.NetCore.Tests.Naming;

[Trait("Category", "Naming")]
public sealed class NamingConsistencyTests
{
    private static readonly string Root = FindRepositoryRoot();

    [Fact]
    public void N01_NoLegacyDirectories()
    {
        Assert.False(
            Directory.Exists(Path.Combine(Root, "src", "Snap7.NetCore")),
            "N-01 FAIL: 仍存在旧目录 src/Snap7.NetCore");
        Assert.False(
            Directory.Exists(Path.Combine(Root, "tests", "Snap7.NetCore.Tests")),
            "N-01 FAIL: 仍存在旧目录 tests/Snap7.NetCore.Tests");
    }

    [Fact]
    public void N02_NoLegacyProjectOrSolutionFiles()
    {
        Assert.False(
            File.Exists(Path.Combine(Root, "Snap7.NetCore.sln")),
            "N-02 FAIL: 仍存在旧解决方案 Snap7.NetCore.sln");
        Assert.False(
            File.Exists(Path.Combine(Root, "src", "S7.NetCore", "Snap7.NetCore.csproj")),
            "N-02 FAIL: 仍存在旧主项目文件");
        Assert.False(
            File.Exists(Path.Combine(Root, "tests", "S7.NetCore.Tests", "Snap7.NetCore.Tests.csproj")),
            "N-02 FAIL: 仍存在旧测试项目文件");
    }

    [Fact]
    public void N03_SolutionListsTwoNewProjects()
    {
        string solution = File.ReadAllText(Path.Combine(Root, "S7.NetCore.sln"));

        Assert.Contains("src\\S7.NetCore\\S7.NetCore.csproj", solution, StringComparison.Ordinal);
        Assert.Contains("tests\\S7.NetCore.Tests\\S7.NetCore.Tests.csproj", solution, StringComparison.Ordinal);
        Assert.DoesNotContain("Snap7.NetCore", solution, StringComparison.Ordinal);

        int projectCount = solution
            .Split('\n')
            .Count(line => line.TrimStart().StartsWith("Project(", StringComparison.Ordinal));
        Assert.Equal(2, projectCount);
    }

    [Fact]
    public void N04_MainProjectProperties()
    {
        string project = File.ReadAllText(Path.Combine(Root, "src", "S7.NetCore", "S7.NetCore.csproj"));

        Assert.Contains("<RootNamespace>S7.NetCore</RootNamespace>", project, StringComparison.Ordinal);
        Assert.Contains("<AssemblyName>S7.NetCore</AssemblyName>", project, StringComparison.Ordinal);
        Assert.Contains("<InternalsVisibleTo Include=\"S7.NetCore.Tests\" />", project, StringComparison.Ordinal);
    }

    [Fact]
    public void N05_TestProjectProperties()
    {
        string project = File.ReadAllText(
            Path.Combine(Root, "tests", "S7.NetCore.Tests", "S7.NetCore.Tests.csproj"));

        Assert.Contains("<AssemblyName>S7.NetCore.Tests</AssemblyName>", project, StringComparison.Ordinal);
        Assert.Contains("<RootNamespace>S7.NetCore.Tests</RootNamespace>", project, StringComparison.Ordinal);
        Assert.Contains(
            "<ProjectReference Include=\"..\\..\\src\\S7.NetCore\\S7.NetCore.csproj\" />",
            project,
            StringComparison.Ordinal);
        Assert.Contains("<Compile Remove=\"snap7.net.cs\" />", project, StringComparison.Ordinal);
        Assert.Contains("<None Include=\"snap7.net.cs\" />", project, StringComparison.Ordinal);
        Assert.Contains(
            "..\\S7.NetCore.Tests.Native\\S7.NetCore.Tests.Native.csproj",
            project,
            StringComparison.Ordinal);
        Assert.Contains("<None Update=\"snap7.dll\">", project, StringComparison.Ordinal);
    }

    [Fact]
    public void N06AndN07_NoLegacyNamespaceOrUsingPrefix()
    {
        foreach (string file in EnumerateSourceFiles())
        {
            string content = File.ReadAllText(file);
            int namespaceIndex = content.IndexOf("namespace Snap7.NetCore", StringComparison.Ordinal);
            Assert.True(
                namespaceIndex < 0,
                $"N-06 FAIL: {Relative(file)} 仍含 namespace Snap7.NetCore");

            int usingIndex = content.IndexOf("using Snap7.NetCore", StringComparison.Ordinal);
            Assert.True(
                usingIndex < 0,
                $"N-07 FAIL: {Relative(file)} 仍含 using Snap7.NetCore");
        }
    }

    [Fact]
    public void N08_TestDirectoryLiteralUsesNewPath()
    {
        string content = File.ReadAllText(
            Path.Combine(Root, "tests", "S7.NetCore.Tests", "VirtualPlc", "TypedDb1ReadOnlyTests.cs"));

        Assert.Contains("tests/S7.NetCore.Tests/VirtualPlc", content, StringComparison.Ordinal);
        Assert.Contains("未找到期望目录 'tests/S7.NetCore.Tests/VirtualPlc'", content, StringComparison.Ordinal);
    }

    [Fact]
    public void N09_NoLegacyIdentifierPrefix()
    {
        foreach (string file in EnumerateSourceFiles())
        {
            string content = File.ReadAllText(file);
            Assert.DoesNotContain("namespace Snap7", content, StringComparison.Ordinal);
            Assert.DoesNotContain("class Snap7", content, StringComparison.Ordinal);
            Assert.DoesNotContain("Snap7LibName", content, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void N10_EnvironmentVariablesPreserved()
    {
        string content = File.ReadAllText(
            Path.Combine(Root, "tests", "S7.NetCore.Tests", "VirtualPlc", "VirtualPlcEndpoint.cs"))
            + File.ReadAllText(
                Path.Combine(Root, "tests", "S7.NetCore.Tests", "VirtualPlc", "VirtualPlcEnvironment.cs"))
            + File.ReadAllText(
                Path.Combine(
                    Root,
                    "tests",
                    "S7.NetCore.Tests",
                    "VirtualPlc",
                    "Server",
                    "VirtualPlcServerSettings.cs"));

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
            Assert.Contains(variable, content, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void N11_DocumentCommandsUseNewTargets()
    {
        string readme = File.ReadAllText(Path.Combine(Root, "README.md"));

        Assert.Contains("dotnet build S7.NetCore.sln", readme, StringComparison.Ordinal);
        Assert.Contains("dotnet test S7.NetCore.sln", readme, StringComparison.Ordinal);
        Assert.Contains("src/S7.NetCore/S7.NetCore.csproj", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet build Snap7.NetCore.sln", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet test Snap7.NetCore.sln", readme, StringComparison.Ordinal);
    }

    [Fact]
    public void N12_AssemblyNameIsNew()
    {
        Assembly assembly = typeof(S7Client).Assembly;
        Assert.Equal("S7.NetCore", assembly.GetName().Name);
    }

    [Fact]
    public void N13_NoLegacyNameOutsideWhitelist()
    {
        string[] projectFiles =
        [
            Path.Combine(Root, "S7.NetCore.sln"),
            Path.Combine(Root, "Directory.Build.props"),
            Path.Combine(Root, "src", "S7.NetCore", "S7.NetCore.csproj"),
            Path.Combine(Root, "tests", "S7.NetCore.Tests", "S7.NetCore.Tests.csproj")
        ];

        foreach (string file in projectFiles)
        {
            string content = File.ReadAllText(file);
            Assert.True(
                !content.Contains("Snap7.NetCore", StringComparison.Ordinal),
                $"N-13 FAIL: {Relative(file)} 含旧名 Snap7.NetCore");
        }

        string readme = File.ReadAllText(Path.Combine(Root, "README.md"));
        int migrationIndex = readme.IndexOf("## 变更记录与迁移", StringComparison.Ordinal);
        Assert.True(migrationIndex > 0, "N-13 FAIL: README 缺少受控迁移段");
        string beforeMigration = readme[..migrationIndex];
        Assert.DoesNotContain("Snap7.NetCore", beforeMigration, StringComparison.Ordinal);

        foreach (string file in EnumerateSourceFiles())
        {
            string content = File.ReadAllText(file);
            Assert.True(
                !content.Contains("Snap7.NetCore", StringComparison.Ordinal),
                $"N-13 FAIL: {Relative(file)} 含旧名 Snap7.NetCore");
        }
    }

    [Fact]
    public void N14_NoCompatibilityLayer()
    {
        foreach (string file in EnumerateSourceFiles())
        {
            string content = File.ReadAllText(file);
            Assert.DoesNotContain("TypeForwardedTo", content, StringComparison.Ordinal);
            Assert.DoesNotContain("Snap7.NetCore.dll", content, StringComparison.Ordinal);
        }

        string output = Path.GetDirectoryName(typeof(S7Client).Assembly.Location)!;
        Assert.False(File.Exists(Path.Combine(output, "Snap7.NetCore.dll")), "N-14 FAIL: 输出目录存在旧程序集");
        Assert.False(File.Exists(Path.Combine(output, "Snap7.NetCore.Tests.dll")), "N-14 FAIL: 输出目录存在旧测试程序集");
    }

    [Fact]
    public void N15_VersionIsMajor2()
    {
        string props = File.ReadAllText(Path.Combine(Root, "Directory.Build.props"));
        Assert.Contains("<VersionPrefix>2.1.0</VersionPrefix>", props, StringComparison.Ordinal);

        Version? version = typeof(S7Client).Assembly.GetName().Version;
        Assert.NotNull(version);
        Assert.Equal(2, version!.Major);
    }

    [Fact]
    public void N16_NativeBridgeSourceIsVerbatim()
    {
        const string expectedHash = "F0C917F51A8938A1A9A752B6821CF11857214479A4542901C9225A5E46765B16";
        string path = Path.Combine(Root, "tests", "S7.NetCore.Tests", "snap7.net.cs");

        using SHA256 sha = SHA256.Create();
        string hash = Convert.ToHexString(sha.ComputeHash(File.ReadAllBytes(path)));
        Assert.Equal(expectedHash, hash);

        string content = File.ReadAllText(path);
        Assert.Contains("namespace Snap7", content, StringComparison.Ordinal);
        Assert.DoesNotContain("#nullable", content, StringComparison.Ordinal);
        Assert.DoesNotContain("#pragma", content, StringComparison.Ordinal);
    }

    [Fact]
    public void N17_ProductionHasNoNativeDependency()
    {
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
            Assert.DoesNotContain("NativeLibrary", content, StringComparison.Ordinal);
        }

        string project = File.ReadAllText(Path.Combine(Root, "src", "S7.NetCore", "S7.NetCore.csproj"));
        Assert.DoesNotContain("Native", project, StringComparison.Ordinal);
        Assert.DoesNotContain("snap7", project, StringComparison.OrdinalIgnoreCase);

        string outputRoot = Path.Combine(Root, "src", "S7.NetCore", "bin");
        if (Directory.Exists(outputRoot))
        {
            foreach (string file in Directory.EnumerateFiles(outputRoot, "*", SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(file);
                Assert.False(name.Equals("snap7.dll", StringComparison.OrdinalIgnoreCase), "N-17 FAIL: 生产输出含 snap7.dll");
                Assert.False(name.Equals("libsnap7.so", StringComparison.OrdinalIgnoreCase), "N-17 FAIL: 生产输出含 libsnap7.so");
                Assert.False(
                    name.Equals("S7.NetCore.Tests.Native.dll", StringComparison.OrdinalIgnoreCase),
                    "N-17 FAIL: 生产输出含桥接程序集");
            }
        }
    }

    [Fact]
    public void N18_TestSideHasNativeCapability()
    {
        string testProject = File.ReadAllText(
            Path.Combine(Root, "tests", "S7.NetCore.Tests", "S7.NetCore.Tests.csproj"));
        string nativeProject = File.ReadAllText(
            Path.Combine(Root, "tests", "S7.NetCore.Tests.Native", "S7.NetCore.Tests.Native.csproj"));

        Assert.Contains(
            "..\\S7.NetCore.Tests.Native\\S7.NetCore.Tests.Native.csproj",
            testProject,
            StringComparison.Ordinal);
        Assert.Contains(
            "..\\S7.NetCore.Tests\\snap7.net.cs",
            nativeProject,
            StringComparison.Ordinal);
        Assert.True(
            File.Exists(Path.Combine(AppContext.BaseDirectory, "snap7.dll")),
            "N-18 FAIL: Windows 测试输出缺少 snap7.dll");
    }

    [Fact]
    public void N19_NativeTypesAreIsolatedToServerFolder()
    {
        string serverSegment = $"{Path.DirectorySeparatorChar}Server{Path.DirectorySeparatorChar}";
        string namingSegment = $"{Path.DirectorySeparatorChar}Naming{Path.DirectorySeparatorChar}";

        foreach (string file in Directory.EnumerateFiles(
            Path.Combine(Root, "tests", "S7.NetCore.Tests"),
            "*.cs",
            SearchOption.AllDirectories))
        {
            if (file.Contains(serverSegment, StringComparison.Ordinal)
                || file.Contains(namingSegment, StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.EndsWith("snap7.net.cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string content = File.ReadAllText(file);
            Assert.DoesNotContain("using Snap7;", content, StringComparison.Ordinal);
            Assert.DoesNotContain("Snap7.S7Server", content, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void N20_EndpointBaselineIs1102()
    {
        string endpoint = File.ReadAllText(
            Path.Combine(Root, "tests", "S7.NetCore.Tests", "VirtualPlc", "VirtualPlcEndpoint.cs"));
        string settings = File.ReadAllText(
            Path.Combine(Root, "tests", "S7.NetCore.Tests", "VirtualPlc", "Server", "VirtualPlcServerSettings.cs"));

        Assert.Contains("new(\"127.0.0.1\", 1102", endpoint, StringComparison.Ordinal);
        Assert.Contains("\"1102\"", settings, StringComparison.Ordinal);

        foreach (string variable in new[]
        {
            "S7_VIRTUAL_PLC_SERVER",
            "S7_VIRTUAL_PLC_SERVER_LIBRARY",
            "S7_VIRTUAL_PLC_SERVER_MAX_CLIENTS"
        })
        {
            Assert.Contains(variable, settings, StringComparison.Ordinal);
        }
    }

    private static IEnumerable<string> EnumerateSourceFiles()
    {
        string namingSegment = $"{Path.DirectorySeparatorChar}Naming{Path.DirectorySeparatorChar}";

        foreach (string directory in new[]
        {
            Path.Combine(Root, "src", "S7.NetCore"),
            Path.Combine(Root, "tests", "S7.NetCore.Tests")
        })
        {
            foreach (string file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    || file.Contains(namingSegment, StringComparison.Ordinal)
                    || file.EndsWith("snap7.net.cs", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                yield return file;
            }
        }
    }

    private static string Relative(string path)
    {
        return Path.GetRelativePath(Root, path);
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

        throw new InvalidOperationException(
            $"N-00 FAIL: 未能从 '{AppContext.BaseDirectory}' 定位仓库根（期望包含 S7.NetCore.sln）。");
    }
}