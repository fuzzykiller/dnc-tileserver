using System;
using System.Collections.Generic;

namespace TileServer.Http
{
    public class HttpRequest
    {
        public HttpRequest(HttpVersion httpVersion, HttpVerb verb, string path, IReadOnlyDictionary<string, string> headers)
        {
            HttpVersion = httpVersion;
            Verb = verb;
            Path = path;
            Headers = headers;
            
            IsPersistent = HttpUtil.IsConnectionPersistent(httpVersion, headers);
        }

        public HttpRequest(HttpVersion httpVersion, HttpVerb verb, string path, IReadOnlyDictionary<string, string> headers, byte[] requestContent)
            : this(httpVersion, verb, path, headers)
        {
            RequestContent = requestContent;
        }


        public HttpVersion HttpVersion { get; }

        public HttpVerb Verb { get; }

        public string Path { get; }

        public IReadOnlyDictionary<string, string> Headers { get; }

        public byte[] RequestContent { get; }

        public bool IsPersistent { get; }
    }
}