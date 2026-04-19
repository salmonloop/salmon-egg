using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Linq;
using Xunit;
using SalmonEgg.Infrastructure.Services;

namespace SalmonEgg.Infrastructure.Tests.Services;

public class PlatformShellServiceTests
{
    [Fact]
    public void CreateShellExecuteInfo_WhenPathStartsWithDash_UsesDoubleDash()
    {
        // Act
        var result = PlatformShellService.CreateShellExecuteInfo("-foo");

        // Assert
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.True(result.UseShellExecute);
            Assert.Equal("-foo", result.FileName);
        }
        else
        {
            Assert.False(result.UseShellExecute);
            Assert.Equal(RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "open" : "xdg-open", result.FileName);
            Assert.Equal(2, result.ArgumentList.Count);
            Assert.Equal("--", result.ArgumentList[0]);
            Assert.Equal("-foo", result.ArgumentList[1]);
        }
    }

    [Fact]
    public void CreateShellExecuteInfo_WhenPathStartsWithDoubleDash_UsesDoubleDash()
    {
        // Act
        var result = PlatformShellService.CreateShellExecuteInfo("--env");

        // Assert
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.True(result.UseShellExecute);
            Assert.Equal("--env", result.FileName);
        }
        else
        {
            Assert.False(result.UseShellExecute);
            Assert.Equal(RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "open" : "xdg-open", result.FileName);
            Assert.Equal(2, result.ArgumentList.Count);
            Assert.Equal("--", result.ArgumentList[0]);
            Assert.Equal("--env", result.ArgumentList[1]);
        }
    }

    [Fact]
    public void CreateShellExecuteInfo_WhenPathIsNormal_UsesDoubleDash()
    {
        // Act
        var result = PlatformShellService.CreateShellExecuteInfo("normal/path/file.txt");

        // Assert
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.True(result.UseShellExecute);
            Assert.Equal("normal/path/file.txt", result.FileName);
        }
        else
        {
            Assert.False(result.UseShellExecute);
            Assert.Equal(RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "open" : "xdg-open", result.FileName);
            Assert.Equal(2, result.ArgumentList.Count);
            Assert.Equal("--", result.ArgumentList[0]);
            Assert.Equal("normal/path/file.txt", result.ArgumentList[1]);
        }
    }

    [Fact]
    public void CreateShellExecuteInfo_WhenPathIsDoubleDash_UsesDoubleDash()
    {
        // Act
        var result = PlatformShellService.CreateShellExecuteInfo("--");

        // Assert
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.True(result.UseShellExecute);
            Assert.Equal("--", result.FileName);
        }
        else
        {
            Assert.False(result.UseShellExecute);
            Assert.Equal(RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "open" : "xdg-open", result.FileName);
            Assert.Equal(2, result.ArgumentList.Count);
            Assert.Equal("--", result.ArgumentList[0]);
            Assert.Equal("--", result.ArgumentList[1]);
        }
    }
}
