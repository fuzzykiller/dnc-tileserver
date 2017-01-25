using System;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using TileServer.Http;

namespace TileServer
{
    public class Program
    {
        private static readonly Assembly CurrentAssembly = typeof(Program).GetTypeInfo().Assembly;

        public static void Main(string[] args)
        {
            Console.Write("Starting server...");
            using (var server = new Listener(new IPEndPoint(IPAddress.Any, 8080), ProcessRequest2))
            {
                server.Start();
                Console.WriteLine("Started!");
                Console.WriteLine("Press ENTER to exit...");
                Console.ReadLine();
                Console.WriteLine("Closing...");
            }

            Console.WriteLine("Closed!");
        }

        private static async Task ProcessRequest(HttpRequest request, HttpResponse response)
        {
            response.Headers["Content-type"] = "text/plain; charset=utf-8";
            await response.WriteAsync($"Hallo von {request.Path}!", Encoding.UTF8);
        }

        private static async Task ProcessRequest2(HttpRequest request, HttpResponse response)
        {
            if (request.Path.StartsWith("/tile"))
            {
                await TryGetTile(request, response);
                return;
            }

            await TryGetStaticFile(request, response);
        }

        private static async Task TryGetStaticFile(HttpRequest request, HttpResponse response)
        {
            var path = request.Path == "/" ? "/index.html" : request.Path;
            var resourceName = "TileServer.Assets." + path.Substring(1).Replace('/', '.');
            var resourceInfo = CurrentAssembly.GetManifestResourceInfo(resourceName);

            if (resourceInfo == null)
            {
                response.StatusCode = HttpStatusCode.NotFound;
                response.Headers["Content-type"] = "text/plain; charset=utf-8";
                await response.WriteAsync($"File at {request.Path} not found.", Encoding.UTF8);
                return;
            }

            var extension = resourceName.Substring(resourceName.LastIndexOf('.'));
            string mimeType;
            switch (extension)
            {
                case ".png":
                    mimeType = "image/png";
                    break;
                case ".jpg":
                    mimeType = "image/jpeg";
                    break;
                case ".html":
                    mimeType = "text/html";
                    break;
                case ".css":
                    mimeType = "text/css";
                    break;
                case ".js":
                    mimeType = "text/javascript";
                    break;
                default:
                    mimeType = "application/octet-stream";
                    break;
            }

            using (var resStream = CurrentAssembly.GetManifestResourceStream(resourceName))
            {
                response.Headers["Content-type"] = mimeType;
                response.Headers["Content-length"] = resStream.Length.ToString(CultureInfo.InvariantCulture);

                await response.Send(resStream);
            }
        }

        private static async Task TryGetTile(HttpRequest request, HttpResponse response)
        {
            throw new NotImplementedException();
        }
    }
}
