using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace TileServer.Http
{
    public class HttpResponse
    {
        private readonly Socket _socket;
        private readonly HttpVersion _httpVersion;
        private readonly HeadersDictionary _headers;

        private HttpStatusCode _statusCode;

        public HttpResponse(Socket socket, HttpVersion httpVersion)
        {
            _socket = socket;
            _httpVersion = httpVersion;
            _headers = new HeadersDictionary();
        }

        public int TotalBytesSent { get; private set; }

        public bool HeadersSent { get; private set; }

        public HttpStatusCode StatusCode
        {
            get { return _statusCode; }
            set
            {
                if (HeadersSent)
                {
                    throw new InvalidOperationException("Headers already sent.");
                }

                _statusCode = value;
            }
        }

        public IDictionary<string, string> Headers => _headers;

        public bool IsPersistent
        {
            get
            {
                return HttpUtil.IsConnectionPersistent(_httpVersion, _headers);
            }
            set
            {
                Headers["Connection"] = value ? "keep-alive" : "close";
            }
        }

        public async Task WriteAsync(string s, Encoding encoding)
        {
            var stringBytes = encoding.GetBytes(s);

            if (!HeadersSent)
            {
                if (!_headers.ContentLength.HasValue)
                {
                    _headers.ContentLength = stringBytes.Length;
                }

                await SendHeaders();
            }

            var headersContentLength = _headers.ContentLength;

            if (TotalBytesSent + stringBytes.Length > headersContentLength)
            {
                throw new InvalidOperationException(
                    $"Total data length {TotalBytesSent} (sent) + {stringBytes.Length} exceeds specified Content-length of {headersContentLength}!");
            }

            await _socket.SendAllAsync(new ArraySegment<byte>(stringBytes), SocketFlags.None);
            TotalBytesSent += stringBytes.Length;
        }

        private async Task SendHeaders()
        {
            _headers.Freeze();

            var headers = new StringBuilder();
            headers.AppendLine($"{HttpUtil.GetVersionString(_httpVersion)} {HttpUtil.GetStatusString(StatusCode)}");
            headers.AppendLine(Headers.ToString());
            headers.AppendLine();

            var headerBytes = Encoding.ASCII.GetBytes(headers.ToString());
            var bufferSegment = new ArraySegment<byte>(headerBytes);
            await _socket.SendAllAsync(bufferSegment, SocketFlags.None);

            HeadersSent = true;
        }
    }
}