using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace TileServer.Http
{
    public sealed class Listener : IDisposable
    {
        public const int MaxActiveConnections = 10;

        public delegate Task ProcessRequestAsyncCallback(HttpRequest request, HttpResponse response);

        private readonly SemaphoreSlim _connectionSemaphore = new SemaphoreSlim(MaxActiveConnections, MaxActiveConnections);
        private readonly SemaphoreSlim _workSemaphore = new SemaphoreSlim(Environment.ProcessorCount, Environment.ProcessorCount);

        private readonly ProcessRequestAsyncCallback _processRequest;
        private readonly IPEndPoint _endPoint;

        private readonly object _syncRoot = new object();
        private readonly Dictionary<EndPoint, HttpConnection> _activeHttpConnections =
            new Dictionary<EndPoint, HttpConnection>();

        private Socket _listenSocket;
        
        public Listener(IPEndPoint listeningEndPoint, ProcessRequestAsyncCallback processRequest)
        {
            _endPoint = listeningEndPoint;
            _processRequest = processRequest;
        }

        public bool IsListening => _listenSocket?.IsBound ?? false;

        void IDisposable.Dispose()
        {
            Stop();
            _connectionSemaphore.Dispose();
            _workSemaphore.Dispose();
        }

        public void Start()
        {
            if (_listenSocket != null)
            {
                throw new InvalidOperationException("Already listening.");
            }

            _listenSocket = new Socket(_endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            _listenSocket.Bind(_endPoint);
            _listenSocket.Listen(30);

            Task.Run(() => AcceptConnections());
        }

        public void Stop()
        {
            _listenSocket?.Dispose();
            _listenSocket = null;

            // TODO: Wait for workers to finish
        }

        private void AcceptConnections()
        {
            while (IsListening)
            {
                _connectionSemaphore.Wait();

                Task<Socket> acceptTask;

                try
                {
                    acceptTask = _listenSocket.AcceptAsync();
                }
                catch (ObjectDisposedException)
                {
                    _connectionSemaphore.Release(1);
                    return;
                }

                acceptTask.ContinueWith(AcceptConnection);
            }
        }

        private void AcceptConnection(Task<Socket> acceptTask)
        {
            if (acceptTask.IsFaulted)
            {
                Debug.WriteLine(acceptTask.Exception.GetBaseException());
                _connectionSemaphore.Release(1);
                return;
            }

            var socket = acceptTask.Result;
            Debug.WriteLine($"Accepted connection from {socket.RemoteEndPoint}");

            lock (_syncRoot)
            {
                _activeHttpConnections.Add(socket.RemoteEndPoint,
                    new HttpConnection(socket, RemoveFromActiveConnections, _connectionSemaphore, _workSemaphore,
                        _processRequest));
            }
        }

        private void RemoveFromActiveConnections(EndPoint endPoint)
        {
            lock (_syncRoot)
            {
                _activeHttpConnections.Remove(endPoint);
            }
        }
    }
}
