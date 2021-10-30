using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Text.Json.Serialization;
using TSApi.Engine.Middlewares;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace TSApi
{
    public class Startup
    {
        public static Dictionary<string, string> usersDb = new Dictionary<string, string>();
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllersWithViews().AddJsonOptions(options => {
                //options.JsonSerializerOptions.IgnoreNullValues = true;
                options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault;
                options.JsonSerializerOptions.PropertyNamingPolicy = null;
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
                app.UseDeveloperExceptionPage();

            #region load usersDb.json
            if (System.IO.File.Exists("usersDb.json"))
            {
                try
                {
                    usersDb = JsonConvert.DeserializeObject<Dictionary<string, string>>(System.IO.File.ReadAllText("usersDb.json"));
                }
                catch { }
            }
            #endregion

            app.UseForwardedHeaders(new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
            });

            app.UseRouting();
            app.UseModHeaders();
            app.UseAccs();
            app.UseTorAPI();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}
