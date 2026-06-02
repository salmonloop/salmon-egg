using System;

namespace SalmonEgg.Presentation.Core.Services;

public interface IApplicationActivationSignalSource
{
    event EventHandler? Activated;
}
