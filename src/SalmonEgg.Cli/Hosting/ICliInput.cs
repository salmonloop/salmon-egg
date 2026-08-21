using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SalmonEgg.Cli.Hosting;

public interface ICliInput
{
    Task<string?> ReadSecretLineAsync(CancellationToken cancellationToken = default);
}

public sealed class TextCliInput : ICliInput
{
    private readonly TextReader _reader;

    public TextCliInput(TextReader reader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    public Task<string?> ReadSecretLineAsync(CancellationToken cancellationToken = default)
        => _reader.ReadLineAsync(cancellationToken).AsTask();
}
