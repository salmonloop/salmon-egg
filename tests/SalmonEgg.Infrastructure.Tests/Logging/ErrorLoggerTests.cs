using System;
using System.Linq;
using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Logging;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Logging;

public sealed class ErrorLoggerTests
{
    [Fact]
    public void LogError_Entry_IsRetrievableViaGetRecentErrors()
    {
        var logger = new ErrorLogger();
        logger.LogError(new ErrorLogEntry("E1", "boom", ErrorSeverity.Error));

        var recent = logger.GetRecentErrors();
        var entry = Assert.Single(recent);
        Assert.Equal("E1", entry.ErrorCode);
        Assert.Equal("boom", entry.ErrorMessage);
        Assert.Equal(ErrorSeverity.Error, entry.Severity);
    }

    [Fact]
    public void LogError_Convenience_BuildsEntryWithCodeSeverityAndContext()
    {
        var logger = new ErrorLogger();
        logger.LogError("CODE", "message", ErrorSeverity.Warning, "Method", "session-1");

        var entry = Assert.Single(logger.GetRecentErrors());
        Assert.Equal("CODE", entry.ErrorCode);
        Assert.Equal(ErrorSeverity.Warning, entry.Severity);
        Assert.Equal("Method", entry.MethodName);
        Assert.Equal("session-1", entry.SessionId);
    }

    [Fact]
    public void GetRecentErrors_FiltersByMinimumSeverity()
    {
        var logger = new ErrorLogger();
        LogAt(logger, ErrorSeverity.Info, 1);
        LogAt(logger, ErrorSeverity.Warning, 2);
        LogAt(logger, ErrorSeverity.Error, 3);
        LogAt(logger, ErrorSeverity.Critical, 4);

        var recent = logger.GetRecentErrors(minSeverity: ErrorSeverity.Error);

        Assert.Equal(2, recent.Count());
        Assert.All(recent, e => Assert.True(e.Severity >= ErrorSeverity.Error));
    }

    [Fact]
    public void GetRecentErrors_OrdersByTimestampDescending()
    {
        var logger = new ErrorLogger();
        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 3; i++)
        {
            logger.LogError(new ErrorLogEntry($"E{i}", $"m{i}", ErrorSeverity.Error)
            {
                Timestamp = baseTime.AddSeconds(i)
            });
        }

        var recent = logger.GetRecentErrors(count: 3);

        Assert.Equal(new[] { "E2", "E1", "E0" }, recent.Select(e => e.ErrorCode).ToArray());
    }

    [Fact]
    public void ClearErrors_RemovesAllEntries()
    {
        var logger = new ErrorLogger();
        logger.LogError("E1", "m", ErrorSeverity.Error);
        logger.LogError("E2", "m", ErrorSeverity.Critical);

        logger.ClearErrors();

        Assert.Empty(logger.GetRecentErrors());
        Assert.Equal(0, logger.GetErrorCount());
    }

    [Fact]
    public void ClearErrorsUpToSeverity_RemovesUpToAndKeepsAbove()
    {
        var logger = new ErrorLogger();
        LogAt(logger, ErrorSeverity.Info, 1);
        LogAt(logger, ErrorSeverity.Warning, 2);
        LogAt(logger, ErrorSeverity.Error, 3);
        LogAt(logger, ErrorSeverity.Critical, 4);

        logger.ClearErrorsUpToSeverity(ErrorSeverity.Warning);

        var remaining = logger.GetRecentErrors().ToList();
        Assert.Equal(2, remaining.Count);
        Assert.All(remaining, e => Assert.True(e.Severity > ErrorSeverity.Warning));
    }

    [Fact]
    public void LogError_EvictsOldestBeyondCapacityKeepingNewest()
    {
        var logger = new ErrorLogger();
        const int total = 1050;
        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < total; i++)
        {
            logger.LogError(new ErrorLogEntry($"E{i}", "m", ErrorSeverity.Error)
            {
                Timestamp = baseTime.AddSeconds(i)
            });
        }

        Assert.Equal(1000, logger.GetErrorCount());
        var all = logger.GetRecentErrors(count: total).ToList();
        Assert.DoesNotContain(all, e => e.ErrorCode == "E0");
        Assert.Contains(all, e => e.ErrorCode == $"E{total - 1}");
    }

    [Fact]
    public void GetErrorStatistics_CountsBySeverity()
    {
        var logger = new ErrorLogger();
        LogAt(logger, ErrorSeverity.Info, 1);
        LogAt(logger, ErrorSeverity.Info, 2);
        LogAt(logger, ErrorSeverity.Warning, 3);
        LogAt(logger, ErrorSeverity.Critical, 4);

        var stats = logger.GetErrorStatistics();

        Assert.Equal(2, stats[ErrorSeverity.Info]);
        Assert.Equal(1, stats[ErrorSeverity.Warning]);
        Assert.Equal(0, stats[ErrorSeverity.Error]);
        Assert.Equal(1, stats[ErrorSeverity.Critical]);
    }

    [Fact]
    public void HasCriticalErrors_AndHasErrorsOrHigher_ReflectLoggedSeverity()
    {
        var logger = new ErrorLogger();
        Assert.False(logger.HasCriticalErrors());
        Assert.False(logger.HasErrorsOrHigher());

        logger.LogError("E", "m", ErrorSeverity.Error);
        Assert.True(logger.HasErrorsOrHigher());
        Assert.False(logger.HasCriticalErrors());

        logger.LogError("C", "m", ErrorSeverity.Critical);
        Assert.True(logger.HasCriticalErrors());
    }

    [Fact]
    public void LogError_NullEntry_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ErrorLogger().LogError(null!));
    }

    private static void LogAt(ErrorLogger logger, ErrorSeverity severity, int index)
    {
        logger.LogError($"E{index}", "m", severity);
    }
}
