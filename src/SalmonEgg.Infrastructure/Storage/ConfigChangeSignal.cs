using System;
using System.Threading;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Infrastructure.Storage;

public sealed class ConfigChangeSignal : IConfigChangeSignal
{
    private int _suppressCount;

    public event EventHandler<ConfigChangedEventArgs>? Changed;

    public bool IsSuppressed => Volatile.Read(ref _suppressCount) > 0;

    public IDisposable Suppress()
    {
        Interlocked.Increment(ref _suppressCount);
        return new Suppression(this);
    }

    public void NotifyChanged(string path, ConfigChangeKind kind)
    {
        if (IsSuppressed)
        {
            return;
        }

        Changed?.Invoke(this, new ConfigChangedEventArgs(path, kind));
    }

    private sealed class Suppression : IDisposable
    {
        private ConfigChangeSignal? _owner;

        public Suppression(ConfigChangeSignal owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            if (owner is not null)
            {
                Interlocked.Decrement(ref owner._suppressCount);
            }
        }
    }
}
