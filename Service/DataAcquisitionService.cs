using DataAcquisitionServerAppWithWebPage.Data;
using DataAcquisitionServerAppWithWebPage.Service;
using Newtonsoft.Json;
using System.Data;
using System.IO.Ports;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Xml;

namespace DataAcquisitionServerApp
{
    public class TcpListenerWrapper
    {
        public TcpListener Listener { get; set; }
    }
    public class SerialPortSetting
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
        public TcpClient _tcpClient;
        public DateTime _dateTime;
    }

    public class deviceList
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
        public TcpServer? _tcpServer;
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
        private WebSocketServer? _webSocketServer;
        private SerialPortIOServer? _serialIOPortServer;
        private static System.Timers.Timer timer;
        private DatabaseManager _databaseManager;
        //private readonly ILogger _logger;
        public DataAcquisitionService(IServiceProvider serviceProvider/*,ILogger<DataAcquisitionService> logger*/)
        {
            //在 Blazor 中，依赖注入通常在组件中使用，但如果你想在一个普通的类中使用，需要传递 IServiceProvider 来解析服务
            _mqttService = serviceProvider.GetService(typeof(MqttService)) as MqttService;
            //单例模式实现
            _tcpServer = serviceProvider.GetService(typeof(TcpServer)) as TcpServer;
            _webSocketServer = serviceProvider.GetService(typeof(WebSocketServer)) as WebSocketServer;
            _serialIOPortServer = serviceProvider.GetService(typeof(SerialPortIOServer)) as SerialPortIOServer;

            //_logger = logger;

            DataGlobal.nowRecordTableName = GetNowRecordTableName();


            // Calculate the time until midnight
            TimeSpan timeToMidnight = DateTime.Today.AddDays(1) - DateTime.Now;

            // Create a timer
            timer = new System.Timers.Timer();

            // Set the timer to execute the CreateNewTable method once a day
            timer.Elapsed += CreateNewTable;

            // Set the timer interval to 1 day in milliseconds
            //timer.Interval = TimeSpan.FromDays(1).TotalMilliseconds;
            timer.Interval = timeToMidnight.TotalMilliseconds;

            // Start the timer after a delay
            //Task.Delay(timeToMidnight).ContinueWith(t => timer.Start());
            timer.Start();





            if (_listenerWrapper1 == null)
            {

                LogMessage("Starting TCP service...", LogLevel.Information);
                _connectedTcpClients.Clear();
                //await Task.Factory.StartNew(() => ListenTcp(tcpServerSetting.TCPAddress,tcpServerSetting.TCPPort));
                _tcpServer.StartServerAsync();
                LogMessage("TCP服务 已启动", LogLevel.Information);
                ChangeDeviceState(deviceState, "ifRunningTCPServer");
            }

            if (_listenerWrapper2 == null)
            {
                LogMessage("Starting WebSocket service...", LogLevel.Information);
                _connectedClients.Clear();
                _webSocketServer.StartServerAsync();
                LogMessage("WebSocket服务 已启动", LogLevel.Information);
                ChangeDeviceState(deviceState, "ifRunningWebSocketServer");
            }


            //LogMessage("Starting SerialPortIO service...", LogLevel.Information);
            //_serialIOPortServer.StartSerialPortIOServer();
            //ChangeDeviceState(deviceState, "ifRunningSerialPort1");

            // 在构造函数中启动异步任务

            Task.Run(async () =>
            {
                try
                {
                    await StartPingTaskAsync();
                }
                catch (Exception ex)
                {
                    // 处理异常
                    //_logger.LogInformation(DateTime.Now.ToString()+":"+ex);
                }
            });

            Task.Run(async () =>
            {
                try
                {
                    await StateMonitor();
                }
                catch (Exception ex)
                {
                    // 处理异常
                    //_logger.LogInformation(DateTime.Now.ToString()+":"+ex);
                }
            });

        }


        private void CreateNewTable(Object source, System.Timers.ElapsedEventArgs e)
        {
            int maxRetryCount = 3;
            int retryCount = 0;
            bool success = false;

            while (!success && retryCount < maxRetryCount)
            {
                try
                {
                    // Get the current date
                    string date = DateTime.Now.ToString("yyyyMMdd");

                    // Create the new table name
                    string newTableName = "fct_measure_" + date;
                    var dbHelper = new DatabaseHelper();
                    var query = $"SHOW TABLES LIKE '{newTableName}'";
                    DataTable dt = dbHelper.ExecuteQuery(query);
                    if (dt.Container == null)
                    {
                        query = $"CREATE TABLE {newTableName} LIKE fct_measure";
                        DataGlobal.nowRecordTableName = newTableName;
                        dbHelper.ExecuteNonQuery(query);
                        string name = DataGlobal.nowRecordTableName;

                        SetTableName(name);

                        //_logger.LogInformation(DateTime.Now.ToString() + ":" + $"Have created a new Table :{newTableName}");
                        success = true;
                    }

                    timer.Interval = TimeSpan.FromDays(1).TotalMilliseconds;
                }
                catch (Exception ex)
                {
                    retryCount++;
                    //_logger.LogInformation(DateTime.Now.ToString() + ":" + "Failed to create a database table.and retry create, Exception  " + ex.Message);
                    System.Threading.Thread.Sleep(5000); // wait for 5 seconds before retrying
                }
            }

            if (!success)
            {
                //_logger.LogInformation(DateTime.Now.ToString() + ":" + "Failed to create a database table. After 3 retry times" );
            }
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

        public async Task StateMonitor()
        {
            while (true)
            {
                // 检查 TCP 服务器状态
                if (!_tcpServer.IsRunning)
                {
                    // TCP 服务器已停止，抛出错误或进行其他处理
                    //_logger.LogInformation(DateTime.Now.ToString()+":"+"The TCP server is shut down abnormally ！");
                }

                DatabaseHelper db = new DatabaseHelper();


                if (SystemState.canConnectSQL != db.ChecSQLConnection())
                {
                    SystemState.canConnectSQL = db.ChecSQLConnection();
                    //_logger.LogInformation(DateTime.Now.ToString()+":"+$"The State of Connecting to MySQL:{SystemState.canConnectSQL}");
                }

                // 等待一段时间再进行下一次检查
                await Task.Delay(TimeSpan.FromSeconds(5));
            }
        }

        public async Task RunAsync()
        {
            LogMessage("按 S 键启动服务，P 停止服务，Q 退出。", LogLevel.Information);

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
            if (_serialPort != null && deviceList.ifStartSerialPort1)
            {
                _serialPort.Close();
                _serialPort.Dispose();
                LogMessage("UART 服务已关闭", LogLevel.Information);
                ChangeDeviceState(deviceState, "ifRunningSerialPort1");
            }

            if (_httpListener != null && deviceList.ifStartWebSocketServer)
            {
                _connectedClients.Clear();
                _cancellationTokenSource2.Cancel();
                _httpListener.Stop();
                _httpListener.Close();
                LogMessage("WebSocket 服务已停止", LogLevel.Information);
                ChangeDeviceState(deviceState, "ifRunningWebSocketServer");
                //_logger.LogInformation(DateTime.Now.ToString()+":"+$"当前客户端连接数量: {_connectedClients.Count}");
            }
            if (_listenerWrapper1 != null && deviceList.ifStartTCPServer)
            {
                _connectedTcpClients.Clear();
                _cancellationTokenSource1.Cancel();
                _listenerWrapper1.Listener.Stop();
                _listenerWrapper1 = null;
                LogMessage("TCP 服务已停止", LogLevel.Information);
                ChangeDeviceState(deviceState, "ifRunningTCPServer");
            }
            if (_listenerWrapper2 != null)
            {
                _listenerWrapper2.Listener.Stop();
                _listenerWrapper2 = null;
                LogMessage("TELNET 服务已停止", LogLevel.Information);
            }

        }

        public async Task StartServicesAsync()
        {
            if (!deviceState.ifRunningMqttServer && deviceList.ifStartMqttServer)
            {
                await _mqttService.StartMQTTService();
                LogMessage("MQTT服务 已启动", LogLevel.Information); LogMessage($"MQTT服务启动状态: {_mqttService.mqttServer.IsStarted},MQTT服务器参数:{_mqttService.mqttServer.ServerSessionItems}", LogLevel.Information);

                ChangeDeviceState(deviceState, "ifRunningMqttServer");
            }
            if (!_serialPort.IsOpen && deviceList.ifStartSerialPort1)
            {
                LogMessage("Starting UART service...", LogLevel.Information);
                try
                {
                    _serialPort = new SerialPort(serialPortSetting.PortName.ToString(), serialPortSetting.BaudRate, Parity.None, 8, StopBits.One);
                    _serialPort.Open();
                    _serialPort.DataReceived += SerialPortDataReceived;
                }
                catch (Exception e)
                {

                    //_logger.LogInformation(DateTime.Now.ToString()+":"+$"{e}");
                }

                LogMessage("UART服务 已启动", LogLevel.Information);
                ChangeDeviceState(deviceState, "ifRunningSerialPort1");
            }
            else
            {
                LogMessage("Closing UART service...", LogLevel.Information);
                _serialPort.Close();
                _serialPort.Dispose();
                LogMessage("UART 服务已关闭", LogLevel.Information);
                ChangeDeviceState(deviceState, "ifRunningSerialPort1");
                LogMessage("Starting UART service...", LogLevel.Information);
                try
                {
                    _serialPort = new SerialPort(serialPortSetting.PortName.ToString(), serialPortSetting.BaudRate, Parity.None, 8, StopBits.One);
                    _serialPort.Open();
                    _serialPort.DataReceived += SerialPortDataReceived;
                }
                catch (Exception e)
                {

                    //_logger.LogInformation(DateTime.Now.ToString()+":"+$"{e}");
                }

                LogMessage("UART服务 已启动", LogLevel.Information);
                ChangeDeviceState(deviceState, "ifRunningSerialPort1");
            }

            if (!_httpListener.IsListening && deviceList.ifStartWebSocketServer)
            {
                LogMessage("Starting Webscoket service...", LogLevel.Information);
                _cancellationTokenSource2 = new CancellationTokenSource();
                //_httpListener = new HttpListener();
                //_httpListener.Prefixes.Add("http://192.168.1.103:5005/");
                //_httpListener.Start();
                // //_logger.LogInformation(DateTime.Now.ToString()+":"+"WebSocket 服务器已启动");
                Task.Run(() => ListenWebSocket(_cancellationTokenSource2.Token));


                LogMessage("WebSocket 服务器已启动", LogLevel.Information);
                ChangeDeviceState(deviceState, "ifRunningWebSocketServer");
            }

            if (_listenerWrapper1 == null && deviceList.ifStartTCPServer)
            {

                LogMessage("Starting TCP service...", LogLevel.Information);
                _connectedTcpClients.Clear();
                //await Task.Factory.StartNew(() => ListenTcp(tcpServerSetting.TCPAddress,tcpServerSetting.TCPPort));
                //var tcpServer  =  new TcpServer();
                //tcpServer.StartServerAsync();
                LogMessage("TCP服务 已启动", LogLevel.Information);
                ChangeDeviceState(deviceState, "ifRunningTCPServer");
            }
            if (_listenerWrapper2 == null)
            {
                LogMessage("Starting Telnet service...", LogLevel.Information);
                _listenerWrapper2 = new TcpListenerWrapper();
                TelnetService.StartTelnetServerAsync(_listenerWrapper2, "127.0.0.1", 23);
                LogMessage("Telnet服务 已启动", LogLevel.Information);

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
                    LogMessage($"客户端 {_clientCount++} 已连接", LogLevel.Information);
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

                LogMessage($"{e}", LogLevel.Error);
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
                        var hex = BitConverter.ToString(buffer, 0, byteCount).Replace("-", "");
                        //_logger.LogInformation(DateTime.Now.ToString()+":"+$"收到来自客户端的消息：{hex}");

                        var jsonPacket = new
                        {
                            Address = client.Client.RemoteEndPoint.ToString(),
                            Message = hex
                        };

                        string jsonString = JsonConvert.SerializeObject(jsonPacket);

                        foreach (var item in _connectedClients.ToList()) // ToList() is used to avoid collection modification during enumeration
                        {
                            try
                            {
                                await item.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(jsonString)), WebSocketMessageType.Text, true, CancellationToken.None);
                                //_logger.LogInformation(DateTime.Now.ToString()+":"+$"发送消息到客户端: {jsonString}");
                            }
                            catch (Exception e)
                            {
                                //_logger.LogInformation(DateTime.Now.ToString()+":"+$"发送消息到客户端时出错：{e.Message}");
                                _connectedClients.Remove(item);
                            }
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
                //_logger.LogInformation(DateTime.Now.ToString()+":"+$"处理客户端 {_clientCount} 时出错：{e.Message}");
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
                //_logger.LogInformation(DateTime.Now.ToString()+":"+$"客户端 {_clientCount--} 已断开");
            }
        }





        private async Task ListenWebSocket(CancellationToken cancellationToken)
        {

            _httpListener = new HttpListener();
            //var address = $"{ipAddress}:{port}/";
            //httpListener.Prefixes.Add(address);
            _httpListener.Prefixes.Add("http://192.168.1.103:5005/");
            _httpListener.Start();
            while (!cancellationToken.IsCancellationRequested)
            {
                var context = await _httpListener.GetContextAsync();

                if (context.Request.IsWebSocketRequest)
                {

                    try
                    {
                        var webSocketContext = await context.AcceptWebSocketAsync(null);
                        LogMessage("WebSocket 连接已建立", LogLevel.Information);
                        await WebSocketConnectionHandler(webSocketContext.WebSocket);
                    }
                    catch (Exception e)
                    {
                        //_logger.LogInformation(DateTime.Now.ToString()+":"+"WebSocket 连接错误: " + e.Message);
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
            var buffer = new byte[1024];
            _connectedClients.Add(webSocket);
            //_logger.LogInformation(DateTime.Now.ToString()+":"+$"当前客户端连接数量: {_connectedClients.Count}");
            while (true)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                var num = result.Count;
                if (result != null)
                {
                    var jsonString = Encoding.UTF8.GetString(buffer, 0, num);
                    var jsonPacket = JsonConvert.DeserializeObject<dynamic>(jsonString);

                    var address = (string)jsonPacket.Address;
                    var messageHex = (string)jsonPacket.Message;

                    //_logger.LogInformation(DateTime.Now.ToString()+":"+$"收到来自 WebSocket 客户端{address}的消息: {messageHex}");


                    var messageBytes = StringToByteArray(messageHex);

                    var targetClient = _connectedTcpClients.FirstOrDefault(c => c._tcpClient.Client.RemoteEndPoint.ToString() == address);
                    if (targetClient != null)
                    {
                        var stream = targetClient._tcpClient.GetStream();
                        await stream.WriteAsync(messageBytes, 0, messageBytes.Length);
                    }
                    //_logger.LogInformation(DateTime.Now.ToString()+":"+$"发送到目标客户端 客户端{address}的报文: {messageHex}");

                    //// 处理从 WebSocket 客户端接收到的消息，例如将其发送到串口
                    //if (_serialPort != null && _serialPort.IsOpen && !string.IsNullOrEmpty(message))
                    //{
                    //    _serialPort.Write(message);
                    //}
                    //if (_listenerWrapper1.Listener != null)
                    //{
                    //    foreach (var item in _connectedTcpClients)
                    //    {
                    //        /*  tcpClient.Client.RemoteEndPoint as IPEndPoint：尝试将RemoteEndPoint属性转换为IPEndPoint类型。如果RemoteEndPoint为null或不能转换为IPEndPoint，as操作符将返回null。

                    //          (tcpClient.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString()：使用null条件访问操作符（?.），如果转换后的IPEndPoint对象不为null，我们将访问其Address属性并调用ToString()方法将IP地址转换为字符串。如果IPEndPoint对象为null，则整个表达式将返回null。

                    //          string remoteAddress = ... ?? string.Empty;：空合并操作符（??）用于处理上述表达式返回的null值。如果表达式结果为null，remoteAddress将被赋值为空字符串（string.Empty）。如果表达式结果不为null，remoteAddress将被赋值为IP地址字符串。

                    //          综上所述，这段代码的作用是尝试从tcpClient对象获取远程客户端的IP地址字符串，如果无法获取（例如，RemoteEndPoint为null），则将remoteAddress赋值为空字符串。*/
                    //        string remoteAddress = (item._tcpClient.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? string.Empty;

                    //        if (remoteAddress == "192.168.1.103")
                    //        {
                    //            try
                    //            {
                    //                var stream = item._tcpClient.GetStream();
                    //                var sendBuffer = Encoding.UTF8.GetBytes(message);
                    //                await stream.WriteAsync(sendBuffer, 0, sendBuffer.Length);
                    //                 //_logger.LogInformation(DateTime.Now.ToString()+":"+$"发送消息到 TCP 客户端: {message}");
                    //            }
                    //            catch (Exception e)
                    //            {
                    //                 //_logger.LogInformation(DateTime.Now.ToString()+":"+$"发送消息到 TCP 客户端出错：{e.Message}");
                    //            }
                    //        }

                    //    }
                    //}

                    //while (webSocket.State == WebSocketState.Open)
                    //{
                    //var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    //if (result.MessageType == WebSocketMessageType.Close)
                    //{
                    //    await webSocketContext.WebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                    //}
                    //else
                    //{
                    //    var jsonString = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    //    var jsonPacket = JsonConvert.DeserializeObject<dynamic>(jsonString);

                    //    var address = (string)jsonPacket.Address;
                    //    var messageHex = (string)jsonPacket.Message;


                    //}
                    //}



                }
                else if (result.MessageType == WebSocketMessageType.Close)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "关闭连接", CancellationToken.None);
                    LogMessage("WebSocket 连接已关闭", LogLevel.Information);
                    _connectedClients.Remove(webSocket);
                    //_logger.LogInformation(DateTime.Now.ToString()+":"+$"当前客户端连接数量: {_connectedClients.Count}");
                    break;
                }
            }
        }

        void SerialPortDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            SerialPort serialPort = (SerialPort)sender;
            string data = serialPort.ReadExisting();
            //_logger.LogInformation(DateTime.Now.ToString()+":"+$"从串口收到数据: {data}");
            byte[] buffer = Encoding.UTF8.GetBytes(data);

            foreach (var client in _connectedClients)
            {
                client.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);
                //_logger.LogInformation(DateTime.Now.ToString()+":"+$"发送消息到客户端: {data}");
            }
        }


        public void UpdateSerialPortSettings(string port, int baud)
        {
            if (_serialPort != null && _serialPort.IsOpen)
            {
                _serialPort.Close();
            }

            _serialPort.PortName = serialPortSetting.PortName ?? _serialPort.PortName;
            _serialPort.BaudRate = _serialPort.BaudRate;

            _serialPort.Open();
        }

        public async void UpdateTCPServerSettings(string address, int port)
        {
            tcpServerSetting.TCPPort = port;
            tcpServerSetting.TCPAddress = address;
            if (_listenerWrapper1 != null)
            {
                _listenerWrapper1.Listener.Stop();
                _listenerWrapper1 = null;
                LogMessage("TCP 服务已停止", LogLevel.Information);

                LogMessage("Starting TCP service...", LogLevel.Information);
                _listenerWrapper1 = new TcpListenerWrapper();
                await Task.Factory.StartNew(() => ListenTcp(tcpServerSetting.TCPAddress, tcpServerSetting.TCPPort));
                LogMessage("TCP服务 已启动", LogLevel.Information);
            }
        }

        private LogEntry GenerateLog(string message, LogLevel level)
        {
            LogEntry entry = new LogEntry();

            entry.Level = level;
            entry.Message = message;
            entry.Timestamp = DateTime.Now;

            return entry;
        }


        public void LogMessage(string message, LogLevel level)
        {
            //_logger.LogInformation(DateTime.Now.ToString()+":"+message);
            logQueue?.Enqueue(GenerateLog(message, level));
        }

        //private async Task ListenWebSocket(CancellationToken cancellationToken)
        //{
        //    while (!cancellationToken.IsCancellationRequested)
        //    {
        //        var context = await _httpListener.GetContextAsync();

        //        if (context.Request.IsWebSocketRequest)
        //        {
        //            WebSocketContext webSocketContext = null;

        //            try
        //            {
        //                webSocketContext = await context.AcceptWebSocketAsync(null);
        //                _connectedClients.Add(webSocketContext.WebSocket);
        //                 //_logger.LogInformation(DateTime.Now.ToString()+":"+"WebSocket 连接已建立");
        //                 //_logger.LogInformation(DateTime.Now.ToString()+":"+$"当前客户端连接数量: {_connectedClients.Count}");
        //                // Start a new task to handle this connection
        //                Task.Run(async () =>
        //                {
        //                    var buffer = new byte[1024];
        //                    while (webSocketContext.WebSocket.State == WebSocketState.Open)
        //                    {
        //                        var result = await webSocketContext.WebSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
        //                        if (result.MessageType == WebSocketMessageType.Close)
        //                        {
        //                            await webSocketContext.WebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
        //                        }
        //                        else
        //                        {


        //                            var jsonString = Encoding.UTF8.GetString(buffer, 0, result.Count);
        //                            var jsonPacket = JsonConvert.DeserializeObject<dynamic>(jsonString);
        //                            var address = (string)jsonPacket.Address;
        //                            var messageHex = (string)jsonPacket.Message;
        //                             //_logger.LogInformation(DateTime.Now.ToString()+":"+$"收到后台下发: {messageHex}");
        //                             //_logger.LogInformation(DateTime.Now.ToString()+":"+$"目标地址: {address}");

        //                            var messageBytes = StringToByteArray(messageHex);

        //                            var targetClient = _connectedTcpClients.FirstOrDefault(c => c._tcpClient.Client.RemoteEndPoint.ToString() == address);
        //                            if (targetClient != null)
        //                            {
        //                                var stream = targetClient._tcpClient.GetStream();
        //                                await stream.WriteAsync(messageBytes, 0, messageBytes.Length);
        //                            }
        //                        }
        //                    }
        //                });
        //            }
        //            catch (Exception e)
        //            {
        //                 //_logger.LogInformation(DateTime.Now.ToString()+":"+"WebSocket 连接错误: " + e.Message);
        //            }
        //        }
        //        else
        //        {
        //            context.Response.StatusCode = 400;
        //            context.Response.Close();
        //        }
        //    }
        //}

        public static byte[] StringToByteArray(string hex)
        {
            return Enumerable.Range(0, hex.Length)
                             .Where(x => x % 2 == 0)
                             .Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
                             .ToArray();
        }

        public async Task StartPingTaskAsync()
        {
            // 创建一个新的 Ping 对象
            var ping = new System.Net.NetworkInformation.Ping();
            // 使用数据库帮助类来执行数据库操作
            var dbHelper = new DatabaseHelper();
            while (true)
            {
                try
                {

                    // 查询所有设备的 IP 端点
                    DataTable dataTable = new DataTable();

                    var query = "SELECT imei, ipEndPoint FROM fct_device_code";
                    dataTable = dbHelper.ExecuteQuery(query);



                    var deviceList = new List<(string Imei, string IpEndPoint)>();
                    foreach (DataRow row in dataTable.Rows)
                    {
                        deviceList.Add((row["imei"].ToString(), row["ipEndPoint"].ToString()));
                    }

                    foreach (var device in deviceList)
                    {
                        // 解析设备的 IP 地址和端口
                        var ipEndPoint = IPEndPoint.Parse(device.IpEndPoint);

                        try
                        {
                            // 发送 ICMP echo 请求
                            var reply = await ping.SendPingAsync(ipEndPoint.Address);

                            // 根据响应来判断设备是否在线
                            var onlineState = reply.Status == System.Net.NetworkInformation.IPStatus.Success ? "1" : "0";
                            if (onlineState == "0")
                            {
                                // 更新设备的在线状态

                                query = $"UPDATE fct_device_code SET onlineState = '{onlineState}' WHERE imei = '{device.Imei}'";
                                dbHelper.ExecuteNonQuery(query);
                            }


                        }
                        catch (Exception ex)
                        {
                            //_logger.LogInformation(DateTime.Now.ToString()+":"+$"Error pinging device {device.Imei}: {ex.Message}");
                        }
                    }
                }
                catch (Exception e)
                {
                    //_logger.LogInformation(DateTime.Now.ToString()+":"+e);
                    throw;
                }


                // 等待一段时间再进行下一轮的 ping
                await Task.Delay(TimeSpan.FromMinutes(1));
            }
        }



        public static void SetTableName(string value)
        {
            XmlDocument doc = new XmlDocument();
            doc.Load("databseConfig.xml"); // Load the XML file

            XmlNode node = doc.SelectSingleNode("/configuration/connectionStrings/table"); // Select the XML node

            if (node != null)
            {
                XmlAttribute attribute = node.Attributes["tableName"]; // Get the tableName attribute

                if (attribute != null)
                {
                    attribute.Value = value; // Set the tableName attribute
                    doc.Save("databseConfig.xml"); // Save the changes
                }
            }
        }


        private string GetNowRecordTableName()
        {
            string tableName;
            XmlDocument doc = new XmlDocument();
            doc.Load("databseConfig.xml"); // 加载 XML 文件

            XmlNode node = doc.SelectSingleNode("/configuration/connectionStrings/table"); // 选择 XML 节点

            if (node != null)
            {
                XmlAttribute attribute = node.Attributes["tableName"]; // 获取连接字符串属性

                if (attribute != null)
                {
                    tableName = attribute.Value; // 获取连接字符串
                    return tableName;
                }
                else
                {
                    return null;
                }
            }
            else
            {
                return null;
            }


        }



    }
}



