using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.ServiceProcess;
using System.Text;
using System.Threading;

namespace RhService
{
    public partial class Service : ServiceBase
    {
        public RhServer.Server server = null;
        Thread Thread;
        public Service()
        {
            InitializeComponent();
        }

        protected override void OnStart(string[] args)
        {
            
            String port = "45823";
            String key = "";
            
            string[] imagePathArgs = Environment.GetCommandLineArgs();
            if (imagePathArgs.Length > 0)
            {
                foreach (String str in imagePathArgs)
                {

                    String[] s = str.Split('=');
                    if (s.Length == 2)
                    {
                        if (s[0] == "port")
                            port = s[1];
                        if (s[0] == "key")
                            key = s[1];
                    }
                }
            }

            server = new RhServer.Server(Convert.ToInt32(port), key, eventLog1);
            Thread = new Thread(new ParameterizedThreadStart(ClientThread));
            Thread.Start(server);
        }

        protected void ClientThread(Object StateInfo)
        {
            RhServer.Server srv = (RhServer.Server)StateInfo;
            srv.Go();
        }

        protected override void OnStop()
        {
            if (server != null)
            {
                try
                {
                    server.StopListen();
                    server.Stop();
                }
                catch(Exception ex)
                {
                    //...
                }

                Thread.Sleep(500);

                try
                {
                    if (Thread.ThreadState != System.Threading.ThreadState.Aborted)
                    {
                        Thread.Abort();
                        Thread.Sleep(50);
                    }
                }
                catch (Exception ex)
                {
                    //...
                }

            }
            server = null;

        }

    }
}
