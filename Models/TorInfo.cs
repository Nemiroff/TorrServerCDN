using System;
using System.Diagnostics;
using System.Threading;
using TSApi.Engine;

namespace TSApi.Models
{
    public class TorInfo
    {
        public int port { get; set; }

        public string clientIp { get; set; }

        public UserData user { get; set; }

        public Thread thread { get; set; }

        public Process process { get; set; }

        public DateTime lastActive { get; set; }

        public int countError{ get; set; }


        public void Dispose()
        {
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
                foreach (string line in Bash.Run($"ps axu | grep \"/sandbox/{user.login}/\" " + "| grep -v grep | awk '{print $2}'").Split("\n"))
                {
                    if (int.TryParse(line, out int pid))
                        Bash.Run($"kill -9 {pid}");
                }
            }
            catch { }
            #endregion

            clientIp = null;
            thread = null;
        }
    }
}
