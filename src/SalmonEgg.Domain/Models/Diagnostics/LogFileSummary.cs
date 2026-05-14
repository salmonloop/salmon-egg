using System;

namespace SalmonEgg.Domain.Models.Diagnostics;

public sealed record LogFileSummary(string Path, DateTimeOffset LastWriteTimeUtc);
