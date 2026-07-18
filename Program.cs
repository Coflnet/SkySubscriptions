using Coflnet.Core;
using Coflnet.Security.OpenBao;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.Subscriptions
{
    public class Program
    {
        public static void Main(string[] args)
        {

            //SubscribeEngine.Instance.LoadFromDb();
            //RunIsolatedForever(SubscribeEngine.Instance.ProcessQueues, "SubscribeEngine");
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((_, config) => config.AddOpenBaoFromEnvironment())
                .ConfigureLogging((context, logging) =>
                {
                    // Shared OTel logging configuration from Coflnet.Core.
                    // Bridges ILogger -> OTLP (HttpProtobuf) so logs land in Loki, correlated with traces in Jaeger.
                    logging.AddOpenTelemetryLogging(
                        context.Configuration,
                        context.Configuration["JAEGER_SERVICE_NAME"] ?? "sky-subscriptions");
                })
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                });
    }
}
