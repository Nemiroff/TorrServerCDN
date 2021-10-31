using TSApi.Models;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using System;
using System.Threading;
using System.Diagnostics;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Net.Http.Headers;
using System.Text;

namespace TSApi.Engine.Middlewares
{
    public class TorAPI
    {
        #region TorAPI
        private readonly RequestDelegate _next;

        static Random random = new Random();

        public static Dictionary<string, TorInfo> db = new Dictionary<string, TorInfo>();

        public TorAPI(RequestDelegate next)
        {
            _next = next;
        }
        #endregion

        #region CheckPort
        async public static ValueTask<bool> CheckPort(int port, HttpContext httpContext)
        {
            try
            {
                bool servIsWork = false;
                DateTime endTimeCheckort = DateTime.Now.AddSeconds(8);

                while (true)
                {
                    try
                    {
                        if (DateTime.Now > endTimeCheckort)
                            break;

                        await Task.Delay(200);

                        using (var client = new HttpClient())
                        {
                            client.Timeout = TimeSpan.FromSeconds(2);

                            var response = await client.GetAsync($"http://127.0.0.1:{port}", HttpCompletionOption.ResponseHeadersRead, httpContext.RequestAborted);
                            if (response.StatusCode == System.Net.HttpStatusCode.OK)
                            {
                                servIsWork = true;
                                break;
                            }
                        }
                    }
                    catch { }
                }

                return servIsWork;
            }
            catch
            {
                return false;
            }
        }
        #endregion

        async public Task InvokeAsync(HttpContext httpContext)
        {
            #region Служебный запрос
            string clientIp = httpContext.Connection.RemoteIpAddress.ToString();

            if (clientIp == "127.0.0.1" || httpContext.Request.Path.Value.StartsWith("/cron"))
            {
                await _next(httpContext);
                return;
            }
            #endregion

            var userData = httpContext.Features.Get<UserData>();
            
            if (!db.TryGetValue(userData.login, out TorInfo info))
            {
                string inDir = "/opt/TSApi";
                string torPath = userData.torPath ?? "master";

                #region TorInfo
                info = new TorInfo()
                {
                    user = userData,
                    port = random.Next(60000, 62000),
                    lastActive = DateTime.Now
                };

                db.Add(userData.login, info);
                #endregion

                #region Создаем папку пользователя
                if (!File.Exists($"sandbox/{userData.login}/config.db"))
                {
                    Directory.CreateDirectory($"sandbox/{userData.login}");
                    File.Copy($"dl/{torPath}/config.db", $"sandbox/{userData.login}/config.db");
                }
                #endregion

                #region Запускаем TorrServer
                restart: info.thread = new Thread(() =>
                {
                    try
                    {
                        string comand = $"{inDir}/dl/{torPath}/TorrServer -p {info.port} -d {inDir}/sandbox/{info.user.login} >/dev/null 2>&1"; // -r 

                        var processInfo = new ProcessStartInfo();
                        processInfo.FileName = "/bin/bash";
                        processInfo.Arguments = $" -c \"{comand}\"";

                        info.process = Process.Start(processInfo);
                        info.process.WaitForExit();
                    }
                    catch { }

                    info.OnProcessForExit();
                });

                info.thread.Start();
                #endregion

                #region Проверяем доступность сервера
                if (await CheckPort(info.port, httpContext) == false)
                {
                    info.Dispose();
                    info.port = random.Next(60000, 62000);
                    goto restart;
                }
                #endregion

                #region Отслеживанием падение процесса
                info.processForExit += (s, e) =>
                {
                    info.Dispose();
                    db.Remove(info.user.login);
                };
                #endregion
            }

            // Обновляем IP клиента и время последнего запроса
            info.clientIp = httpContext.Connection.RemoteIpAddress.ToString();
            info.lastActive = DateTime.Now;

            #region settings
            if (httpContext.Request.Path.Value.StartsWith("/settings"))
            {
                if (httpContext.Request.Method != "POST")
                {
                    httpContext.Response.StatusCode = 404;
                    await httpContext.Response.WriteAsync("404 page not found");
                    return;
                }

                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(2);

                    #region Данные запроса
                    MemoryStream mem = new MemoryStream();
                    await httpContext.Request.Body.CopyToAsync(mem);
                    string requestJson = Encoding.UTF8.GetString(mem.ToArray());
                    #endregion

                    #region Актуальные настройки
                    var response = await client.PostAsync($"http://127.0.0.1:{info.port}/settings", new StringContent("{\"action\":\"get\"}", Encoding.UTF8, "application/json"));
                    string settingsJson = await response.Content.ReadAsStringAsync();

                    if (requestJson.Trim() == "{\"action\":\"get\"}")
                    {
                        await httpContext.Response.WriteAsync(settingsJson);
                        return;
                    }

                    if (!userData.allowedToChangeSettings)
                    {
                        await httpContext.Response.WriteAsync(string.Empty);
                        return;
                    }
                    #endregion

                    #region Обновляем настройки кеша 
                    string ReaderReadAHead = Regex.Match(requestJson, "\"ReaderReadAHead\":([0-9]+)", RegexOptions.IgnoreCase).Groups[1].Value;
                    string PreloadCache = Regex.Match(requestJson, "\"PreloadCache\":([0-9]+)", RegexOptions.IgnoreCase).Groups[1].Value;

                    settingsJson = Regex.Replace(settingsJson, "\"ReaderReadAHead\":([0-9]+)", $"\"ReaderReadAHead\":{ReaderReadAHead}", RegexOptions.IgnoreCase);
                    settingsJson = Regex.Replace(settingsJson, "\"PreloadCache\":([0-9]+)", $"\"PreloadCache\":{PreloadCache}", RegexOptions.IgnoreCase);

                    await client.PostAsync($"http://127.0.0.1:{info.port}/settings", new StringContent(settingsJson, Encoding.UTF8, "application/json"));
                    #endregion

                    // Успех
                    await httpContext.Response.WriteAsync(string.Empty);
                    return;
                }
            }
            #endregion

            #region Отправляем запрос в torrserver
            string pathRequest = Uri.EscapeUriString(httpContext.Request.Path.Value);
            string servUri = $"http://127.0.0.1:{info.port}{pathRequest + httpContext.Request.QueryString.Value}";

            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromMinutes(2);
                var request = CreateProxyHttpRequest(httpContext, new Uri(servUri));
                var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, httpContext.RequestAborted);
                await CopyProxyHttpResponse(httpContext, response, info);
            }
            #endregion
        }



        #region CreateProxyHttpRequest
        HttpRequestMessage CreateProxyHttpRequest(HttpContext context, Uri uri)
        {
            var request = context.Request;

            var requestMessage = new HttpRequestMessage();
            var requestMethod = request.Method;
            if (HttpMethods.IsPost(requestMethod))
            {
                var streamContent = new StreamContent(request.Body);
                requestMessage.Content = streamContent;
            }

            foreach (var header in request.Headers)
            {
                if (!requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()) && requestMessage.Content != null)
                {
                    requestMessage.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
                }
            }

            requestMessage.Headers.Host = uri.Authority;
            requestMessage.RequestUri = uri;
            requestMessage.Method = new HttpMethod(request.Method);

            return requestMessage;
        }
        #endregion

        #region CopyProxyHttpResponse
        async Task CopyProxyHttpResponse(HttpContext context, HttpResponseMessage responseMessage, TorInfo info)
        {
            var response = context.Response;
            response.StatusCode = (int)responseMessage.StatusCode;

            #region UpdateHeaders
            void UpdateHeaders(HttpHeaders headers)
            {
                foreach (var header in headers)
                {
                    if (header.Key.ToLower() is "transfer-encoding" or "etag" or "connection")
                        continue;

                    string value = string.Empty;
                    foreach (var val in header.Value)
                        value += $"; {val}";

                    response.Headers[header.Key] = Regex.Replace(value, "^; ", "");
                }
            }
            #endregion

            UpdateHeaders(responseMessage.Headers);
            UpdateHeaders(responseMessage.Content.Headers);

            using (var responseStream = await responseMessage.Content.ReadAsStreamAsync())
            {
                await CopyToAsyncInternal(response.Body, responseStream, context.RequestAborted, info);
            }
        }
        #endregion


        #region CopyToAsyncInternal
        async Task CopyToAsyncInternal(Stream destination, Stream responseStream, CancellationToken cancellationToken, TorInfo info)
        {
            if (destination == null)
                throw new ArgumentNullException("destination");

            if (!responseStream.CanRead && !responseStream.CanWrite)
                throw new ObjectDisposedException("ObjectDisposed_StreamClosed");

            if (!destination.CanRead && !destination.CanWrite)
                throw new ObjectDisposedException("ObjectDisposed_StreamClosed");

            if (!responseStream.CanRead)
                throw new NotSupportedException("NotSupported_UnreadableStream");

            if (!destination.CanWrite)
                throw new NotSupportedException("NotSupported_UnwritableStream");

            byte[] buffer = new byte[81920];
            int bytesRead;
            while ((bytesRead = await responseStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) != 0)
            {
                await destination.WriteAsync(buffer, 0, bytesRead, cancellationToken).ConfigureAwait(false);
                info.lastActive = DateTime.Now;
            }
        }
        #endregion
    }
}
