using System;
using System.Collections.Generic;
using System.Windows.Input;

namespace SalmonEgg.Presentation.Models.Search;

public enum SearchResultKind
{
    Session,
    Project,
    Command,
    Setting,
    File,
    Placeholder
}

public sealed class SearchResultItem
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public string? Subtitle { get; init; }

    public SearchResultKind Kind { get; init; }

    public string? IconGlyph { get; init; }

    public string? Tag { get; init; }

    public ICommand? ActivateCommand { get; init; }
}

public sealed class SearchHistoryItem
{
    public required string Query { get; init; }

    public ICommand? UseCommand { get; init; }
}

public sealed class SearchResultGroup
{
    public required string Title { get; init; }

    public required string Name { get; init; }

    public int Priority { get; init; }

    public List<SearchResultItem> Items { get; } = new();
}
