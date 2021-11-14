using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using NetworkPorts;
using System.Net;

namespace TSApi
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseKestrel(op => op.Listen(IPAddress.Any, NetworkPort.Http));
                    webBuilder.UseStartup<Startup>();
                });
    }
}
