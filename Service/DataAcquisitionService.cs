using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


namespace DataAcquisitionServerApp
{
    public class TcpListenerWrapper
    {
        public TcpListener Listener { get; set; }
    }
    public  class SerialPortSetting
    {
        public string PortName { get; set; }
        public int BaudRate { get; set; }
    }
    public class TCPServerSetting
    {
        public string TCPAddress { get; set; }
        public int TCPPort { get; set; }
    }
    public class DataAcquisitionService
    {
        SerialPort _serialPort = new SerialPort();
        HttpListener _httpListener = new HttpListener();
        CancellationTokenSource _cancellationTokenSource1 = new CancellationTokenSource();
        CancellationTokenSource _cancellationTokenSource2 = new CancellationTokenSource();
        List<WebSocket> _connectedClients = new List<WebSocket>();
        List<TcpClient> _connectedTcpClients = new List<TcpClient>();
        TcpListenerWrapper? _listenerWrapper1 = null;
        TcpListenerWrapper? _listenerWrapper2 = null;
        SerialPortSetting serialPortSetting = new SerialPortSetting();  
        TCPServerSetting tcpServerSetting = new TCPServerSetting();

        int _clientCount = 0;

        public  DataAcquisitionService()
        {
            serialPortSetting.PortName = "COM1";
            serialPortSetting.BaudRate = 9600;
            tcpServerSetting.TCPPort = 9009;
            tcpServerSetting.TCPAddress = "127.0.0.1";
        }

        public async Task RunAsync()
        {
            Console.WriteLine("按 S 键启动服务，P 停止服务，Q 退出。");

            while (true)
            {
                var key = Console.ReadKey(true).Key;

                if (key == ConsoleKey.S)
                {
                    await StartServicesAsync();
                }
                else if (key == ConsoleKey.P)
                {
                    StopServices();
                }
                else if (key == ConsoleKey.Q)
                {
                    StopServices();
                    break;
                }
            }
        }

        public void StopServices()
        {
            if (_serialPort != null)
            {
                _serialPort.Close();
                _serialPort.Dispose();
                Console.WriteLine("UART 服务已关闭");
            }

            if (_httpListener != null)
            {
                _cancellationTokenSource1.Cancel();
                _httpListener.Stop();
                _httpListener.Close();
                Console.WriteLine("WebSocket 服务已停止");
                _connectedClients.Clear();
                Console.WriteLine($"当前客户端连接数量: {_connectedClients.Count}");
            }
            if (_listenerWrapper1 != null)
            {
                _listenerWrapper1.Listener.Stop();
                _listenerWrapper1 = null;
                Console.WriteLine("TCP 服务已停止");
            }
            if (_listenerWrapper2 != null)
            {
                _listenerWrapper2.Listener.Stop();
                _listenerWrapper2 = null;
                Console.WriteLine("TELNET 服务已停止");
            }

        }

        public async Task StartServicesAsync()
        {
            if (!_serialPort.IsOpen)
            {
                Console.WriteLine("Starting UART service...");
                try
                {
                    _serialPort = new SerialPort(serialPortSetting.PortName.ToString(), serialPortSetting.BaudRate, Parity.None, 8, StopBits.One);
                    _serialPort.Open();
                    _serialPort.DataReceived += SerialPortDataReceived;
                }
                catch (Exception e)
                {

                    Console.WriteLine($"{e}");
                }

                Console.WriteLine("UART服务 已启动");
            }
            else
            {
                Console.WriteLine("Closing UART service...");
                _serialPort.Close();
                _serialPort.Dispose();
                Console.WriteLine("UART 服务已关闭");

                Console.WriteLine("Starting UART service...");
                try
                {
                    _serialPort = new SerialPort(serialPortSetting.PortName.ToString(), serialPortSetting.BaudRate, Parity.None, 8, StopBits.One);
                    _serialPort.Open();
                    _serialPort.DataReceived += SerialPortDataReceived;
                }
                catch (Exception e)
                {

                    Console.WriteLine($"{e}");
                }

                Console.WriteLine("UART服务 已启动");
            }

            if (!_httpListener.IsListening)
            {
                Console.WriteLine("Starting Webscoket service...");
                await Task.Factory.StartNew(() => ListenWebsocket(_httpListener, _cancellationTokenSource1, "127,0,0,1", 5000));
                Console.WriteLine("WebSocket 服务器已启动");
            }

            if (_listenerWrapper1 == null)
            {

                Console.WriteLine("Starting TCP service...");
                _listenerWrapper1 = new TcpListenerWrapper();
                await Task.Factory.StartNew(() => ListenTcp(_listenerWrapper1, _cancellationTokenSource2, tcpServerSetting.TCPAddress,tcpServerSetting.TCPPort));
                Console.WriteLine("TCP服务 已启动");
            }
            if (_listenerWrapper2 == null)
            {
                Console.WriteLine("Starting Telnet service...");
                _listenerWrapper2 = new TcpListenerWrapper();
                 TelnetService.StartTelnetServerAsync(_listenerWrapper2,"127.0.0.1", 23);
                Console.WriteLine("Telnet服务 已启动");

            }

        }

        async void ListenTcp(TcpListenerWrapper tcplistener, CancellationTokenSource cancellationToken, string ipAddress, int port)
        {
            tcplistener.Listener = new TcpListener(IPAddress.Any, port);
            tcplistener.Listener.Start();
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client = await tcplistener.Listener.AcceptTcpClientAsync();
                Console.WriteLine($"客户端 {_clientCount++} 已连接");

                // 存储客户端对象
                _connectedTcpClients.Add(client);
                // 为每个客户端创建一个新线程进行处理
                await Task.Run(async () => await HandleClient(client));
            }
        }


        async Task HandleClient(TcpClient client)
        {
            try
            {
                var stream = client.GetStream();
                var buffer = new byte[1024];

                while (client.Connected)
                {
                    var byteCount = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (byteCount > 0)
                    {
                        var message = Encoding.UTF8.GetString(buffer, 0, byteCount);
                        Console.WriteLine($"收到来自客户端的消息：{message}");
                        foreach (var item in _connectedClients)
                        {
                            await item.SendAsync(new ArraySegment<byte>(buffer, 0, byteCount), WebSocketMessageType.Text, true, CancellationToken.None);
                            Console.WriteLine($"发送消息到客户端: {message}");
                        }
                    }
                    else
                    {
                        break;
                    }
                }

            }
            catch (Exception e)
            {
                Console.WriteLine($"处理客户端 {_clientCount} 时出错：{e.Message}");
            }
            finally
            {
                client.Close();
                Console.WriteLine($"客户端 {_clientCount--} 已断开");
            }
        }

        async Task ListenWebsocket(HttpListener httpListener, CancellationTokenSource cancellationToken, string ipAddress, int port)
        {
            cancellationToken = new CancellationTokenSource();
            httpListener = new HttpListener();
            httpListener.Prefixes.Add($"{ipAddress}:{port}/");
            httpListener.Start();
            while (!cancellationToken.IsCancellationRequested)
            {
                var context = await _httpListener.GetContextAsync();

                if (context.Request.IsWebSocketRequest)
                {

                    try
                    {
                        var webSocketContext = await context.AcceptWebSocketAsync(null);
                        Console.WriteLine("WebSocket 连接已建立");
                        await WebSocketConnectionHandler(webSocketContext.WebSocket);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("WebSocket 连接错误: " + e.Message);
                    }
                }
                else
                {
                    context.Response.StatusCode = 400;
                    context.Response.Close();
                }
            }
        }


        async Task WebSocketConnectionHandler(WebSocket webSocket)
        {
            var buffer = new byte[1024 * 4];
            _connectedClients.Add(webSocket);
            Console.WriteLine($"当前客户端连接数量: {_connectedClients.Count}");
            while (true)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Text)
                {
                    string message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    Console.WriteLine($"收到来自 WebSocket 客户端的消息: {message}");

                    // 处理从 WebSocket 客户端接收到的消息，例如将其发送到串口
                    if (_serialPort != null && _serialPort.IsOpen && !string.IsNullOrEmpty(message))
                    {
                        _serialPort.Write(message);
                    }
                    if (_listenerWrapper1.Listener != null)
                    {
                        foreach (var tcpClient in _connectedTcpClients)
                        {
                            /*  tcpClient.Client.RemoteEndPoint as IPEndPoint：尝试将RemoteEndPoint属性转换为IPEndPoint类型。如果RemoteEndPoint为null或不能转换为IPEndPoint，as操作符将返回null。

                              (tcpClient.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString()：使用null条件访问操作符（?.），如果转换后的IPEndPoint对象不为null，我们将访问其Address属性并调用ToString()方法将IP地址转换为字符串。如果IPEndPoint对象为null，则整个表达式将返回null。

                              string remoteAddress = ... ?? string.Empty;：空合并操作符（??）用于处理上述表达式返回的null值。如果表达式结果为null，remoteAddress将被赋值为空字符串（string.Empty）。如果表达式结果不为null，remoteAddress将被赋值为IP地址字符串。

                              综上所述，这段代码的作用是尝试从tcpClient对象获取远程客户端的IP地址字符串，如果无法获取（例如，RemoteEndPoint为null），则将remoteAddress赋值为空字符串。*/
                            string remoteAddress = (tcpClient.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? string.Empty;

                            if (remoteAddress == "192.168.1.103")
                            {
                                try
                                {
                                    var stream = tcpClient.GetStream();
                                    var sendBuffer = Encoding.UTF8.GetBytes(message);
                                    await stream.WriteAsync(sendBuffer, 0, sendBuffer.Length);
                                    Console.WriteLine($"发送消息到 TCP 客户端: {message}");
                                }
                                catch (Exception e)
                                {
                                    Console.WriteLine($"发送消息到 TCP 客户端出错：{e.Message}");
                                }
                            }

                        }
                    }
                }
                else if (result.MessageType == WebSocketMessageType.Close)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "关闭连接", CancellationToken.None);
                    Console.WriteLine("WebSocket 连接已关闭");
                    _connectedClients.Remove(webSocket);
                    Console.WriteLine($"当前客户端连接数量: {_connectedClients.Count}");
                    break;
                }
            }
        }

        void SerialPortDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            SerialPort serialPort = (SerialPort)sender;
            string data = serialPort.ReadExisting();
            Console.WriteLine($"从串口收到数据: {data}");
            byte[] buffer = Encoding.UTF8.GetBytes(data);

            foreach (var client in _connectedClients)
            {
                client.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);
                Console.WriteLine($"发送消息到客户端: {data}");
            }
        }


        public void UpdateSerialPortSettings(string port,int baud)
        {
            if (_serialPort != null && _serialPort.IsOpen)
            {
                _serialPort.Close();
            }

            _serialPort.PortName = serialPortSetting.PortName ?? _serialPort.PortName;
            _serialPort.BaudRate =  _serialPort.BaudRate;

            _serialPort.Open();
        }

        public  async void UpdateTCPServerSettings(string address, int port)
        {
            tcpServerSetting.TCPPort = port;
            tcpServerSetting.TCPAddress = address;
            if (_listenerWrapper1!=null)
            {
                _listenerWrapper1.Listener.Stop();
                _listenerWrapper1 = null;
                Console.WriteLine("TCP 服务已停止");

                Console.WriteLine("Starting TCP service...");
                _listenerWrapper1 = new TcpListenerWrapper();
                await Task.Factory.StartNew(() => ListenTcp(_listenerWrapper1, _cancellationTokenSource2, tcpServerSetting.TCPAddress, tcpServerSetting.TCPPort));
                Console.WriteLine("TCP服务 已启动");
            }
        }
    }
}



