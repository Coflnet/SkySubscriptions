using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Coflnet.Sky.Subscriptions.Models;
using Coflnet.Sky.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;
using Prometheus;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Items.Client.Api;
using Coflnet.Sky.Filter;
using Coflnet.Core.Security;
using Coflnet.Sky.Subscriptions.Infrastructure;
using Microsoft.AspNetCore.Http;

namespace Coflnet.Sky.Subscriptions
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
            HypixelContext.SetConfiguration(configuration);
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers();
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "SkySubscriptions", Version = "v1" });
                c.DocumentFilter<Coflnet.OpenApi.NullOnlySchemaDocumentFilter>();
                // Set the comments path for the Swagger JSON and UI.
                var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
                var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
                c.IncludeXmlComments(xmlPath);
            });

            var serverVersion = new MariaDbServerVersion(new Version(Configuration["MARIADB_VERSION"]));
            var connectionString = DatabaseTls.ConnectionString(Configuration);
            if (Configuration.GetSection("DB:TLS").Exists())
            {
                services.AddSingleton(_ => PemTlsOptions.FromConfiguration(Configuration.GetSection("DB:TLS")));
                services.AddSingleton<DatabaseTlsInterceptor>();
            }
            services.AddDbContext<SubsDbContext>((provider, options) =>
            {
                options.UseMySql(connectionString, serverVersion, b => b.EnableRetryOnFailure(5))
                    .EnableDetailedErrors();
                if (Configuration.GetSection("DB:TLS").Exists())
                    options.AddInterceptors(provider.GetRequiredService<DatabaseTlsInterceptor>());
            });
            services.AddSingleton<INotificationService, NotificationService>();
            services.AddSingleton<SubscribeEngine>();
            services.AddSingleton<NBT>();
            services.AddSingleton<ItemDetails>();
            services.AddSingleton<Kafka.KafkaCreator>();
            services.AddSingleton<FilterEngine>();
            services.AddSingleton<IItemsApi>(di=>
            {
                var config = di.GetRequiredService<IConfiguration>();
                return new ItemsApi(config["ITEMS_BASE_URL"]);
            });
            services.AddHostedService<SubscribeEngine>(provider => provider.GetService<SubscribeEngine>());
            services.AddJaeger(Configuration);
            services.AddCoflService();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "SkySubscriptions v1");
                c.RoutePrefix = "api";
            });

            app.UseHttpsRedirection();

            app.UseRouting();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapMetrics();
                endpoints.MapControllers();
                endpoints.MapGet("/health/ready", async context =>
                {
                    try
                    {
                        await DatabaseReadiness.CheckAsync(context.RequestServices.GetRequiredService<SubsDbContext>(),
                            context.RequestAborted);
                        await context.Response.WriteAsync("ready");
                    }
                    catch (Exception)
                    {
                        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                        await context.Response.WriteAsync("not ready");
                    }
                });
            });
        }
    }
}
