using System;
using System.IO;
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

        private HttpStatusCode _statusCode;

        public HttpResponse(Socket socket, HttpVersion httpVersion)
        {
            _socket = socket;
            _httpVersion = httpVersion;
            _statusCode = HttpStatusCode.OK;
            Headers = new HeadersDictionary();
        }

        public long TotalBytesSent { get; private set; }

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

        public HeadersDictionary Headers { get; }

        public bool IsPersistent
        {
            get
            {
                return HttpUtil.IsConnectionPersistent(_httpVersion, Headers);
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
                if (!Headers.ContentLength.HasValue)
                {
                    Headers.ContentLength = stringBytes.Length;
                }

                await SendHeaders();
            }

            var headersContentLength = Headers.ContentLength;

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
            Headers.Freeze();

            var headers = new StringBuilder();
            headers.AppendLine($"{HttpUtil.GetVersionString(_httpVersion)} {HttpUtil.GetStatusString(StatusCode)}");
            headers.AppendLine(Headers.ToString());
            headers.AppendLine();

            var headerBytes = Encoding.ASCII.GetBytes(headers.ToString());
            var bufferSegment = new ArraySegment<byte>(headerBytes);
            await _socket.SendAllAsync(bufferSegment, SocketFlags.None);

            HeadersSent = true;
        }

        public async Task Send(Stream stream)
        {
            if (!HeadersSent)
            {
                if (!Headers.ContentLength.HasValue)
                {
                    Headers.ContentLength = stream.Length;
                }

                await SendHeaders();
            }

            var buffer = new byte[16384];
            var bytesToCopy = Headers.ContentLength.Value - TotalBytesSent;
            var remaining = bytesToCopy;

            if (remaining == 0)
            {
                throw new InvalidOperationException($"All data as indicated by Content-length = {Headers.ContentLength} has already been sent.");
            }

            while (remaining > 0)
            {
                var bytesToRead = (int) Math.Min(remaining, buffer.Length);
                var bytesRead = await stream.ReadAsync(buffer, 0, bytesToRead);

                if (bytesRead == 0)
                {
                    break;
                }

                var bytesSent = 0;
                do
                {
                    bytesSent += await _socket.SendAsync(new ArraySegment<byte>(buffer, bytesSent, bytesRead - bytesSent), SocketFlags.None);
                } while (bytesSent < bytesRead);

                remaining -= bytesRead;
            }

            TotalBytesSent += bytesToCopy;
        }
    }
}