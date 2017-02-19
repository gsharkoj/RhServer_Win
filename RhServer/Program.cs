using System;
using System.Collections.Generic;
using System.Text;
using System.Timers;
using System.Net.Sockets;
using System.Net;
using System.Threading;

namespace RhServer
{

    class Program
    {
        public static Server server = null;

        static void Main(string[] args)
        {
            server = new Server(45823);
            
            server.Go();
        }


        /*static void ClientThread(Object StateInfo)
        {
            server = new Server(45823);
            server.Go();

        }*/

        private static void tmrShow_Tick(object sender, ElapsedEventArgs e)
        {
            if (server!=null && server.Clients != null)
            {
                foreach (KeyValuePair<int, Client> t in server.Clients)
                {
                    lock (t.Value)
                    {
                        Console.WriteLine("sec out:" + t.Value.sec_out.ToString() + "; sec in:" + t.Value.sec_in.ToString() + "; kol = " +      t.Value.kol_image.ToString());
                        t.Value.sec_out = 0;
                        t.Value.sec_in = 0;
                        t.Value.kol_image = 0;
                    }
                }
            }

        }
    }
}


