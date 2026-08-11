using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

using SalmonEgg.Presentation.Core.Services.Input;

namespace SalmonEgg.Presentation.Core.Tests.Ui;

internal static class XamlComplianceTestHelpers
{
    internal static bool IsValueSelectorRequiringFocusEngagement(XElement element)
    {
        return element.Name.LocalName is "ComboBox" or "NumberBox";
    }


    internal static string LoadXaml(string relativePath)
    {
        return LoadText(relativePath);
    }

    internal static string LoadText(string relativePath)
    {
        var root = FindRepoRoot();
        var fullPath = Path.Combine(root, NormalizeRelativePath(relativePath));
        return File.ReadAllText(fullPath);
    }

    internal static string[] EnumerateProductCSharpFiles()
    {
        var root = FindRepoRoot();
        var sourceRoots = new[]
        {
            Path.Combine(root, "src"),
            Path.Combine(root, "SalmonEgg", "SalmonEgg")
        };

        return sourceRoots
            .Where(Directory.Exists)
            .SelectMany(sourceRoot => Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    internal static string ExtractSection(string content, string startMarker, string? endMarker = null)
    {
        var start = content.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Unable to locate marker '{startMarker}'.");

        var end = endMarker is null
            ? content.Length
            : content.IndexOf(endMarker, start, StringComparison.Ordinal);
        if (end < 0)
        {
            end = content.Length;
        }

        return content.Substring(start, end - start);
    }

    internal static void AssertTitleBarCommandTargetsMainNavigationOnGamepadDown(string xaml, string controlName)
    {
        var controlSection = ExtractSection(
            xaml,
            $"x:Name=\"{controlName}\"",
            ">");

        Assert.Contains("XYFocusDown=\"{x:Bind MainNavView, Mode=OneWay}\"", controlSection, StringComparison.Ordinal);
    }

    internal static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SalmonEgg.sln")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root (SalmonEgg.sln) not found.");
    }

    internal static XElement FindElementByName(string relativePath, string elementName)
    {
        var document = XDocument.Parse(LoadXaml(relativePath));
        var element = document.Descendants().FirstOrDefault(candidate =>
            candidate.Attributes().Any(attribute =>
                string.Equals(attribute.Name.LocalName, "Name", StringComparison.Ordinal)
                && string.Equals(attribute.Value, elementName, StringComparison.Ordinal)));
        if (element is null)
        {
            throw new InvalidOperationException($"Element '{elementName}' not found in XAML '{relativePath}'.");
        }

        return element;
    }

    internal static XElement FindElementByUid(XDocument document, string uid)
    {
        var element = document.Descendants().FirstOrDefault(candidate =>
            candidate.Attributes().Any(attribute =>
                string.Equals(attribute.Name.LocalName, "Uid", StringComparison.Ordinal)
                && string.Equals(attribute.Value, uid, StringComparison.Ordinal)));
        if (element is null)
        {
            throw new InvalidOperationException($"Element with x:Uid '{uid}' not found.");
        }

        return element;
    }

    internal static void AssertCollapsedExpanderOwnsUid(XDocument document, string uid)
    {
        var expander = AssertExpanderOwnsUid(document, uid);

        Assert.Equal("False", GetAttributeByLocalName(expander, "IsExpanded"));
    }

    internal static XElement AssertExpanderOwnsUid(XDocument document, string uid)
    {
        var element = FindElementByUid(document, uid);
        return Assert.Single(
            element.Ancestors(),
            ancestor => ancestor.Name.LocalName == "Expander");
    }

    internal static void AssertTitleBarStyleKeepsNativeRoundedBorderlessAppearance(
        XDocument document,
        string styleKey,
        string expectedCornerRadius)
    {
        var xNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var style = document.Descendants()
            .FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "Style", StringComparison.Ordinal)
                && string.Equals(element.Attribute(xNamespace + "Key")?.Value, styleKey, StringComparison.Ordinal));

        Assert.NotNull(style);
        Assert.Contains(style!.Descendants().Where(element => string.Equals(element.Name.LocalName, "Setter", StringComparison.Ordinal)),
            setter => string.Equals(setter.Attribute("Property")?.Value, "CornerRadius", StringComparison.Ordinal)
                && string.Equals(setter.Attribute("Value")?.Value, expectedCornerRadius, StringComparison.Ordinal));
        Assert.Contains(style.Descendants().Where(element => string.Equals(element.Name.LocalName, "Setter", StringComparison.Ordinal)),
            setter => string.Equals(setter.Attribute("Property")?.Value, "BorderBrush", StringComparison.Ordinal)
                && string.Equals(setter.Attribute("Value")?.Value, "Transparent", StringComparison.Ordinal));
        Assert.Contains(style.Descendants().Where(element => string.Equals(element.Name.LocalName, "Setter", StringComparison.Ordinal)),
            setter => string.Equals(setter.Attribute("Property")?.Value, "BorderThickness", StringComparison.Ordinal)
                && string.Equals(setter.Attribute("Value")?.Value, "0", StringComparison.Ordinal));
    }

    internal static XElement FindStyleByKey(XDocument document, string styleKey)
    {
        var xNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var style = document.Descendants()
            .FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "Style", StringComparison.Ordinal)
                && string.Equals(element.Attribute(xNamespace + "Key")?.Value, styleKey, StringComparison.Ordinal));

        Assert.NotNull(style);
        return style!;
    }

    internal static XElement FindImplicitStyleByTargetType(XDocument document, string targetType)
    {
        var xNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var style = document.Descendants()
            .FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "Style", StringComparison.Ordinal)
                && string.Equals(element.Attribute("TargetType")?.Value, targetType, StringComparison.Ordinal)
                && element.Attribute(xNamespace + "Key") is null);

        Assert.NotNull(style);
        return style!;
    }

    internal static void AssertNoInputStyleOverride(XElement element)
    {
        var style = GetAttributeByLocalName(element, "Style");

        Assert.Null(style);
    }

    internal static void AssertStyleSetter(XElement style, string property, string value)
    {
        Assert.Contains(style.Descendants().Where(element => string.Equals(element.Name.LocalName, "Setter", StringComparison.Ordinal)),
            setter => string.Equals(setter.Attribute("Property")?.Value, property, StringComparison.Ordinal)
                && string.Equals(setter.Attribute("Value")?.Value, value, StringComparison.Ordinal));
    }

    internal static string NormalizeRelativePath(string relativePath)
        => relativePath.Replace('\\', Path.DirectorySeparatorChar);

    internal static int CountOccurrences(string value, string fragment)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(fragment, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += fragment.Length;
        }

        return count;
    }

    internal static bool HasAttributeByLocalName(XElement element, string localName)
        => element.Attributes().Any(attribute => string.Equals(attribute.Name.LocalName, localName, StringComparison.Ordinal));

    internal static bool IsUserVisibleTextAttribute(XAttribute attribute)
    {
        if (attribute.Name.LocalName is not ("Text" or "Content" or "Header" or "PlaceholderText" or "ToolTip" or "Name"))
        {
            return false;
        }

        return attribute.Name.LocalName != "Name"
            || attribute.Name.NamespaceName.EndsWith("/automation", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsHardcodedUserVisibleLiteral(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith('{')
            || trimmed.StartsWith("&#x", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("ms-appx://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("/", StringComparison.Ordinal)
            || trimmed.All(char.IsDigit))
        {
            return false;
        }

        return true;
    }

    internal static bool IsVisibleLiteralWhitelist(string xamlFile, XElement element, XAttribute attribute)
    {
        var fileName = Path.GetFileName(xamlFile);
        var elementName = element.Name.LocalName;
        var value = attribute.Value;

        return elementName is "FontIcon" or "SymbolIcon"
            || string.Equals(value, "Icon", StringComparison.Ordinal)
            || string.Equals(value, "boot", StringComparison.Ordinal)
            || string.Equals(value, "inactive", StringComparison.Ordinal)
            || string.Equals(fileName, "UnoNumberBoxThemeOverrides.xaml", StringComparison.OrdinalIgnoreCase)
                && attribute.Name.LocalName is "Text" or "Content"
                && value is "\uE70D" or "\uE70E" or "\uE894" or "\uEC8F"
            || string.Equals(fileName, "ChatInputArea.xaml", StringComparison.OrdinalIgnoreCase)
                && attribute.Name.LocalName == "Content"
            && value.Length <= 2;
    }

    internal static bool IsChineseUserVisibleTextAttribute(XAttribute attribute)
        => attribute.Name.LocalName is "Text"
            or "Content"
            or "Header"
            or "OnContent"
            or "OffContent"
            or "Title"
            or "Message"
            or "ToolTip"
            or "ToolTipService.ToolTip"
            or "AutomationProperties.Name";

    internal static bool IsCjkUnifiedIdeograph(char value)
        => value is >= '\u3400' and <= '\u9fff';

    internal static string GetResourceValue(XDocument resources, string name)
    {
        var value = resources.Descendants("data")
            .FirstOrDefault(data => string.Equals((string?)data.Attribute("name"), name, StringComparison.Ordinal))
            ?.Element("value")
            ?.Value;

        Assert.False(string.IsNullOrWhiteSpace(value), $"Resource '{name}' must define a non-empty value.");
        return value!;
    }

    internal static string? GetAttributeByLocalName(XElement element, string localName)
        => element.Attributes()
            .FirstOrDefault(attribute => string.Equals(attribute.Name.LocalName, localName, StringComparison.Ordinal))
            ?.Value;

    internal static bool HasXUid(XElement element, string expectedValue)
        => string.Equals(GetAttributeByLocalName(element, "Uid"), expectedValue, StringComparison.Ordinal);
}
