using Microsoft.AspNetCore.Builder;

namespace TSApi.Engine.Middlewares
{
    public static class Extensions
    {
        public static IApplicationBuilder UseModHeaders(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<ModHeaders>();
        }

        public static IApplicationBuilder UseAccs(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<Accs>();
        }

        public static IApplicationBuilder UseTorAPI(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<TorAPI>();
        }
    }
}
