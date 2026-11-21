using S7.NetCore;

namespace S7.NetCore.Tests.VirtualPlc;

[Collection(VirtualPlcUnitCollection.Name)]
[Trait("Category", "VirtualPlc")]
public sealed class TypedDb1ReadOnlyTests
{
    [Fact]
    public void Db1_HasNoTypedOrByteWriteCalls()
    {
        string? directory = FindVirtualPlcDirectory();
        Assert.True(
            directory is not null,
            $"未找到期望目录 'tests/S7.NetCore.Tests/VirtualPlc'（查找起点：{AppContext.BaseDirectory}）。");

        foreach (string fileName in new[]
        {
            "TypedWriteRoundTripTests.cs",
            "WriteRoundTripTests.cs",
            "TypedWStringTests.cs"
        })
        {
            string path = Path.Combine(directory!, fileName);
            Assert.True(File.Exists(path), $"未找到测试源文件：{path}");
            string content = File.ReadAllText(path);

            Assert.DoesNotContain(
                "new S7TypedWriteRequest(\"DB1.",
                content,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "S7Area.DataBlock, 1,",
                content,
                StringComparison.Ordinal);
        }
    }

    private static string? FindVirtualPlcDirectory()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            string candidate = Path.Combine(
                directory.FullName,
                "tests/S7.NetCore.Tests/VirtualPlc");

            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }
}