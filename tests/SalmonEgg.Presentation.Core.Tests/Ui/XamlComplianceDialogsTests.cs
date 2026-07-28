using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

using SalmonEgg.Presentation.Core.Services.Input;

namespace SalmonEgg.Presentation.Core.Tests.Ui;

using static SalmonEgg.Presentation.Core.Tests.Ui.XamlComplianceTestHelpers;

public sealed class XamlComplianceDialogsTests
{

    [Fact]
    public void RemoteProjectSelectionDialog_UsesFluentTextResources()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Navigation\RemoteProjectSelectionDialog.xaml");

        Assert.Contains("<ContentDialog.Resources>", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectionMode=\"Single\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{ThemeResource BodyStrongTextBlockStyle}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{ThemeResource CaptionTextBlockStyle}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Foreground=\"{ThemeResource TextFillColorSecondaryBrush}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("FontSize=\"14\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("FontSize=\"12\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("FontSize=\"16\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Opacity=", xaml, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"SalmonEgg\SalmonEgg\Presentation\Views\Navigation\RemoteProjectSelectionDialog.xaml", "navViews:RemoteProjectSelectionDialog")]
    [InlineData(@"SalmonEgg\SalmonEgg\Presentation\Views\Navigation\SessionsListDialog.xaml", "navViews:SessionsListDialog")]
    [InlineData(@"SalmonEgg\SalmonEgg\Presentation\Views\ConfigurationEditorDialog.xaml", "views:ConfigurationEditorDialog")]
    public void CustomContentDialogs_BasedOnDefaultContentDialogStyle(string relativePath, string targetType)
    {
        var document = XDocument.Parse(LoadXaml(relativePath));
        var style = FindImplicitStyleByTargetType(document, targetType);

        Assert.Equal("{StaticResource DefaultContentDialogStyle}", GetAttributeByLocalName(style, "BasedOn"));
    }

    [Fact]
    public void Dialogs_DoNotForceDesktopMinimumWidth()
    {
        var sessionsDialog = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Navigation\SessionsListDialog.xaml");
        var configurationDialog = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\ConfigurationEditorDialog.xaml");

        Assert.DoesNotContain("MinWidth=\"420\"", sessionsDialog, StringComparison.Ordinal);
        Assert.DoesNotContain("MinWidth=\"400\"", configurationDialog, StringComparison.Ordinal);
        Assert.Contains("MaxWidth=\"560\"", sessionsDialog, StringComparison.Ordinal);
        Assert.Contains("MaxWidth=\"560\"", configurationDialog, StringComparison.Ordinal);
    }

    [Fact]
    public void SessionsListDialog_TextsAreLocalized()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Navigation\SessionsListDialog.xaml");

        Assert.DoesNotContain("PlaceholderText=\"搜索会话\"", xaml);
        Assert.Contains("x:Uid=\"SessionsDialog\"", xaml);
        Assert.Contains("x:Uid=\"SessionsDialogSearchBox\"", xaml);
    }

    [Fact]
    public void ComboBoxes_DoNotUseDisplayMemberPath_ForUnoWasm()
    {
        var chatInputXaml = LoadXaml(@"SalmonEgg\SalmonEgg\Controls\ChatInputArea.xaml");
        var dialogXaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\ConfigurationEditorDialog.xaml");
        var editorXaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AgentProfileEditorPage.xaml");

        Assert.DoesNotContain("DisplayMemberPath=", chatInputXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("DisplayMemberPath=\"Name\"", dialogXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("DisplayMemberPath=\"Name\"", editorXaml, StringComparison.Ordinal);
        Assert.Contains("x:DataType=\"selectors:ComposerSelectorItemViewModel\"", chatInputXaml, StringComparison.Ordinal);
        Assert.Contains("x:DataType=\"vm:TransportOption\"", dialogXaml, StringComparison.Ordinal);
        Assert.Contains("x:DataType=\"vm:TransportOption\"", editorXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{x:Bind DisplayName, Mode=OneWay}\"", chatInputXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{x:Bind Name, Mode=OneWay}\"", dialogXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{x:Bind Name, Mode=OneWay}\"", editorXaml, StringComparison.Ordinal);
    }
}
