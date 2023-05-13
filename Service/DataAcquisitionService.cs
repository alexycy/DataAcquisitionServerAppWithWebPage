using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using MQTTnet;
using MQTTnet.Server;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DataAcquisitionServerAppWithWebPage.Pages;


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
    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public LogLevel Level { get; set; } = LogLevel.None;
        public string Message { get; set; } = "";
    }

    public class compositeClient
    {
      public  TcpClient _tcpClient;
      public  DateTime  _dateTime;
    }
    
    public class  deviceList
    {
        public bool ifStartSerialPort1 { get; set; }
        public bool ifStartSerialPort2 { get; set; }
        public bool ifStartTCPServer { get; set; }

        public bool ifStartWebSocketServer { get; set; }
        public bool ifStartMqttServer { get; set; }
    }
    public class deviceState
    {
        public deviceState()
        {
            ifRunningSerialPort1 = false;
            ifRunningSerialPort2 = false;
            ifRunningTCPServer = false;
            ifRunningWebSocketServer = false;
            ifRunningMqttServer = false;
        }
        public bool ifRunningSerialPort1 { get; set; }
        public bool ifRunningSerialPort2 { get; set; }
        public bool ifRunningTCPServer { get; set; }

        public bool ifRunningWebSocketServer { get; set; }
        public bool ifRunningMqttServer { get; set; }
    }
    public class DataAcquisitionService
    {
        public event Action OnDeviceStateChanged;
        private readonly MqttService _mqttService;
        public deviceState deviceState = new deviceState();
        public deviceList deviceList = new deviceList();
        SerialPort _serialPort = new SerialPort();
        HttpListener _httpListener = new HttpListener();
        CancellationTokenSource _cancellationTokenSource1 = new CancellationTokenSource();
        CancellationTokenSource _cancellationTokenSource2 = new CancellationTokenSource();
        public List<WebSocket> _connectedClients = new List<WebSocket>();
        public List<compositeClient> _connectedTcpClients = new List<compositeClient>();
        TcpListenerWrapper? _listenerWrapper1 = null;
        TcpListenerWrapper? _listenerWrapper2 = null;
        SerialPortSetting serialPortSetting = new SerialPortSetting();  
        TCPServerSetting tcpServerSetting = new TCPServerSetting();
        public Queue<LogEntry> logQueue = new Queue<LogEntry>();  
        int _clientCount = 0;

        public  DataAcquisitionService(IServiceProvider serviceProvider)
        {
            //在 Blazor 中，依赖注入通常在组件中使用，但如果你想在一个普通的类中使用，需要传递 IServiceProvider 来解析服务
            _mqttService = serviceProvider.GetService(typeof(MqttService)) as MqttService;

            

            serialPortSetting.PortName = "COM1";
            serialPortSetting.BaudRate = 9600;
            tcpServerSetting.TCPPort = 9009;
            tcpServerSetting.TCPAddress = "127.0.0.1";
        }

        public void ChangeDeviceState(deviceState device, string propertyName)
        {
            // 获取device类型的指定属性
            var property = device.GetType().GetProperty(propertyName);
            if (property != null && property.PropertyType == typeof(bool))
            {
                // 获取当前属性值
                bool currentValue = (bool)property.GetValue(device);

                // 更改属性值
                property.SetValue(device, !currentValue);

                // 触发事件
                OnDeviceStateChanged?.Invoke();
            }
            else
            {
                // 如果找不到该属性，或者属性类型不是bool，抛出异常
                throw new ArgumentException($"Property {propertyName} not found or not of type bool.");
            }
        }


        public async Task RunAsync()
        {
            LogMessage("按 S 键启动服务，P 停止服务，Q 退出。",LogLevel.Information);

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

        public async void StopServices()
        {
            if (deviceState.ifRunningMqttServer && deviceList.ifStartMqttServer)
            {
                await _mqttService.StopMQTTService();
                LogMessage("MQTT 服务已关闭", LogLevel.Information);
                ChangeDeviceState(deviceState, "ifRunningMqttServer");
            }
            if (_serialPort != null&& deviceList.ifStartSerialPort1)
            {
                _serialPort.Close();
                _serialPort.Dispose();
                LogMessage("UART 服务已关闭",LogLevel.Information);
                ChangeDeviceState(deviceState, "ifRunningSerialPort1");
            }

            if (_httpListener != null&& deviceList.ifStartWebSocketServer)
            {
                _connectedClients.Clear();
                _cancellationTokenSource2.Cancel();
                _httpListener.Stop();
                _httpListener.Close();
                LogMessage("WebSocket 服务已停止",LogLevel.Information);
                ChangeDeviceState(deviceState, "ifRunningWebSocketServer");
                Console.WriteLine($"当前客户端连接数量: {_connectedClients.Count}");
            }
            if (_listenerWrapper1 != null&& deviceList.ifStartTCPServer)
            {
                _connectedTcpClients.Clear(); 
                _cancellationTokenSource1.Cancel();
                _listenerWrapper1.Listener.Stop();
                _listenerWrapper1 = null;
                LogMessage("TCP 服务已停止",LogLevel.Information);
                ChangeDeviceState(deviceState, "ifRunningTCPServer");
            }
            if (_listenerWrapper2 != null)
            {
                _listenerWrapper2.Listener.Stop();
                _listenerWrapper2 = null;
                LogMessage("TELNET 服务已停止",LogLevel.Information);
            }

        }

        public async Task StartServicesAsync()
        {
            if (!deviceState.ifRunningMqttServer && deviceList.ifStartMqttServer)
            {
                await _mqttService.StartMQTTService();
                LogMessage("MQTT服务 已启动", LogLevel.Information);    LogMessage($"MQTT服务启动状态: {_mqttService.mqttServer.IsStarted},MQTT服务器参数:{_mqttService.mqttServer.ServerSessionItems}", LogLevel.Information);
            
                ChangeDeviceState(deviceState, "ifRunningMqttServer");
            }
            if (!_serialPort.IsOpen && deviceList.ifStartSerialPort1)
            {
                LogMessage("Starting UART service...",LogLevel.Information);
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

                LogMessage("UART服务 已启动",LogLevel.Information);
                ChangeDeviceState(deviceState, "ifRunningSerialPort1");
            }
            else
            {
                LogMessage("Closing UART service...",LogLevel.Information);
                _serialPort.Close();
                _serialPort.Dispose();
                LogMessage("UART 服务已关闭",LogLevel.Information);
                ChangeDeviceState(deviceState, "ifRunningSerialPort1");
                LogMessage("Starting UART service...",LogLevel.Information);
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

                LogMessage("UART服务 已启动",LogLevel.Information);
                ChangeDeviceState(deviceState, "ifRunningSerialPort1");
            }

            if (!_httpListener.IsListening && deviceList.ifStartWebSocketServer)
            {
                LogMessage("Starting Webscoket service...",LogLevel.Information);
                _cancellationTokenSource2 = new CancellationTokenSource();
                _httpListener = new HttpListener();
                _httpListener.Prefixes.Add("http://192.168.1.101:5005/");
                _httpListener.Start();
                Console.WriteLine("WebSocket 服务器已启动");
                Task.Run(() => ListenWebSocket(_cancellationTokenSource2.Token));


                LogMessage("WebSocket 服务器已启动",LogLevel.Information);
                ChangeDeviceState(deviceState, "ifRunningWebSocketServer");
            }

            if (_listenerWrapper1 == null&& deviceList.ifStartTCPServer)
            {

                LogMessage("Starting TCP service...",LogLevel.Information);
                _connectedTcpClients.Clear();
                await Task.Factory.StartNew(() => ListenTcp(tcpServerSetting.TCPAddress,tcpServerSetting.TCPPort));
                LogMessage("TCP服务 已启动",LogLevel.Information);
                ChangeDeviceState(deviceState, "ifRunningTCPServer");
            }
            if (_listenerWrapper2 == null)
            {
                LogMessage("Starting Telnet service...",LogLevel.Information);
                _listenerWrapper2 = new TcpListenerWrapper();
                 TelnetService.StartTelnetServerAsync(_listenerWrapper2,"127.0.0.1", 23);
                LogMessage("Telnet服务 已启动",LogLevel.Information);

            }

        }

        async void ListenTcp(string ipAddress, int port)
        {
            _listenerWrapper1 = new TcpListenerWrapper();
            _listenerWrapper1.Listener = new TcpListener(IPAddress.Any, port);
            _listenerWrapper1.Listener.Start();
            try
            {
                while (!_cancellationTokenSource1.IsCancellationRequested && _listenerWrapper1 != null)
                {
                    TcpClient client = await _listenerWrapper1.Listener.AcceptTcpClientAsync();
                    LogMessage($"客户端 {_clientCount++} 已连接",LogLevel.Information);
                    compositeClient cc = new compositeClient();
                    cc._tcpClient = client;
                    cc._dateTime = DateTime.Now;
                    // 存储客户端对象
                    _connectedTcpClients.Add(cc);
                    // 为每个客户端创建一个新线程进行处理
                    Task.Run(async () => await HandleClient(client));
                }
            }
            catch (Exception e)
            {

                LogMessage($"{e}",LogLevel.Error);
                return;
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
                foreach (var item in _connectedTcpClients)
                {
                    if (item._tcpClient == client)
                    {
                        _connectedTcpClients.Remove(item);
                    }
                }
                
                client.Close();
                Console.WriteLine($"客户端 {_clientCount--} 已断开");
            }
        }

        async Task ListenWebsocket(string ipAddress, int port)
        {
            _cancellationTokenSource2 = new CancellationTokenSource();
            _httpListener = new HttpListener();
            //var address = $"{ipAddress}:{port}/";
            //httpListener.Prefixes.Add(address);
            _httpListener.Prefixes.Add("http://192.168.1.101:9008/");
            _httpListener.Start();
            while (!_cancellationTokenSource2.IsCancellationRequested)
            {
                var context = await _httpListener.GetContextAsync();

                if (context.Request.IsWebSocketRequest)
                {

                    try
                    {
                        var webSocketContext = await context.AcceptWebSocketAsync(null);
                        LogMessage("WebSocket 连接已建立",LogLevel.Information);
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
                        foreach (var item in _connectedTcpClients)
                        {
                            /*  tcpClient.Client.RemoteEndPoint as IPEndPoint：尝试将RemoteEndPoint属性转换为IPEndPoint类型。如果RemoteEndPoint为null或不能转换为IPEndPoint，as操作符将返回null。

                              (tcpClient.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString()：使用null条件访问操作符（?.），如果转换后的IPEndPoint对象不为null，我们将访问其Address属性并调用ToString()方法将IP地址转换为字符串。如果IPEndPoint对象为null，则整个表达式将返回null。

                              string remoteAddress = ... ?? string.Empty;：空合并操作符（??）用于处理上述表达式返回的null值。如果表达式结果为null，remoteAddress将被赋值为空字符串（string.Empty）。如果表达式结果不为null，remoteAddress将被赋值为IP地址字符串。

                              综上所述，这段代码的作用是尝试从tcpClient对象获取远程客户端的IP地址字符串，如果无法获取（例如，RemoteEndPoint为null），则将remoteAddress赋值为空字符串。*/
                            string remoteAddress = (item._tcpClient.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? string.Empty;

                            if (remoteAddress == "192.168.1.103")
                            {
                                try
                                {
                                    var stream = item._tcpClient.GetStream();
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
                    LogMessage("WebSocket 连接已关闭",LogLevel.Information);
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
                LogMessage("TCP 服务已停止",LogLevel.Information);

                LogMessage("Starting TCP service...",LogLevel.Information);
                _listenerWrapper1 = new TcpListenerWrapper();
                await Task.Factory.StartNew(() => ListenTcp(tcpServerSetting.TCPAddress, tcpServerSetting.TCPPort));
                LogMessage("TCP服务 已启动",LogLevel.Information);
            }
        }

        private LogEntry GenerateLog(string message ,LogLevel level)
        {
            LogEntry entry = new LogEntry();

            entry.Level = level; 
            entry.Message = message;
            entry.Timestamp = DateTime.Now;

            return entry;
        }


        public void LogMessage(string message,LogLevel level)
        {
            Console.WriteLine(message);
            logQueue?.Enqueue(GenerateLog(message, level));
        }

        private  async Task ListenWebSocket(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var context = await _httpListener.GetContextAsync();

                if (context.Request.IsWebSocketRequest)
                {
                    WebSocketContext webSocketContext = null;

                    try
                    {
                        webSocketContext = await context.AcceptWebSocketAsync(null);
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
    }
}



