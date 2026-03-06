using System;
using System.Threading.Tasks;
using UnoAcpClient.Application.Common;
using UnoAcpClient.Domain.Models;

namespace UnoAcpClient.Application.Services
{
    public interface IConnectionService
    {
        Task<Result> ConnectAsync(string configId);
        Task DisconnectAsync();
        ConnectionState GetCurrentState();
        IObservable<ConnectionState> ConnectionStateChanges { get; }
    }
}
