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
        public const int MaxActiveConnections = 100;
        public static readonly int MaxActiveWorkers = Environment.ProcessorCount * 4;

        public delegate Task ProcessRequestAsyncCallback(HttpRequest request, HttpResponse response);

        private readonly SemaphoreSlim _connectionSemaphore = new SemaphoreSlim(MaxActiveConnections, MaxActiveConnections);
        private readonly SemaphoreSlim _workSemaphore = new SemaphoreSlim(MaxActiveWorkers, MaxActiveWorkers);

        private readonly ProcessRequestAsyncCallback _processRequest;
        private readonly IPEndPoint _endPoint;

        private readonly object _syncRoot = new object();
        private readonly Dictionary<EndPoint, HttpConnection> _activeHttpConnections =
            new Dictionary<EndPoint, HttpConnection>();

        private Socket _listenSocket;
        private SocketAsyncEventArgs _socketAsyncEventArgs;

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
            _listenSocket.Listen(5);
            _socketAsyncEventArgs = new SocketAsyncEventArgs();
            _socketAsyncEventArgs.Completed += AcceptCompleted;
            StartAccept();

            //Task.Run(() => AcceptConnections());
        }

        private void StartAccept()
        {
            if (!IsListening)
            {
                return;
            }

            _connectionSemaphore.Wait();
            _socketAsyncEventArgs.AcceptSocket = null;

            var willRaiseEvent = _listenSocket.AcceptAsync(_socketAsyncEventArgs);
            if (!willRaiseEvent)
            {
                AcceptCompleted(null, _socketAsyncEventArgs);
            }
        }

        private void AcceptCompleted(object sender, SocketAsyncEventArgs socketAsyncEventArgs)
        {
            if (socketAsyncEventArgs.AcceptSocket != null)
            {
                Task.Factory.StartNew(AcceptConnection, socketAsyncEventArgs.AcceptSocket);
            }

            StartAccept();
        }

        public void Stop()
        {
            _listenSocket?.Dispose();
            _listenSocket = null;
            _socketAsyncEventArgs.Dispose();
            _socketAsyncEventArgs = null;

            // TODO: Wait for workers to finish
        }

        private void AcceptConnection(object argument)
        {
            var socket = (Socket)argument;

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
