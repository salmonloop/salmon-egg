using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using SalmonEgg.Presentation.Core.Resources;
using SalmonEgg.Presentation.Core.Services;
using SalmonEgg.Presentation.Models.Search;

namespace SalmonEgg.Presentation.Core.Services.Search;

public sealed class DefaultGlobalSearchPipeline : IGlobalSearchPipeline
{
    private readonly IStringLocalizer<CoreStrings> _localizer;

    public DefaultGlobalSearchPipeline(IStringLocalizer<CoreStrings> localizer)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
    }

    public Task<GlobalSearchSnapshot> SearchAsync(
        string query,
        GlobalSearchSourceSnapshot source,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(query))
        {
            return Task.FromResult(new GlobalSearchSnapshot(ImmutableArray<GlobalSearchGroupSnapshot>.Empty));
        }

        var normalizedQuery = query.Trim().ToLowerInvariant();

        var groups = new List<GlobalSearchGroupSnapshot>();

        var sessions = SearchSessions(normalizedQuery, source.Sessions);
        cancellationToken.ThrowIfCancellationRequested();
        if (sessions.Items.Length > 0)
        {
            groups.Add(sessions);
        }

        var projects = SearchProjects(normalizedQuery, source.Projects);
        cancellationToken.ThrowIfCancellationRequested();
        if (projects.Items.Length > 0)
        {
            groups.Add(projects);
        }

        var settings = SearchSettings(normalizedQuery);
        cancellationToken.ThrowIfCancellationRequested();
        if (settings.Items.Length > 0)
        {
            groups.Add(settings);
        }

        var commands = SearchCommands(normalizedQuery);
        cancellationToken.ThrowIfCancellationRequested();
        if (commands.Items.Length > 0)
        {
            groups.Add(commands);
        }

        var snapshot = new GlobalSearchSnapshot(
            groups
                .OrderByDescending(group => group.Priority)
                .ToImmutableArray());
        return Task.FromResult(snapshot);
    }

    private GlobalSearchGroupSnapshot SearchSessions(string normalizedQuery, ImmutableArray<GlobalSearchSessionSource> sessions)
    {
        var matches = sessions
            .Select(session => new
            {
                Session = session,
                Score = Math.Max(
                    MatchScore(session.Title, normalizedQuery),
                    MatchScore(session.ConversationId, normalizedQuery))
            })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .Take(10)
            .Select(item => new GlobalSearchItemSnapshot(
                item.Session.ConversationId,
                item.Session.Title,
                item.Session.Cwd,
                SearchResultKind.Session,
                "\uE8BD",
                Tag: null))
            .ToImmutableArray();

        return new GlobalSearchGroupSnapshot(
            Name: "sessions",
            Title: _localizer["Search_Sessions"],
            Priority: 100,
            Items: matches);
    }

    private GlobalSearchGroupSnapshot SearchProjects(string normalizedQuery, ImmutableArray<GlobalSearchProjectSource> projects)
    {
        var items = new List<GlobalSearchItemSnapshot>();
        if (MatchScore(_localizer["Nav_Unclassified"], normalizedQuery) > 0)
        {
            items.Add(new GlobalSearchItemSnapshot(
                NavigationProjectIds.Unclassified,
                _localizer["Nav_Unclassified"],
                Subtitle: null,
                SearchResultKind.Project,
                "\uE8F1",
                Tag: null));
        }

        foreach (var project in projects)
        {
            if (MatchScore(project.Name, normalizedQuery) <= 0
                && MatchScore(project.RootPath, normalizedQuery) <= 0)
            {
                continue;
            }

            items.Add(new GlobalSearchItemSnapshot(
                project.ProjectId,
                project.Name,
                project.RootPath,
                SearchResultKind.Project,
                "\uE8F1",
                Tag: null));
        }

        return new GlobalSearchGroupSnapshot(
            Name: "projects",
            Title: _localizer["Search_Projects"],
            Priority: 90,
            Items: items.ToImmutableArray());
    }

    private GlobalSearchGroupSnapshot SearchSettings(string normalizedQuery)
    {
        var settingsItems = new (string Id, string Title, string Subtitle)[]
        {
            ("General", "通用设置", "主题、语言、启动选项"),
            ("Shortcuts", "快捷键", "自定义键盘快捷键"),
            ("Appearance", "外观", "主题和背景效果"),
            ("DataStorage", "数据与存储", "历史记录和缓存管理"),
            ("AgentAcp", "ACP 配置", "服务器和连接配置"),
            ("Diagnostics", "诊断", "日志和调试信息"),
            ("About", "关于", "版本和许可证信息")
        };

        var items = settingsItems
            .Where(item =>
                MatchScore(item.Title, normalizedQuery) > 0
                || MatchScore(item.Subtitle, normalizedQuery) > 0
                || MatchScore(item.Id, normalizedQuery) > 0)
            .Select(item => new GlobalSearchItemSnapshot(
                item.Id,
                item.Title,
                item.Subtitle,
                SearchResultKind.Setting,
                "\uE713",
                Tag: null))
            .ToImmutableArray();

        return new GlobalSearchGroupSnapshot(
            Name: "settings",
            Title: _localizer["Search_Settings"],
            Priority: 80,
            Items: items);
    }

    private GlobalSearchGroupSnapshot SearchCommands(string normalizedQuery)
    {
        var commands = new (string Id, string Title, string Subtitle, string Tag)[]
        {
            ("new_session", "新建会话", "创建新的聊天会话", "new"),
            ("new_project", "新建项目", "添加新的项目文件夹", "project"),
            ("toggle_theme", "切换主题", "在亮色/暗色/系统主题间切换", "theme"),
            ("toggle_anim", "切换动画", "启用或禁用界面动画", "animation")
        };

        var items = commands
            .Where(command =>
                MatchScore(command.Title, normalizedQuery) > 0
                || MatchScore(command.Subtitle, normalizedQuery) > 0
                || MatchScore(command.Id, normalizedQuery) > 0)
            .Select(command => new GlobalSearchItemSnapshot(
                command.Id,
                command.Title,
                command.Subtitle,
                SearchResultKind.Command,
                "\uE756",
                command.Tag))
            .ToImmutableArray();

        return new GlobalSearchGroupSnapshot(
            Name: "commands",
            Title: _localizer["Search_Commands"],
            Priority: 70,
            Items: items);
    }

    private static int MatchScore(string text, string query)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var lower = text.ToLowerInvariant();
        if (lower == query)
        {
            return 100;
        }

        if (lower.StartsWith(query, StringComparison.Ordinal))
        {
            return 80;
        }

        if (lower.Contains(query, StringComparison.Ordinal))
        {
            return 50;
        }

        var words = lower.Split(new[] { ' ', '_', '-', '.', '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        return words.Any(word => word.StartsWith(query, StringComparison.Ordinal)) ? 60 : 0;
    }
}
