using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using TSApi.Engine.Middlewares;

namespace TSApi.Controllers
{
    [Route("cron/[action]")]
    public class CronController : Controller
    {
        #region UpdateUsersDb
        public string UpdateUsersDb()
        {
            if (HttpContext.Connection.RemoteIpAddress.ToString() != "127.0.0.1")
                return "Hello World!";

            if (System.IO.File.Exists("usersDb.json"))
                Startup.usersDb = JsonConvert.DeserializeObject<Dictionary<string, string>>(System.IO.File.ReadAllText("usersDb.json"));

            return "ok";
        }
        #endregion

        #region CheckingNodes
        static bool workCheckingNodes = false;

        async public Task<string> CheckingNodes()
        {
            if (HttpContext.Connection.RemoteIpAddress.ToString() != "127.0.0.1")
                return "Pwnd!";

            if (workCheckingNodes)
                return "work";

            workCheckingNodes = true;

            try
            {
                foreach (var node in TorAPI.db.ToArray())
                {
                    if (node.Value.countError >= 3 || DateTime.Now.AddMinutes(-15) > node.Value.lastActive)
                    {
                        node.Value.Dispose();
                        TorAPI.db.Remove(node.Key);
                    }
                    else
                    {
                        if (await TorAPI.CheckPort(node.Value.port, HttpContext) == false)
                        {
                            node.Value.countError += 1;
                        }
                        else
                        {
                            node.Value.countError = 0;
                        }
                    }
                }
            }
            catch { }

            workCheckingNodes = false;
            return "ok";
        }
        #endregion
    }
}
