using System;
using System.IO;
using FlaUI.Core.AutomationElements;
using Xunit.Sdk;

namespace SalmonEgg.GuiTests.Windows;

public sealed class CloudSyncSettingsSmokeTests
{
    [Fact]
    public void DataStorageCloudSyncProviderPicker_SwitchesVisibleProviderSetupPane()
    {
        GuiTestGate.RequireEnabled();

        using var appData = GuiAppDataScope.CreateDeterministicLeftNavData();
        using var session = WindowsGuiAppSession.LaunchFresh();

        session.ResizeMainWindow(width: 1400, height: 900);
        NavigateToDataStorageSettings(session, appData);

        Assert.NotNull(FindAndScrollIntoView(session, "DataStorage.CloudSync.ProviderPicker", TimeSpan.FromSeconds(10)));
        Assert.NotNull(FindAndScrollIntoView(session, "DataStorage.CloudSync.ConnectSelected", TimeSpan.FromSeconds(5)));
        Assert.NotNull(FindAndScrollIntoView(session, "DataStorage.CloudSync.SyncNow", TimeSpan.FromSeconds(5)));
        Assert.NotNull(FindAndScrollIntoView(session, "DataStorage.CloudSync.Disconnect", TimeSpan.FromSeconds(5)));

        SelectCloudSyncProvider(session, "S3 compatible");
        Assert.True(
            session.WaitUntilOnscreen("DataStorage.CloudSync.S3Endpoint", TimeSpan.FromSeconds(5)),
            BuildFailureMessage(session, appData, "S3 endpoint field did not become visible after selecting the S3 provider."));
        Assert.False(
            session.IsOnscreen("DataStorage.CloudSync.WebDavFileUrl", TimeSpan.FromMilliseconds(500)),
            BuildFailureMessage(session, appData, "WebDAV fields must not remain visible while the S3 provider is selected."));

        SelectCloudSyncProvider(session, "WebDAV");
        Assert.True(
            session.WaitUntilOnscreen("DataStorage.CloudSync.WebDavFileUrl", TimeSpan.FromSeconds(5)),
            BuildFailureMessage(session, appData, "WebDAV folder URL field did not become visible after selecting the WebDAV provider."));
        Assert.False(
            session.IsOnscreen("DataStorage.CloudSync.S3Endpoint", TimeSpan.FromMilliseconds(500)),
            BuildFailureMessage(session, appData, "S3 fields must not remain visible while the WebDAV provider is selected."));
    }

    private static void NavigateToDataStorageSettings(WindowsGuiAppSession session, GuiAppDataScope appData)
    {
        var settingsItem = session.FindByAutomationId("SettingsItem", TimeSpan.FromSeconds(10));
        session.ActivateElement(settingsItem);

        var dataStorageItem = session.TryFindByAutomationId("SettingsNav.DataStorage", TimeSpan.FromSeconds(10));
        if (dataStorageItem is null)
        {
            throw CreateNavigationFailure(session, appData, "Data storage settings entry did not become visible after opening settings.");
        }

        session.ActivateElement(dataStorageItem);
        if (!session.WaitUntilOnscreen("DataStorage.SaveLocalHistory", TimeSpan.FromSeconds(10)))
        {
            throw CreateNavigationFailure(session, appData, "Data storage settings content did not become visible.");
        }
    }

    private static void SelectCloudSyncProvider(WindowsGuiAppSession session, string providerDisplayName)
    {
        var picker = FindAndScrollIntoView(session, "DataStorage.CloudSync.ProviderPicker", TimeSpan.FromSeconds(10));
        session.ClickElement(picker);

        var providerItem = session.TryFindVisibleElementByNameAnywhere(providerDisplayName, TimeSpan.FromSeconds(5));
        if (providerItem is null)
        {
            throw new XunitException(
                $"Cloud sync provider '{providerDisplayName}' did not appear in the provider picker." + Environment.NewLine +
                $"Visible texts: [{string.Join(", ", session.GetVisibleTexts())}]");
        }

        SelectComboBoxItemElement(session, FindSelectableAncestor(providerItem));
    }

    private static AutomationElement FindAndScrollIntoView(
        WindowsGuiAppSession session,
        string automationId,
        TimeSpan timeout)
    {
        AutomationElement? element = null;
        session.WaitUntil(
            () =>
            {
                element = session.TryFindByAutomationId(automationId, TimeSpan.FromMilliseconds(250));
                if (element is not null)
                {
                    return true;
                }

                session.ScrollWheel(-120);
                return false;
            },
            timeout);

        element ??= session.FindByAutomationId(automationId, TimeSpan.FromMilliseconds(250));
        if (element.Patterns.ScrollItem.IsSupported)
        {
            element.Patterns.ScrollItem.Pattern.ScrollIntoView();
            session.WaitUntil(() => !element.IsOffscreen, TimeSpan.FromSeconds(1));
        }

        return element;
    }

    private static AutomationElement FindSelectableAncestor(AutomationElement element)
    {
        var current = element;
        while (current is not null)
        {
            if (current.Patterns.SelectionItem.IsSupported || current.Patterns.Invoke.IsSupported)
            {
                return current;
            }

            current = current.Parent;
        }

        throw new XunitException("Could not find a selectable ancestor for the cloud sync provider picker item.");
    }

    private static void SelectComboBoxItemElement(WindowsGuiAppSession session, AutomationElement item)
    {
        if (item.Patterns.SelectionItem.IsSupported)
        {
            item.Patterns.SelectionItem.Pattern.Select();
            return;
        }

        session.ActivateElement(item);
    }

    private static XunitException CreateNavigationFailure(
        WindowsGuiAppSession session,
        GuiAppDataScope appData,
        string message)
        => new(BuildFailureMessage(session, appData, message));

    private static string BuildFailureMessage(
        WindowsGuiAppSession session,
        GuiAppDataScope appData,
        string message)
    {
        var captureRoot = Path.Combine(Path.GetTempPath(), "SalmonEgg.GuiTests");
        Directory.CreateDirectory(captureRoot);
        var capturePath = Path.Combine(captureRoot, $"settings-cloud-sync-{DateTime.UtcNow:yyyyMMddHHmmssfff}.png");
        session.CaptureMainWindowToFile(capturePath);

        return message + Environment.NewLine +
            $"Screenshot: {capturePath}" + Environment.NewLine +
            $"Visible texts: [{string.Join(", ", session.GetVisibleTexts())}]" + Environment.NewLine +
            appData.ReadBootLogTail();
    }
}
