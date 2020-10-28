using IDNT.AppBase.Network.Protocol.Syslog;
using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace ExampleServer
{
    class Program
    {
        static private async Task OnMessageReceived(SyslogMessage m, CancellationToken ct)
        {
            Console.WriteLine(m.ToString());
            return;
        }

        static void Main(string[] args)
        {
            var syslogServer = new SyslogServer(listenAddress: IPAddress.Any);

            syslogServer.Start(OnMessageReceived);

            Console.WriteLine($"Server ready. Send syslog messages to {syslogServer.ListenAddress.ToString()} UDP port {syslogServer.UdpListenPort} or TCP port {syslogServer.TcpListenPort}.");
            Console.WriteLine("Press [ENTER] to stop");
            
            Console.ReadLine();
            syslogServer.Stop();

            Console.WriteLine("Server stopped.");
        }
    }
}
