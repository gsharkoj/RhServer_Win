using System;
using System.Collections.Generic;
using System.Text;
using System.Net.Sockets;
using System.Net;
using System.Threading;
using System.Text.RegularExpressions;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Timers;
using System.Diagnostics;

namespace RhServer
{
    // Класс-обработчик клиента
    public class Client
    {

        public int sec_out = 0;
        public int sec_in = 0;
        public int kol_image = 0;
        public Server server = null;
        public NetworkStream client_stream = null;
        public TcpClient tcp_client = null;
        public int id_client = 0;
        public int id_client_partner = 0;
        public int byte_read = 0;
        // параметры картинки
        public int Size_W;
        public int Size_H;
        public int len_x;
        public int byte_per_pixel;
        public Boolean is_view = false;
        public Byte[] buf_image_data_send = null;
        public Byte[] buf_image_data_unzip = null;
        Boolean[] buf_index_image_data = null;
        public Boolean index_update = false;
        Byte[] buf_image_data = null;

        public Processing proc = null;
        public String user_data = "";
        // Конструктор класса. Ему нужно передавать принятого клиента от TcpListener
        public Client(TcpClient Client, Server Server)
        {
            server = Server;
            tcp_client = Client;
            tcp_client.SendBufferSize = 32 * 1024;// 
            tcp_client.ReceiveBufferSize = 32 * 1024; // 
            client_stream = tcp_client.GetStream();
            proc = new Processing(this);
        }

        public void SendData(Byte[] Data)
        {
            proc.SendData(Data);
        }

        public void InitImageData(int width, int height, int len, int size)
        {
            lock (this)
            {
                Size_W = width;
                Size_H = height;
                len_x = len;
                byte_per_pixel = size;

                index_update = false;

                // выделяем память
                int w_count = Size_W / len_x;
    
                buf_image_data = new Byte[Size_W * Size_H * byte_per_pixel];
                buf_image_data_send = new Byte[Size_W * Size_H + Size_W * Size_H * byte_per_pixel];
                buf_image_data_unzip = new Byte[4 * w_count * Size_H + Size_W * Size_H * byte_per_pixel];
                buf_index_image_data = new Boolean[w_count * Size_H];

                Array.Clear(buf_image_data, 0, buf_image_data.Length);
                Array.Clear(buf_image_data_send, 0, buf_image_data_send.Length);
                Array.Clear(buf_index_image_data, 0, buf_index_image_data.Length);
                Array.Clear(buf_image_data_unzip, 0, buf_image_data_unzip.Length);
            }
 
        }

        public void SetImageData(ref Byte[] Data)
        {
            lock (buf_image_data)
            {
                int offset = 0;
                GZipStream stream = new GZipStream(new MemoryStream(Data), CompressionMode.Decompress, true);
                
                    const int size = 4096 * 2;
                    byte[] buffer = new byte[size];
                    int count_read = 0;
                    while (true)
                    {
                        count_read = stream.Read(buffer, 0, size);
                        if (count_read == 0)
                            break;
                        Buffer.BlockCopy(buffer, 0, buf_image_data_unzip, offset, count_read);
                        offset += count_read;
                    }
                stream.Close();

                try
                {
                    if (offset != 0)
                    {
                        int index_j = len_x * byte_per_pixel;
                        int offset_local = 0;
                        while (offset_local < offset)
                        {
                            int index = BitConverter.ToInt32(buf_image_data_unzip, offset_local);
                            offset_local += 4;
                            Buffer.BlockCopy(buf_image_data_unzip, offset_local, buf_image_data, index, index_j);
                            offset_local += index_j;
                            buf_index_image_data[index / index_j] = false;
                        }
                        if (offset_local > 0)
                            index_update = true;
                    }
                }
                catch (Exception ex)
                {
                    server.WriteLog("SetImageData write:" + ex.Message);
                }
            }
        }

        public int PrepareImageData()
        {
            int t1 = Environment.TickCount;
            int offset = 0;
            lock (buf_image_data)
            {
                if (index_update)
                {
                    int index_j = len_x * byte_per_pixel;
                    for (int i = 0; i < buf_index_image_data.Length; i++)
                    {
                        if (buf_index_image_data[i] == false)
                        {
                            int index = i * index_j;
                            Buffer.BlockCopy(BitConverter.GetBytes(index), 0, buf_image_data_send, offset, 4);
                            offset += 4;
                            Buffer.BlockCopy(buf_image_data, index, buf_image_data_send, offset, index_j);
                            offset += index_j;
                            buf_index_image_data[i] = true;
                        }
                    }
                    index_update = false;
                }
            }

            // архивируем данные
            if (offset == 0)
                return 0;
            else
            {
                // сжимаем данные                
                var stream_out = new MemoryStream();
                GZipStream gz = new GZipStream(stream_out, CompressionMode.Compress, true);
                gz.Write(buf_image_data_send, 0, offset);
                gz.Close();

                int count_read = 0;
                offset = 0;
                stream_out.Position = 0;

                const int size = 4096*2;
                byte[] buffer = new byte[size];

                while (true)
                {
                    count_read = stream_out.Read(buffer, 0, size);
                    if (count_read == 0)
                        break;
                    Buffer.BlockCopy(buffer, 0, buf_image_data_send, offset, count_read);
                    offset += count_read;

                }
                stream_out.Close();

                return offset;
            }
        }

        public void Go()
        {
            byte[] Buf_Command = new byte[4];
            try
            {
                while (true)
                {
                    // Обнуляем счетчик
                    if (byte_read >= Int32.MaxValue - 5 * 1024 * 1024)
                        byte_read = 0;

                    if (this.server.stop_listen)
                        break;
                    if (tcp_client == null)
                        break;
                    if (tcp_client.Connected == false)
                        break;
                    while (client_stream.CanRead == false);

                    Array.Clear(Buf_Command, 0, Buf_Command.Length);
                    if (!ReadCommand(Buf_Command))
                        continue;

                    int command = BitConverter.ToInt32(Buf_Command, 0);

                    switch (command)
                    {
                        case (int)Command.get_id:
                            user_data = proc.ReadClientData();
                            id_client = this.server.GetID(this);
                            byte_read += proc.SetID(id_client);
                            if (id_client == 0)
                                Stop();
                            break;
                        case (int)Command.get_connect:
                            byte_read += proc.GetDataConnection();
                            break;
                        case (int)Command.set_connect:
                            byte_read += proc.SetConnection();
                            break;
                        case (int)Command.get_size:
                            byte_read += proc.GetSize();
                            break;
                        case (int)Command.set_size:
                            byte_read += proc.SetSize();
                            break;
                        case (int)Command.set_image:
                            if (is_view == false)
                                is_view = true;
                            byte_read += proc.SetImage();
                            //proc.SendImageData();
                            break;
                        case (int)Command.get_image:
                            byte_read += proc.GetImage();
                            break;
                        case (int)Command.set_mouse:
                            byte_read += proc.SetMouseData();
                            break;
                        case (int)Command.set_clipboard_data:
                            byte_read += proc.SetClipboardData();
                            break;
                        case (int)Command.set_stop:
                            byte_read += proc.SetStop();
                            break;
                        // HTTP "GET "
                        case 542393671:
                            byte_read += proc.Statistic();
                            Stop();
                            break;
                        case (int)Command.file_command:
                            byte_read += proc.SendFileData();
                            break;
                        case (int)Command.echo:
                            byte_read += proc.SendEchoOk();
                            break;
                        case (int)Command.ping:
                            byte_read += proc.SendPing();
                            break;
                        default:
                            Stop();
                            break;
                    }
                }
            }
            catch (IOException ex)
            {
                server.WriteLog(ex.Message);
                if (is_view)
                    server.WriteLog("Stop view id=" + id_client.ToString());
            }
            catch (SocketException ex)
            {
                server.WriteLog(ex.Message);
                if (is_view)
                    server.WriteLog("Stop view id=" + id_client.ToString());
            }
            catch(Exception ex)
            {
                server.WriteLog(ex.Message);
                if (is_view)
                    server.WriteLog("Stop view id=" + id_client.ToString());
            }
            
        }

        public void Stop(bool delete_from_dic = true)
        {
            try
            {
                if (tcp_client != null && tcp_client.Client != null && tcp_client.Connected)
                {
                    tcp_client.GetStream().Close();
                    tcp_client.Close();
                }
                
            }
            catch
            {
                //....
            }

            server.WriteLog("Client id " + id_client.ToString() + " disconnect");

            if (delete_from_dic)
            {
                if (server.Clients.ContainsKey(this.id_client))
                {
                    lock (server.Clients)
                    {
                        server.Clients.Remove(this.id_client);
                    }
                }
            }
        }

        public bool ReadCommand(byte[] Buf_Head)
        {
            int byte_to_read = 4;
            int offset = 0;

            while (client_stream.CanRead == false);
            while (true)
            {
                if (!tcp_client.Connected)
                    break;
                int count = 0;

                try
                {
                    count = client_stream.Read(Buf_Head, offset, (byte_to_read - offset));
                }
                catch
                {
                    // ...
                }

                if (count == 0)
                {
                    Thread.Sleep(200);
                    break;
                }
                offset = offset + count;

                if (offset == byte_to_read)
                    break;
            }

            if (offset == byte_to_read)
                return true;
            else
                return false;
        }

    }

    public struct ThreadClientParam
    {
        public Server server;
        public TcpClient client;
    }

    public class Server
    {
        TcpListener Listener = null;
        public Dictionary<int, Client> Clients = null;
        public String key;
        public bool stop_listen = false;

        int port;        
        EventLog event_log = null;        
        List<Thread> threads = null; 

        // Запуск сервера
        public Server(int Port)
        {
            Clients = new Dictionary<int, Client>();
            port = Port;
            threads = new List<Thread>();
        }

        public Server(int Port, String Key, EventLog Log)
        {
            Clients = new Dictionary<int, Client>();
            event_log = Log;
            port = Port;
            key = Key;
            threads = new List<Thread>();
        }

        public Client GetClient(int id)
        {
            if (Clients.ContainsKey(id))
                return Clients[id];
            else
                return null;
        }

        public int GetID(Client cl)
        {
            int id = 0;
            lock (Clients)
            {
                Random rand = new Random(DateTime.Now.Millisecond);
                int hop = 10000;
                while (true)
                {
                    if (hop <= 0)
                    {
                        // запишем, что не можем выдать номер
                        WriteLog("No id, ip " + ((IPEndPoint)cl.tcp_client.Client.RemoteEndPoint).Address.ToString());
                        break;
                    }
                    id = rand.Next(100, 999);
                    if (!Clients.ContainsKey(id))
                    {
                        Clients.Add(id, cl);
                        WriteLog("Connect client id=" + id.ToString() + "\tip=" + ((IPEndPoint)cl.tcp_client.Client.RemoteEndPoint).Address.ToString() +"\tuser data="+ cl.user_data);
                        break;
                    }
                    hop -= 1;
                }
            }            
            return id;
        }

        public void StopListen()
        {
            stop_listen = true;
            TcpClient client_end = new TcpClient();
            client_end.Connect("localhost", port);
            client_end.Close();
        }


        public void Go()
        {
            Listener = new TcpListener(IPAddress.Any, port);
            Listener.Start();
            
            WriteLog("Service start in port " + port.ToString());

            // В бесконечном цикле
            while (true)
            {
                if (stop_listen)
                    break;
                // Принимаем нового клиента
                TcpClient Client = Listener.AcceptTcpClient();
                if (stop_listen)
                {
                    Client.Close();
                    break;
                }
                // Создаем поток
                Thread Thread = new Thread(new ParameterizedThreadStart(ClientThread));
                threads.Add(Thread);

                ThreadClientParam param = new ThreadClientParam();
                param.server = this;
                param.client = Client;

                // И запускаем этот поток, передавая ему принятого клиента
                Thread.Start(param);                
            }
        }

        void ClientThread(Object StateInfo)
        {
            ThreadClientParam param = (ThreadClientParam)StateInfo;
            Client client = new Client(param.client, param.server);
            client.Go();
            client.Stop(true);
            client = null;
        }

        public void Stop()
        {
            // Если "слушатель" был создан
            try
            {
                if (Clients != null)
                {

                    foreach (KeyValuePair<int, Client> val in Clients)
                    {
                        try
                        {
                            if (val.Value != null)
                                val.Value.Stop(false);
                        }
                        catch (Exception ex)
                        {
                            WriteLog(ex.Message);
                        }
                    }
                    Clients.Clear();
                    Clients = null;
                }
                if (threads != null)
                {
                    foreach (Thread tr in threads)
                    {
                        try
                        {
                            tr.Abort();
                        }
                        catch(Exception ex)
                        {
                            //...
                            WriteLog(ex.Message);
                        }
                    }
                    threads.Clear();
                }
                if (Listener != null)
                {
                    // Остановим его
                    Listener.Server.Close();
                    Listener.Stop();                    
                    Listener = null;
                }
            }
            catch (Exception ex)
            {
                WriteLog(ex.Message);
            }

            WriteLog("Service stop");
        }

        public void WriteLog(String message)
        {
            try
            {
                if (event_log != null)
                {

                    if (!EventLog.SourceExists("RhService"))
                    {
                        EventLog.CreateEventSource("RhService", "RhService");
                    }
                    event_log.Source = "RhService";
                    event_log.Log = "RhService";
                    event_log.WriteEntry(message);
                }
            }
            catch { }
        }

        // Остановка сервера
        ~Server()
        {
            Stop();
        }
    }

}
