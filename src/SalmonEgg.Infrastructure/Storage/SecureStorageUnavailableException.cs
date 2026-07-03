using System;

namespace SalmonEgg.Infrastructure.Storage;

public sealed class SecureStorageUnavailableException : InvalidOperationException
{
    public SecureStorageUnavailableException(string message)
        : base(message)
    {
    }
}
