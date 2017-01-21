using System;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using TileServer.Http;

namespace TileServer
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Console.Write("Starting server...");
            using (var server = new Listener(new IPEndPoint(IPAddress.Any, 8080), ProcessRequest))
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
    }
}
