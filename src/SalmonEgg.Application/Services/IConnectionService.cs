using System;
using System.Threading.Tasks;
using SalmonEgg.Application.Common;
using SalmonEgg.Domain.Models;

namespace SalmonEgg.Application.Services
{
    public interface IConnectionService
    {
        Task<Result> ConnectAsync(string configId);
        Task DisconnectAsync();
        ConnectionState GetCurrentState();
        IObservable<ConnectionState> ConnectionStateChanges { get; }
    }
}
