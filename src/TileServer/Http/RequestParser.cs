using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace TileServer.Http
{
    internal static class RequestParser
    {
        public delegate Task SendFatalErrorDelegate(HttpStatusCode statusCode, string body = null);
        
        public static async Task<HttpRequest> ParseRequest(ArraySegment<byte> buffer, Socket socket, SendFatalErrorDelegate sendFatalError)
        {
            var receivedText = Encoding.ASCII.GetString(buffer.Array, buffer.Offset, buffer.Count);
            var textReader = new StringReader(receivedText);
            var firstLine = textReader.ReadLine();

            if (string.IsNullOrWhiteSpace(firstLine))
            {
                await sendFatalError(HttpStatusCode.BadRequest, "Request path too long.").ConfigureAwait(false);
                return null;
            }

            var components = firstLine.Split(' ');
            if (components.Length != 3)
            {
                await sendFatalError(HttpStatusCode.BadRequest, "Bad HTTP request.").ConfigureAwait(false);
                return null;
            }

            var bytesRead = firstLine.Length + 2;

            var verb = ParseHttpVerb(components[0]);
            var path = DecodePath(components[1]);
            var httpVersion = ParseHttpVersion(components[2]);

            if (httpVersion == HttpVersion.Unknown)
            {
                await sendFatalError(HttpStatusCode.BadRequest, $"Unknwon HTTP version {components[2]}").ConfigureAwait(false);
                return null;
            }

            if (verb == HttpVerb.Unknown)
            {
                await sendFatalError(HttpStatusCode.BadRequest, $"Unknown HTTP verb {components[0]}").ConfigureAwait(false);
                return null;
            }

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string line;
            while (!string.IsNullOrEmpty(line = textReader.ReadLine()))
            {
                bytesRead += line.Length + 2;
                var colonIndex = line.IndexOf(':');

                if (colonIndex == -1 || colonIndex == 0)
                {
                    await sendFatalError(HttpStatusCode.BadRequest, $"Invalid header: '{line}'").ConfigureAwait(false);
                    return null;
                }

                var key = line.Substring(0, colonIndex).Trim();
                var value = line.Substring(colonIndex + 1).Trim();

                headers[key] = value;
            }

            if (line == null)
            {
                // Reached end without reading empty line
                await sendFatalError(HttpStatusCode.RequestEntityTooLarge, "Request too large.").ConfigureAwait(false);
                return null;
            }

            bytesRead += 2;

            // TODO: Support chunked transfer
            // TODO: Stream request contents
            string temp;
            var contentLength = headers.TryGetValue("Content-length", out temp)
                ? int.Parse(temp, CultureInfo.InvariantCulture)
                : default(int?);

            if (!contentLength.HasValue)
            {
                return new HttpRequest(httpVersion, verb, path, headers);
            }

            if (contentLength.Value < 1024 * 1024)
            {
                var contentBuffer = new byte[contentLength.Value];
                var contentBytesReceived = 0;
                if (bytesRead < buffer.Count)
                {
                    Array.Copy(buffer.Array, buffer.Offset + bytesRead, contentBuffer, 0, buffer.Count - bytesRead);
                    contentBytesReceived = buffer.Count - bytesRead;
                }

                while (contentBytesReceived < contentLength.Value)
                {
                    var bufferSegment = new ArraySegment<byte>(contentBuffer, contentBytesReceived, contentBuffer.Length - contentBytesReceived);
                    contentBytesReceived += await socket.ReceiveAsync(bufferSegment, SocketFlags.None);
                }

                return new HttpRequest(httpVersion, verb, path, headers, contentBuffer);
            }

            await sendFatalError(HttpStatusCode.RequestEntityTooLarge, "Request body too large.")
                .ConfigureAwait(false);

            return null;
        }

        private static HttpVersion ParseHttpVersion(string s)
        {
            switch (s)
            {
                case "HTTP/1.0":
                    return HttpVersion.Http10;
                case "HTTP/1.1":
                    return HttpVersion.Http11;
                default:
                    return HttpVersion.Unknown;
            }
        }

        private static HttpVerb ParseHttpVerb(string s)
        {
            HttpVerb result;
            return Enum.TryParse(s, out result) ? result : HttpVerb.Unknown;
        }

        private static string DecodePath(string s)
        {
            return WebUtility.UrlDecode(s);
        }
    }
}