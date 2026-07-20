using System.Globalization;
using System.Threading;
using SalmonEgg.Acp.Content;
using SalmonEgg.Presentation.ViewModels.Chat;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public sealed class ResourceViewModelTests
{
    [Theory]
    [InlineData(0L, "0.00 B")]
    [InlineData(512L, "512.00 B")]
    [InlineData(1024L, "1.00 KB")]
    [InlineData(1536L, "1.50 KB")]
    [InlineData(1048576L, "1.00 MB")]
    [InlineData(1073741824L, "1.00 GB")]
    public void SizeDisplay_FormatsBytesWithInvariantScale(long bytes, string expected)
    {
        // FormatSize uses CurrentCulture, so the test pins the culture to keep the
        // decimal separator deterministic across Linux/Windows CI locales.
        var originalCulture = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");

            var viewModel = new ResourceViewModel { Size = bytes };

            Assert.Equal(expected, viewModel.SizeDisplay);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void SizeDisplay_WhenSizeIsNull_ReturnsEmpty()
    {
        var viewModel = new ResourceViewModel { Size = null };

        Assert.Equal(string.Empty, viewModel.SizeDisplay);
    }

    [Theory]
    [InlineData("image/png", "\U0001F5BC\uFE0F")]
    [InlineData("video/mp4", "\U0001F3AC")]
    [InlineData("audio/mpeg", "\U0001F3B5")]
    [InlineData("text/plain", "\U0001F4C4")]
    [InlineData("application/json", "\U0001F4CB")]
    [InlineData("application/pdf", "\U0001F4D5")]
    [InlineData("application/octet-stream", "\U0001F4CE")]
    [InlineData("", "\U0001F4CE")]
    public void TypeIcon_SelectsByMimeTypePrefixOrKeyword(string mimeType, string expected)
    {
        var viewModel = new ResourceViewModel { MimeType = mimeType };

        Assert.Equal(expected, viewModel.TypeIcon);
    }

    [Fact]
    public void DisplayTitle_FallsBackTitleThenNameThenUri()
    {
        Assert.Equal("T", new ResourceViewModel { Title = "T", Name = "N", Uri = "U" }.DisplayTitle);
        Assert.Equal("N", new ResourceViewModel { Title = "", Name = "N", Uri = "U" }.DisplayTitle);
        Assert.Equal("U", new ResourceViewModel { Title = "", Name = "", Uri = "U" }.DisplayTitle);
    }

    [Fact]
    public void GetDisplayContent_PrefersEmbeddedContentOverLinkText()
    {
        // IsResourceContent and IsResourceLink are independent flags; the content branch wins
        // for GetDisplayContent only when Content is present.
        var content = new ResourceViewModel { Content = "body", LinkText = string.Empty };
        Assert.True(content.IsResourceContent);
        Assert.False(content.IsResourceLink);
        Assert.Equal("body", content.GetDisplayContent());

        var link = new ResourceViewModel { Content = string.Empty, LinkText = "link" };
        Assert.False(link.IsResourceContent);
        Assert.True(link.IsResourceLink);
        Assert.Equal("link", link.GetDisplayContent());
    }

    [Fact]
    public void CreateFromContent_ProjectsTextResourceFieldsAndExtractsNameFromUri()
    {
        var block = ResourceContentBlock.CreateText("file:///repo/note.txt", "hello", "text/plain");

        var viewModel = ResourceViewModel.CreateFromContent(block);

        Assert.Equal("file:///repo/note.txt", viewModel.Uri);
        Assert.Equal("note.txt", viewModel.Name);
        Assert.Equal("text/plain", viewModel.MimeType);
        Assert.Equal("hello", viewModel.Content);
        Assert.True(viewModel.IsTextResource);
        Assert.False(viewModel.IsBinaryResource);
        Assert.Equal(5, viewModel.Size);
        Assert.True(viewModel.IsResourceContent);
    }

    [Fact]
    public void CreateFromLink_ProjectsLinkFieldsAndFallsBackToUriForName()
    {
        var block = new ResourceLinkContentBlock(
            uri: "https://example.com/docs/report.pdf",
            name: null,
            title: "Report");

        var viewModel = ResourceViewModel.CreateFromLink(block);

        Assert.Equal("https://example.com/docs/report.pdf", viewModel.Uri);
        Assert.Equal("report.pdf", viewModel.Name);
        Assert.Equal("https://example.com/docs/report.pdf", viewModel.LinkText);
        Assert.Equal("Report", viewModel.Title);
        Assert.True(viewModel.IsResourceLink);
        Assert.False(viewModel.IsResourceContent);
    }

    [Fact]
    public void CreateFromLink_UsesProvidedNameWhenPresent()
    {
        var block = new ResourceLinkContentBlock(uri: "https://host/x.md", name: "Display Name");

        Assert.Equal("Display Name", ResourceViewModel.CreateFromLink(block).Name);
    }
}
