using TSApi.Models;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using System;
using System.Threading;
using System.Diagnostics;
using System.Net.Sockets;
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
        public static bool CheckPort(int port)
        {
            try
            {
                using (Socket s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
                {
                    int millisecondsTimeout = 900; // 900ms
                    IAsyncResult result = s.BeginConnect("127.0.0.1", port, null, null);
                    result.AsyncWaitHandle.WaitOne(millisecondsTimeout, true);
                    return s.Connected;
                }
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
                string inDir = "/opt/TSApi";
                string outFile = $"{inDir}/sandbox/{userData.login}/master";

                if (!File.Exists(outFile)) //Если нет бинарника торсерва в папке юзера, то
                {
                    Directory.CreateDirectory($"sandbox/{userData.login}"); //Создаем папку юзера
                    Bash.Run($"ln -s {inDir}/dl/master/TorrServer {outFile}"); // Создаем бинарник для юзера
                }
                #endregion

                #region Копируем настройки если их нет
                if (!File.Exists($"sandbox/{userData.login}/config.db")) //Если нет базы в папке пользователя
                        File.Copy($"dl/master/config.db", $"sandbox/{userData.login}/config.db"); // Копируем начальную базу
                #endregion

                #region Запускаем TorrServer
                restart: info.thread = new Thread(() =>
                {
                    string comand = $"{outFile} -p {info.port} -d {inDir}/sandbox/{userData.login} >/dev/null 2>&1"; // -r 

                    var processInfo = new ProcessStartInfo();
                    processInfo.FileName = "/bin/bash";
                    processInfo.Arguments = $" -c \"{comand}\"";

                    info.process = Process.Start(processInfo);
                    info.process.WaitForExit();
                });

                info.thread.Start();
                #endregion

                #region Проверяем доступность сервера
                bool servIsWork = false;

                for (int i = 0; i < 25; i++) // 5 секунд
                {
                    await Task.Delay(200);

                    if (CheckPort(info.port))
                    {
                        servIsWork = true;
                        break;
                    }
                }

                if (servIsWork == false)
                {
                    info.Dispose();
                    info.port = random.Next(60000, 62000);
                    goto restart;
                }
                #endregion
            }

            // Обновляем IP клиента и время последнего запроса
            info.clientIp = httpContext.Connection.RemoteIpAddress.ToString();
            info.lastActive = DateTime.Now;

            #region settings
            if (httpContext.Request.Path.Value.StartsWith("/settings"))
            {
                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(5);

                    var res = await client.PostAsync($"http://127.0.0.1:{info.port}/settings", new StringContent("{\"action\":\"get\"}", Encoding.UTF8, "application/json"));
                    await httpContext.Response.WriteAsync(await res.Content.ReadAsStringAsync());
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
                    //response.Headers[header.Key] = header.Value.ToArray();
                }
            }
            #endregion

            UpdateHeaders(responseMessage.Headers);
            UpdateHeaders(responseMessage.Content.Headers);

            using (var responseStream = await responseMessage.Content.ReadAsStreamAsync())
            {
                await CopyToAsyncInternal(response.Body, responseStream, context.RequestAborted, info);
                //await responseStream.CopyToAsync(response.Body, context.RequestAborted);
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
