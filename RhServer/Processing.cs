using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Net;
using System.Text;

namespace RhServer
{
    class Processing
    {
        Client client;

        public Processing(Client Client)
        {
            client = Client;
        }

        public String ReadClientData()
        {
            String s = "";
            Byte[] data = null;
            int count = ReadData(ref data);

            if (count != 0)
                s = UTF8Encoding.UTF8.GetString(data, 0, data.Length);
            return s;
        }

        public int Statistic()
        {
            String Request = "";
            int count = ReadDataHTTP_GET(ref Request);

            string[] s = Request.Split('\n');

            string param = s[0].Substring(0, s[0].IndexOf("HTTP")).Trim();
            param = param.Replace("/?", "");

            s = param.Split('&');

            string key = "";
            string command = "";
            foreach (string val in s)
            {

                string[] elem = val.Split('=');
                switch (elem[0])
                {
                    case "key":
                        key = elem[1];
                        break;
                    case "command":
                        command = elem[1];
                        break;
                }
            }

            if (key.Length > 0 && client.server.key.Length > 0)
            {
                if (client.server.key == key)
                {
                    string answer = "";
                    switch (command)
                    {
                        case "list":
                            foreach (KeyValuePair<int, Client> val in client.server.Clients)
                            {
                                string IP = ((IPEndPoint)val.Value.tcp_client.Client.RemoteEndPoint).Address.ToString();
                                string id = val.Value.id_client.ToString();
                                string id_partner = val.Value.id_client_partner.ToString();
                                string user_data = val.Value.user_data;
                                string read_data_kb = (val.Value.byte_read / 1024).ToString();
                                if (answer.Length > 0)
                                    answer += "\n";
                                answer += "id=" + id + "\tip=" + IP + "\tid_partner=" + id_partner + "\tuser_data=" + user_data;

                            }
                            break;
                    }
                    if (answer.Length > 0)
                    {
                        byte[] message = Encoding.ASCII.GetBytes(answer);
                        SendData(message);
                    }
                }
            }

            return count;
        }

        int ReadDataHTTP_GET(ref String Request)
        {
            byte[] buf = new byte[4096];
            Request = "";
            // Буфер для хранения принятых от клиента данных
            byte[] Buffer = new byte[1024];
            // Переменная для хранения количества байт, принятых от клиента
            int Count;
            int len = 0;
            // Читаем из потока клиента до тех пор, пока от него поступают данные

            while (client.client_stream.CanRead == false) ;

            while ((Count = client.client_stream.Read(Buffer, 0, Buffer.Length)) > 0)
            {
                len = len + Count;
                // Преобразуем эти данные в строку и добавим ее к переменной Request
                Request += Encoding.ASCII.GetString(Buffer, 0, Count);
                // Запрос должен обрываться последовательностью \r\n\r\n
                // Либо обрываем прием данных сами, если длина строки Request превышает 4 килобайта
                // Нам не нужно получать данные из POST-запроса (и т. п.), а обычный запрос
                // по идее не должен быть больше 4 килобайт
                if (Request.IndexOf("\r\n\r\n") >= 0 || Request.Length > 4096)
                {
                    break;
                }
            }
            return len;
        }

        public int SetID(int id)
        {
            Byte[] data = GetSimpleData(Command.set_id, id);
            SendData(data);
            data = null;
            return 0;
        }

        public int GetDataConnection()
        {
            Byte[] data = null;
            int count = ReadData(ref data);
            if (count < 8 || data == null)
                throw new Exception("GetDataConnection:Ошибка запроса соединения.");

            int id_client = BitConverter.ToInt32(data, 0);
            int byte_per_pixel = BitConverter.ToInt32(data, 4);

            Client cl = client.server.GetClient(id_client);
            if (cl == null)
            {
                Byte[] data_answer = GetSimpleData(Command.get_connect, ResultConnection.not_found);
                // отвечает, что клиент не найден
                SendData(data_answer);
                data_answer = null;
            }
            else
            {
                if (cl.id_client_partner != 0)
                {
                    Byte[] data_answer = GetSimpleData(Command.get_connect, ResultConnection.not_found);
                    // отвечает, что клиент не найден (уже используется сеанс связи)
                    SendData(data_answer);
                    data_answer = null;
                }
                else
                {
                    // связываем партнеров
                    client.id_client_partner = id_client;
                    cl.id_client_partner = client.id_client;

                    // клиент найден, посылаем ему приглашение
                    Byte[] byte_to_send = new Byte[12];
                    Buffer.BlockCopy(BitConverter.GetBytes(ResultConnection.ok.GetHashCode()), 0, byte_to_send, 0, 4);
                    Buffer.BlockCopy(BitConverter.GetBytes(client.id_client), 0, byte_to_send, 4, 4);
                    Buffer.BlockCopy(BitConverter.GetBytes(byte_per_pixel), 0, byte_to_send, 8, 4);

                    Byte[] data_answer = GetSimpleData(Command.get_connect, byte_to_send);
                    cl.SendData(data_answer);

                    byte_to_send = null;
                    data_answer = null;
                }
            }

            return count;
        }

        public int SetConnection()
        {
            Byte[] data = null;
            int count = ReadData(ref data);
            if (count < 4 || data == null)
                throw new Exception("SetConnection:Ошибка запроса соединения.");
            
            if(client.id_client_partner == 0)
                throw new Exception("SetConnection:Ошибка запроса соединения. Партнер не найден");

            int answer = BitConverter.ToInt32(data, 0);

            Client cl = client.server.GetClient(client.id_client_partner);

            switch (answer)
            {
                case (int) ResultConnection.negative:
                    // отвязываем партнеров                    
                    if (cl != null)
                    {
                        // посылаем сигнал об отказе соединения
                        Byte[] data_answer = GetSimpleData(Command.set_connect, ResultConnection.negative);
                        cl.SendData(data_answer);
                        data_answer = null;
                        cl.id_client_partner = 0;
                    }
                    client.id_client_partner = 0;
                    
                    // чистим структуру передачи данных

                    break;
                case (int)ResultConnection.ok:
                    // инициализируем структуры передачи данных
                    if (cl != null)
                    {
                        Byte[] data_answer = GetSimpleData(Command.set_connect, ResultConnection.ok);
                        cl.SendData(data_answer);
                        data_answer = null;
                    }
                    else
                    {
                        Byte[] data_answer = GetSimpleData(Command.set_stop, 0);
                        SendData(data_answer);
                        data_answer = null;
                        cl.id_client_partner = 0;
                    }
                    break;
                default:
                    throw new Exception("SetConnection:Ошибка запроса соединения. Не верный параметр ответа");
            }

            return count;
        }

        public int GetSize()
        {
            Byte[] data = null;
            int count = ReadData(ref data);
            if (count < 4 || data == null)
                throw new Exception("GetSize:Ошибка запроса соединения.");
            
            Client cl = client.server.GetClient(client.id_client_partner);

            if (cl == null)
            {
                Byte[] data_answer = GetSimpleData(Command.set_stop, 0);
                // отвечает, что клиент не найден
                SendData(data_answer);
                data_answer = null;
            }
            else
            {
                Byte[] data_answer = GetSimpleData(Command.get_size, data);
                cl.SendData(data_answer);
            }
            
            return count;
        }

        public int SetSize()
        {
            Byte[] data = null;
            int count = ReadData(ref data);
            if (count < 16 || data == null)
                throw new Exception("SetSize:Ошибка запроса соединения.");

            // читаем данные
            int w = BitConverter.ToInt32(data, 0); // W
            int h = BitConverter.ToInt32(data, 4); // H
            int len = BitConverter.ToInt32(data, 8); // len_x
            int size = BitConverter.ToInt32(data, 12); // byte_per_pixel
                        
            client.InitImageData(w, h, len, size);

            Client cl = client.server.GetClient(client.id_client_partner);
            Byte[] data_answer = GetSimpleData(Command.set_size, data);
            cl.SendData(data_answer);

            //
            data = null;
            data_answer = null;

            return count;
        }

        public int SetImage()
        {
            Byte[] data = null;
            int count = ReadData(ref data);
            if (count < 4 || data == null)
                throw new Exception("SetImage:Ошибка запроса соединения.");

            // подтверждаем получение данных  

            /*          
             Byte[] data_answer = GetSimpleData(Command.image_update, 0);
             SendData(data_answer);

            
             data_answer = null;
            */
            //
            Client cl_partner = client.server.GetClient(client.id_client_partner);

            if (cl_partner == null)
            {
                Byte[] data_answer = GetSimpleData(Command.set_stop, 0);
                // отвечает, что клиент не найден
                SendData(data_answer);
                data_answer = null;
            }
            else
            {

                if (data.Length == 0)
                {
                    Byte[] data_answer = GetSimpleData(Command.get_image, 0);
                    cl_partner.proc.SendData(data_answer);
                    data_answer = null;
                }
                else
                {
                    Byte[] data_answer = GetSimpleData(Command.get_image, data);
                    cl_partner.proc.SendData(data_answer);
                    data_answer = null;
                }
            }

            //client.SetImageData(ref data);
            data = null;
           

            return count;
        }

        public int GetImage()
        {
            Byte[] data = null;
            int count = ReadData(ref data);
            if (count < 4 || data == null)
                throw new Exception("GetImage:Ошибка запроса соединения.");

            Client cl = client.server.GetClient(client.id_client_partner);

            if (cl == null)
            {
                Byte[] data_answer = GetSimpleData(Command.set_stop, 0);
                // отвечает, что клиент не найден
                SendData(data_answer);
                data_answer = null;
            }
            else
            {
                int offset_data = cl.PrepareImageData();
                if (offset_data == 0)
                {
                    Byte[] data_answer = GetSimpleData(Command.get_image, 0);
                    SendData(data_answer);
                    data_answer = null;
                }
                else
                {
                    Byte[] data_answer = GetSimpleData(Command.get_image, cl.buf_image_data_send, offset_data);
                    SendData(data_answer);
                    data_answer = null;
                }
            }
            //
            data = null;
            return count;
        }
        
        public int SendImageData()
        {
          
            Client cl_partner = client.server.GetClient(client.id_client_partner);

            if (cl_partner == null)
            {
                Byte[] data_answer = GetSimpleData(Command.set_stop, 0);
                // отвечает, что клиент не найден
                SendData(data_answer);
                data_answer = null;
            }
            else
            {
                int offset_data = client.PrepareImageData();
                if (offset_data == 0)
                {
                    Byte[] data_answer = GetSimpleData(Command.get_image, 0);
                    cl_partner.proc.SendData(data_answer);
                    data_answer = null;
                }
                else
                {
                    Byte[] data_answer = GetSimpleData(Command.get_image, client.buf_image_data_send, offset_data);
                    cl_partner.proc.SendData(data_answer);
                    data_answer = null;
                }
            }
            //
            return 0;
        }

        public int SetStop()
        {
            Byte[] data = null;
            int count = ReadData(ref data);
            if (count < 4 || data == null)
                throw new Exception("SetStop:Ошибка запроса соединения.");
            Client cl = client.server.GetClient(client.id_client_partner);
            if (cl != null)
            {
                Byte[] data_answer = GetSimpleData(Command.set_stop, 0);
                cl.SendData(data_answer);
                
                client.id_client_partner = 0;
                cl.id_client_partner = 0;
            }

            data = null;
            return count;
        }

        public int SendEchoOk()
        {
            Console.WriteLine("Echo !!!!!!" + DateTime.Now.ToString());
            Byte[] data = null;
            int count = ReadData(ref data);
            if (count < 4 || data == null)
                throw new Exception("SetStop:Ошибка запроса соединения.");

            Byte[] data_answer = GetSimpleData(Command.echo_ok, 0);
            SendData(data_answer);

            data = null;
            return count;
        }

        public int SendPing()
        {
            Console.WriteLine("Ping !!!!!!" + DateTime.Now.ToString());
            Byte[] data = null;
            int count = ReadData(ref data);
            if (count < 4 || data == null)
                throw new Exception("SetStop:Ошибка запроса соединения.");

            Byte[] data_answer = GetSimpleData(Command.ping, 0);
            SendData(data_answer);

            data = null;
            return count;
        }
        public int SetClipboardData()
        {
            Byte[] data = null;
            int count = ReadData(ref data);
            //if (count < 4 || data == null)
                //throw new Exception("SetClipboardData:Ошибка запроса соединения.");
            Client cl = client.server.GetClient(client.id_client_partner);
            if (cl != null)
            {
                Byte[] data_answer = GetSimpleData(Command.set_clipboard_data, data);
                cl.SendData(data_answer);
            }
            else
                throw new Exception("SetClipboardData:Ошибка запроса соединения. Клиент не найден");

            Console.WriteLine("Buffer !!!!!!");

            data = null;
            return count;
        }

        public int SetMouseData()
        {
            Byte[] data = null;
            int count = ReadData(ref data);
            if (count < 4 || data == null)
                throw new Exception("SetMouseData:Ошибка запроса соединения.");

            Client cl = client.server.GetClient(client.id_client_partner);
            
            if (cl != null) 
            {            
                Byte[] data_answer = GetSimpleData(Command.set_mouse, data);
                cl.tcp_client.NoDelay = true;
                cl.SendData(data_answer);
                cl.tcp_client.NoDelay = false;
               /* if (BitConverter.ToInt32(data, 0) == (Int32)MouseCommand.move)
                {
                    // отправляем данные получение
                    data_answer = GetSimpleData(Command.mouse_update, 0);
                    SendData(data_answer);
                    data_answer = null;
                }*/
            }

            // подтверждаем получение данных мыши и клавиатуры
            // пока так не делаем
            data = null;
            return count;
        }


        public int SendFileData()
        {
            Byte[] data = null;
            int count = ReadData(ref data);
            //if (count < 4 || data == null)
               // throw new Exception("SendFileData:Ошибка запроса соединения.");
            if (count > 0)
            {

                Client cl = client.server.GetClient(client.id_client_partner);

                if (cl != null)
                {
                    Byte[] data_answer = GetSimpleData(Command.file_command, data);
                    cl.SendData(data_answer);
                }
                // подтверждаем получение данных мыши и клавиатуры
                // пока так не делаем
            }
            data = null;
            return count;
        }

        int ReadData(ref Byte[] Data)
        {
            int byte_to_read = 4;
            byte[] Buf_Head = new byte[byte_to_read];
            int offset = 0;
            // read data
            while (client.client_stream.CanRead == false);

            // длина сообщения
            while (true)
            {
                if (!client.tcp_client.Connected)
                    break;
                int count = client.client_stream.Read(Buf_Head, offset, (byte_to_read - offset));
                offset = offset + count;

                if (offset == byte_to_read)
                    break;
            }

            if (!client.tcp_client.Connected)
                return 0;

            // читаем остальное
            int len = BitConverter.ToInt32(Buf_Head, 0);
            if (len != 0)
            {
                Data = new byte[len];

                while (client.client_stream.CanRead == false);

                byte_to_read = len;
                offset = 0;
                while (true)
                {
                    if (!client.tcp_client.Connected)
                        break;

                    int count = client.client_stream.Read(Data, offset, (byte_to_read - offset));
                    offset = offset + count;

                    if (offset == byte_to_read)
                        break;
                }
            }
            else
            {
                Data = null;
            }
            return len;
        }

        public Boolean SendData(Byte[] data)
        {
            while (client.client_stream.CanWrite == false) ;
            client.client_stream.Write(data, 0, data.Length);
            return true;
        }

        public Boolean SendData(Byte[] data, int length)
        {
            while (client.client_stream.CanWrite == false) ;
            client.client_stream.Write(data, 0, length);
            return true;
        }

        Byte[] GetSimpleData(Enum command, int command_result)
        {
            Byte[] byte_to_send = new Byte[12];
            Buffer.BlockCopy(BitConverter.GetBytes(command.GetHashCode()), 0, byte_to_send, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(4), 0, byte_to_send, 4, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(command_result), 0, byte_to_send, 8, 4);
            return byte_to_send;
        }

        Byte[] GetSimpleData(Enum command)
        {
            Byte[] byte_to_send = new Byte[8];
            Buffer.BlockCopy(BitConverter.GetBytes(command.GetHashCode()), 0, byte_to_send, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(0), 0, byte_to_send, 4, 4);
            return byte_to_send;
        }

        Byte[] GetSimpleData(Enum command, Enum command_result)
        {
            Byte[] byte_to_send = new Byte[12];
            Buffer.BlockCopy(BitConverter.GetBytes(command.GetHashCode()), 0, byte_to_send, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(4), 0, byte_to_send, 4, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(command_result.GetHashCode()), 0, byte_to_send, 8, 4);
            return byte_to_send;
        }
        Byte[] GetSimpleData(Enum command, Byte[] Data, int offset = 0)
        {
            if (Data == null)
            {
                Byte[] byte_to_send = new Byte[8];
                Buffer.BlockCopy(BitConverter.GetBytes(command.GetHashCode()), 0, byte_to_send, 0, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(0), 0, byte_to_send, 4, 4);
                return byte_to_send;
            }
            else
            {
                Byte[] byte_to_send = null;
                if (offset == 0)
                    byte_to_send = new Byte[Data.Length + 8];
                else
                    byte_to_send = new Byte[offset + 8];

                Buffer.BlockCopy(BitConverter.GetBytes(command.GetHashCode()), 0, byte_to_send, 0, 4);
                if (offset == 0)
                {
                    Buffer.BlockCopy(BitConverter.GetBytes(Data.Length), 0, byte_to_send, 4, 4);
                    Buffer.BlockCopy(Data, 0, byte_to_send, 8, Data.Length);
                }
                else
                {
                    Buffer.BlockCopy(BitConverter.GetBytes(offset), 0, byte_to_send, 4, 4);
                    Buffer.BlockCopy(Data, 0, byte_to_send, 8, offset);
                }
                return byte_to_send;
            }
        }
        Byte[] GetSimpleData(int command, Byte[] Data)
        {
            if (Data == null)
            {
                Byte[] byte_to_send = new Byte[8];
                Buffer.BlockCopy(BitConverter.GetBytes(command), 0, byte_to_send, 0, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(0), 0, byte_to_send, 4, 4);
                return byte_to_send;
            }
            else
            {
                Byte[] byte_to_send = new Byte[Data.Length + 8];
                Buffer.BlockCopy(BitConverter.GetBytes(command), 0, byte_to_send, 0, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(Data.Length), 0, byte_to_send, 4, 4);
                Buffer.BlockCopy(Data, 0, byte_to_send, 8, Data.Length);
                return byte_to_send;
            }
        }
    }
}
