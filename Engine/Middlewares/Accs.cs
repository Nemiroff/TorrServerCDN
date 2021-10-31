using TSApi.Models;
using Microsoft.AspNetCore.Http;
using System;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using System.Collections.Generic;

namespace TSApi.Engine.Middlewares
{
    public class Accs
    {
        #region Accs
        private readonly RequestDelegate _next;

        IMemoryCache memoryCache;

        public Accs(RequestDelegate next, IMemoryCache memoryCache)
        {
            this.memoryCache = memoryCache;
            _next = next;
        }
        #endregion

        #region IsLockHost
        bool IsLockHost(HttpContext httpContext, string login)
        {
            string memKeyLocIP = $"memKeyLocIP:{login}:{DateTime.Today.Day}";
            string clientIP = httpContext.Connection.RemoteIpAddress.ToString();

            if (memoryCache.TryGetValue(memKeyLocIP, out HashSet<string> _ips))
            {
                _ips.Add(clientIP);
                memoryCache.Set(memKeyLocIP, _ips, DateTime.Today.AddDays(1));

                if (_ips.Count >= 8)
                    return true;
            }
            else
            {
                _ips = new HashSet<string>() { clientIP };
                memoryCache.Set(memKeyLocIP, _ips, DateTime.Today.AddDays(1));
            }

            return false;
        }
        #endregion

        public Task Invoke(HttpContext httpContext)
        {
            #region Служебный запрос
            string clientIp = httpContext.Connection.RemoteIpAddress.ToString();

            if (clientIp == "127.0.0.1" || httpContext.Request.Path.Value.StartsWith("/cron"))
                return _next(httpContext);
            #endregion

            #region Методы работающие без авторизации
            if (httpContext.Request.Path.Value.StartsWith("/echo"))
                return httpContext.Response.WriteAsync("MatriX.CDN");

            if (httpContext.Request.Path.Value.StartsWith("/shutdown"))
                return httpContext.Response.WriteAsync("");
            #endregion

            #region Авторизация по домену
            if (Regex.IsMatch(httpContext.Request.Host.Value, "[^-\\.]+-[^\\.]+\\.[^\\.]+\\.[^\\.]+"))
            {
                var g = Regex.Match(httpContext.Request.Host.Value, "^([^-\\.]+)-([^\\.]+)\\.").Groups;

                string login = g[1].Value;
                string passwd = g[2].Value;

                if (IsLockHost(httpContext, login))
                {
                    httpContext.Response.StatusCode = 403;
                    return Task.CompletedTask;
                }

                if (Startup.usersDb.TryGetValue(login, out string _pass) && _pass == passwd)
                {
                    httpContext.Features.Set(new UserData()
                    {
                        login = login,
                        passwd = passwd
                    });

                    return _next(httpContext);
                }
                else
                {
                    httpContext.Response.StatusCode = 403;
                    return Task.CompletedTask;
                }
            }
            #endregion


            #region Обработка stream потока
            if (httpContext.Request.Method == "GET" && Regex.IsMatch(httpContext.Request.Path.Value, "^/(stream|play)"))
            {
                if (TorAPI.db.FirstOrDefault(i => i.Value.clientIp == clientIp).Value is TorInfo info)
                {
                    httpContext.Features.Set(info.user);
                    return _next(httpContext);
                }
                else
                {
                    httpContext.Response.StatusCode = 403;
                    return Task.CompletedTask;
                }
            }
            #endregion

            #region Access-Control-Request-Headers
            if (httpContext.Request.Method == "OPTIONS" && httpContext.Request.Headers.TryGetValue("Access-Control-Request-Headers", out var AccessControl) && AccessControl == "authorization")
            {
                httpContext.Response.StatusCode = 204;
                return Task.CompletedTask;
            }
            #endregion

            if (httpContext.Request.Headers.TryGetValue("Authorization", out var Authorization))
            {
                byte[] data = Convert.FromBase64String(Authorization.ToString().Replace("Basic ", ""));
                string[] decodedString = Encoding.UTF8.GetString(data).Split(":");

                string login = decodedString[0];
                string passwd = decodedString[1];

                if (Startup.usersDb.TryGetValue(login, out string _pass) && _pass == passwd)
                {
                    httpContext.Features.Set(new UserData()
                    {
                        login = login,
                        passwd = passwd
                    });

                    return _next(httpContext);
                }
            }

            httpContext.Response.StatusCode = 401;
            httpContext.Response.Headers.Add("Www-Authenticate", "Basic realm=Authorization Required");
            return Task.CompletedTask;
        }
    }
}
