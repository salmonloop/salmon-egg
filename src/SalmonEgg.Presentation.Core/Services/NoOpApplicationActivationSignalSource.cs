using System;

namespace SalmonEgg.Presentation.Core.Services;

internal sealed class NoOpApplicationActivationSignalSource : IApplicationActivationSignalSource
{
    public static NoOpApplicationActivationSignalSource Instance { get; } = new();

    private NoOpApplicationActivationSignalSource()
    {
    }

    public event EventHandler? Activated
    {
        add { }
        remove { }
    }
}
