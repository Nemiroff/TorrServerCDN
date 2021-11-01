using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using TSApi.Engine;

namespace TSApi.Models
{
    public class TorInfo
    {
        public int port { get; set; }

        public HashSet<string> clientIps { get; set; } = new HashSet<string>();

        public UserData user { get; set; }

        public Thread thread { get; set; }

        public DateTime lastActive { get; set; }

        public int countError { get; set; }


        #region process
        public Process process { get; set; }

        public event EventHandler processForExit;

        public void OnProcessForExit()
        {
            processForExit?.Invoke(this, null);
        }
        #endregion

        #region Dispose
        bool IsDispose;

        public void Dispose()
        {
            if (IsDispose)
                return;

            IsDispose = true;

            #region process
            try
            {
                process.Kill(true);
                process.Dispose();
            }
            catch { }
            #endregion

            #region Bash
            try
            {
                string comand = $"ps axu | grep \"/sandbox/{user.login}/\" " + "| grep -v grep | awk '{print $2}'";

                if (user.IsShared)
                    comand = $"ps axu | grep \"/TorrServer -p {port} -r\" " + "| grep -v grep | awk '{print $2}'";

                foreach (string line in Bash.Run(comand).Split("\n"))
                {
                    if (int.TryParse(line, out int pid))
                        Bash.Run($"kill -9 {pid}");
                }
            }
            catch { }
            #endregion

            clientIps.Clear();
            thread = null;
        }
        #endregion
    }
}
