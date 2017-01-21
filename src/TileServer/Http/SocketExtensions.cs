using System;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace TileServer.Http
{
    public static class SocketExtensions
    {
        public static async Task SendAllAsync(this Socket socket, ArraySegment<byte> segment, SocketFlags flags)
        {
            var sentBytes = 0;
            while (sentBytes < segment.Count)
            {
                var sendSegment = new ArraySegment<byte>(segment.Array, segment.Offset + sentBytes, segment.Count - sentBytes);
                sentBytes += await socket.SendAsync(sendSegment, flags);
            }
        }
    }
}
