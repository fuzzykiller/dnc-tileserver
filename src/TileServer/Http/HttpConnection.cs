using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TileServer.Http
{
    internal class HttpConnection
    {
        /* 
         * TODO:
         * - Use internal queue to enable pipelining
         */

        public const long DefaultKeepAliveTimeout = 5;

        private readonly byte[] _buffer = new byte[4096];

        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        private readonly Action<EndPoint> _removeConnectionFunc;
        private readonly SemaphoreSlim _connectionSemaphore;
        private readonly SemaphoreSlim _socketSemaphore = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _workSemaphore;
        private readonly Listener.ProcessRequestAsyncCallback _processRequest;
        private readonly Socket _socket;
        private bool _closed;

        public HttpConnection(Socket socket, Action<EndPoint> removeConnectionFunc, SemaphoreSlim connectionSemaphore, SemaphoreSlim workSemaphore, Listener.ProcessRequestAsyncCallback processRequest)
        {
            _socket = socket;
            _removeConnectionFunc = removeConnectionFunc;
            _connectionSemaphore = connectionSemaphore;
            _workSemaphore = workSemaphore;
            _processRequest = processRequest;

            _cancellationTokenSource.Token.Register(OnTimeout, false);
            Task.Factory.StartNew(ProcessRequests, TaskCreationOptions.LongRunning);
        }

        public void Close(bool fast = false)
        {
            _connectionSemaphore.Release(1);
            _removeConnectionFunc(_socket.RemoteEndPoint);
            Debug.WriteLine($"Closing connection to {_socket.RemoteEndPoint}");

            if (_closed)
            {
                return;
            }

            try
            {
                if (!fast)
                {
                    _socket.Shutdown(SocketShutdown.Both);
                }
            }
            finally
            {
                _socketSemaphore.Dispose();
                _cancellationTokenSource.Dispose();
                _socket.Dispose();
                _closed = true;
            }
        }

        private async void OnTimeout()
        {
            await _socketSemaphore.WaitAsync().ConfigureAwait(false);
            Close();
        }

        private async Task ProcessRequests()
        {
            var keepAlive = await ProcessRequest().ConfigureAwait(false);
            _cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(DefaultKeepAliveTimeout));
            if (!keepAlive)
            {
                return;
            }

            while (!_cancellationTokenSource.IsCancellationRequested && await ProcessRequest().ConfigureAwait(false))
            {
            }
        }

        private async Task<bool> ProcessRequest()
        {
            var bufferSegment = new ArraySegment<byte>(_buffer);

            // TODO: Retry reading until end of headers reached
            int bytesReceived;
            try
            {
                bytesReceived = await _socket.ReceiveAsync(bufferSegment, SocketFlags.None).ConfigureAwait(false);

                // TODO: Not good enough; a request may have been received but the connection will be closed anyway
                await _socketSemaphore.WaitAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                _socketSemaphore.Release(1);
                // Socket was closed, probably by timeout
                return false;
            }

            if (bytesReceived == 0)
            {
                _socketSemaphore.Release(1);
                // Socket was probably closed
                return false;
            }

            await _workSemaphore.WaitAsync().ConfigureAwait(false);

            try
            {
                // We may be too late and the connection may have timed out from keep-alive
                if (!_socket.Connected)
                {
                    return false;
                }

                HttpRequest httpRequest = null;
                try
                {
                    httpRequest =
                        await RequestParser.ParseRequest(new ArraySegment<byte>(_buffer, 0, bytesReceived), _socket,
                            SendFatalErrorAndClose).ConfigureAwait(false);
                }
                catch
                {
                    if (httpRequest == null)
                    {
                        return false;
                    }
                }

                Debug.WriteLine($"Got request from {_socket.RemoteEndPoint}: {httpRequest.Verb} {httpRequest.Path} {httpRequest.HttpVersion}");
                var httpResponse = new HttpResponse(_socket, httpRequest.HttpVersion);

                try
                {
                    await _processRequest(httpRequest, httpResponse).ConfigureAwait(false);

                    if (httpResponse.TotalBytesSent < httpResponse.Headers.ContentLength)
                    {
                        // Force close connection if less than promised has been sent.
                        return false;
                    }
                }
                catch (Exception e)
                {
                    if (!httpResponse.HeadersSent)
                    {
                        await SendFatalErrorAndClose(HttpStatusCode.InternalServerError, e.ToString()).ConfigureAwait(false);
                    }

                    return false;
                }

                return httpRequest.IsPersistent && httpResponse.IsPersistent;
            }
            finally
            {
                _workSemaphore.Release(1);
                _socketSemaphore.Release(1);
            }
        }

        private async Task SendFatalErrorAndClose(HttpStatusCode statusCode, string body = null)
        {
            if (_closed)
            {
                return;
            }

            var bodyBytes = body != null ? Encoding.UTF8.GetBytes(body) : new byte[0];
            var bodySegment = new ArraySegment<byte>(bodyBytes);
            
            var statusString = HttpUtil.GetStatusString(statusCode);

            // TODO: Is StringBuilder safe? CRLF required!
            var response = new StringBuilder();
            response.AppendLine($"HTTP/1.1 {statusString}");
            response.AppendLine("Server: TileServer/1.0");
            response.AppendLine("Content-type: text/plain; charset=utf-8");
            response.AppendLine($"Content-length: {bodyBytes.Length}");
            response.AppendLine();

            var headerBytes = Encoding.ASCII.GetBytes(response.ToString());
            var headerSegment = new ArraySegment<byte>(headerBytes);

            await _socket.SendAllAsync(headerSegment, SocketFlags.None).ConfigureAwait(false);
            await _socket.SendAllAsync(bodySegment, SocketFlags.None).ConfigureAwait(false);
            Close();
        }
    }
}