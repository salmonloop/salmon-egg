using System;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models.Diagnostics;

namespace SalmonEgg.Domain.Services;

public interface ILiveLogStreamService
{
    Task StartAsync(
        string logsDirectoryPath,
        Func<LiveLogStreamUpdate, Task> onUpdate,
        CancellationToken cancellationToken);
}
