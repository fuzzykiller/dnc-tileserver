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

        private readonly CancellationTokenSource _cancellationTokenSource =
            new CancellationTokenSource(TimeSpan.FromSeconds(DefaultKeepAliveTimeout));

        private readonly Action<EndPoint> _removeConnectionFunc;
        private readonly SemaphoreSlim _connectionSemaphore;
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
                _cancellationTokenSource.Dispose();
                _socket.Dispose();
                _closed = true;
            }
        }

        private void OnTimeout()
        {
            Close();
        }

        private async Task ProcessRequests()
        {
            while (!_cancellationTokenSource.IsCancellationRequested && await ProcessRequest())
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
            }
            catch (ObjectDisposedException)
            {
                // Socket was closed, probably by timeout
                return false;
            }

            if (bytesReceived == 0)
            {
                // Socket was probably closed
                return false;
            }

            await _workSemaphore.WaitAsync();

            try
            {
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
                    await _processRequest(httpRequest, httpResponse);
                }
                catch (Exception e)
                {
                    if (!httpResponse.HeadersSent)
                    {
                        await SendFatalErrorAndClose(HttpStatusCode.InternalServerError, e.ToString());
                    }

                    return false;
                }

                return httpRequest.IsPersistent && httpResponse.IsPersistent;
            }
            finally
            {
                _workSemaphore.Release(1);
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

            await _socket.SendAllAsync(headerSegment, SocketFlags.None);
            await _socket.SendAllAsync(bodySegment, SocketFlags.None);
            Close();
        }
    }
}