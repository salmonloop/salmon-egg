using System;
using System.Collections.Generic;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Models;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public sealed class ChatServiceFactoryAdapter : IAcpChatServiceFactory
{
    private readonly ChatServiceFactory _inner;

    public ChatServiceFactoryAdapter(ChatServiceFactory inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public IChatService CreateChatService(
        TransportType transportType,
        string? command = null,
        IReadOnlyList<string>? arguments = null,
        string? url = null)
        => _inner.CreateChatService(transportType, command, arguments, url);

    public IChatService CreateChatService(ServerConfiguration configuration)
        => _inner.CreateChatService(configuration);
}
