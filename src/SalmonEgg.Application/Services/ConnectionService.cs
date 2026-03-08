using System;
using System.Threading.Tasks;
using SalmonEgg.Application.Common;
using SalmonEgg.Application.UseCases;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Application.Services
{
    public class ConnectionService : IConnectionService
    {
        private readonly ConnectToServerUseCase _connectUseCase;
        private readonly DisconnectUseCase _disconnectUseCase;
        private readonly IConnectionManager _connectionManager;
        private ConnectionState _currentState;

        public IObservable<ConnectionState> ConnectionStateChanges =>
            _connectionManager.ConnectionStateChanges;

        public ConnectionService(
            ConnectToServerUseCase connectUseCase,
            DisconnectUseCase disconnectUseCase,
            IConnectionManager connectionManager)
        {
            _connectUseCase = connectUseCase ?? throw new ArgumentNullException(nameof(connectUseCase));
            _disconnectUseCase = disconnectUseCase ?? throw new ArgumentNullException(nameof(disconnectUseCase));
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));

            _currentState = new ConnectionState { Status = ConnectionStatus.Disconnected };
            _connectionManager.ConnectionStateChanges.Subscribe(state => { _currentState = state; });
        }

        public async Task<Result> ConnectAsync(string configId) =>
            await _connectUseCase.ExecuteAsync(configId);

        public async Task DisconnectAsync() =>
            await _disconnectUseCase.ExecuteAsync();

        public ConnectionState GetCurrentState() => _currentState;
    }
}
