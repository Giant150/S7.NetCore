using System.Reflection;
using System.Runtime.InteropServices;
using Snap7;

namespace S7.NetCore.Tests.VirtualPlc.Server;

internal sealed record VirtualPlcNativeLibraryProbe(
    bool Available,
    VirtualPlcSkipReason Reason,
    string Detail,
    string? Path);

internal static class VirtualPlcNativeLibrary
{
    private static readonly object Sync = new();
    private static bool _resolverRegistered;

    internal static string LibraryFileName => OperatingSystem.IsWindows() ? "snap7.dll" : "libsnap7.so";

    internal static VirtualPlcNativeLibraryProbe Probe(string? explicitPath)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            return new VirtualPlcNativeLibraryProbe(
                false,
                VirtualPlcSkipReason.PlatformUnsupported,
                $"不支持的平台：{RuntimeInformation.OSDescription}",
                null);
        }

        string? path = ResolvePath(explicitPath);
        if (path is null)
        {
            return new VirtualPlcNativeLibraryProbe(
                false,
                VirtualPlcSkipReason.LibraryMissing,
                $"未找到原生库 {LibraryFileName}（输出目录或测试项目目录）。",
                null);
        }

        try
        {
            if (!NativeLibrary.TryLoad(path, out IntPtr handle))
            {
                return new VirtualPlcNativeLibraryProbe(
                    false,
                    VirtualPlcSkipReason.LibraryMissing,
                    $"无法加载原生库：{path}",
                    path);
            }

            NativeLibrary.Free(handle);
            return new VirtualPlcNativeLibraryProbe(true, default, string.Empty, path);
        }
        catch (BadImageFormatException exception)
        {
            return new VirtualPlcNativeLibraryProbe(
                false,
                VirtualPlcSkipReason.LibraryBadImage,
                $"{path} 架构不匹配：{exception.Message}",
                path);
        }
        catch (Exception exception)
        {
            return new VirtualPlcNativeLibraryProbe(
                false,
                VirtualPlcSkipReason.LibraryMissing,
                $"原生库加载失败：{exception.Message}",
                path);
        }
    }

    internal static void EnsureResolver(string? explicitPath)
    {
        string? path = ResolvePath(explicitPath);
        if (path is null)
        {
            return;
        }

        lock (Sync)
        {
            if (_resolverRegistered)
            {
                return;
            }

            NativeLibrary.SetDllImportResolver(
                typeof(S7Server).Assembly,
                (libraryName, _, _) =>
                {
                    if (libraryName.Equals("snap7.dll", StringComparison.OrdinalIgnoreCase)
                        || libraryName.Equals("libsnap7.so", StringComparison.OrdinalIgnoreCase))
                    {
                        return NativeLibrary.TryLoad(path, out IntPtr handle) ? handle : IntPtr.Zero;
                    }

                    return IntPtr.Zero;
                });

            _resolverRegistered = true;
        }
    }

    private static string? ResolvePath(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            if (File.Exists(explicitPath))
            {
                return explicitPath;
            }

            string combined = Path.Combine(explicitPath, LibraryFileName);
            if (File.Exists(combined))
            {
                return combined;
            }

            return null;
        }

        string inOutput = Path.Combine(AppContext.BaseDirectory, LibraryFileName);
        if (File.Exists(inOutput))
        {
            return inOutput;
        }

        string? root = FindRepositoryRoot();
        if (root is not null)
        {
            string inTests = Path.Combine(root, "tests", "S7.NetCore.Tests", LibraryFileName);
            if (File.Exists(inTests))
            {
                return inTests;
            }
        }

        return null;
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