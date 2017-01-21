using System;

namespace TileServer.Http
{
    public class HttpErrorException : Exception
    {
        public string Body { get; }
        public HttpError HttpError { get; }

        public HttpErrorException(HttpError httpError)
        {
            HttpError = httpError;
        }

        public HttpErrorException(HttpError httpError, string body)
            : this(httpError)
        {
            Body = body;
        }
    }
}