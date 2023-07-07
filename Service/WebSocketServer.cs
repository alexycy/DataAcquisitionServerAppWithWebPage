using DataAcquisitionServerAppWithWebPage.Utilities;
using Newtonsoft.Json;
using SuperSocket;
using SuperSocket.WebSocket.Server;

namespace DataAcquisitionServerAppWithWebPage.Service
{


    public class WebSocketServer
    {
        private readonly TcpServer _tcpServer;
        //private static ILogger _logger;
        public class JsonPackage
        {
            public string Address { get; set; }
            public string Message { get; set; }
        }
        public WebSocketServer(TcpServer tcpServer/*,ILogger<WebSocketServer> logger*/)
        {
            ////_logger. = logger;   
            _tcpServer = tcpServer;
        }
        public async void StartServerAsync()
        {
            var server = WebSocketHostBuilder.Create()
                .ConfigureSuperSocket(options =>
                {
                    options.Name = "WebSocket Server";
                    options.AddListener(new ListenOptions
                    {
                        Ip = "Any",
                        Port = Convert.ToInt16(ConfigurationHelper.GetPortValue("webServiceConfig.xml", "webSocketPort", "add", "port"))
                    });
                })
                .UseWebSocketMessageHandler(async (session, message) =>
                {
                    //_logger..LogInformation(DateTime.Now.ToString()+$"{message.Message}");

                    // 在这里处理接收到的消息
                    var receivedText = message.Message;

                    JsonPackage jsonPackage = JsonConvert.DeserializeObject<JsonPackage>(receivedText);

                    // 
                    //_logger..LogInformation(DateTime.Now.ToString()+$"{jsonPackage.Message}");
                    //_logger..LogInformation(DateTime.Now.ToString()+$"{jsonPackage.Address}");

                    // 获取 TcpServer 的所有会话
                    var targetSession = _tcpServer.sessions.FirstOrDefault(s => s.RemoteEndPoint.ToString() == jsonPackage.Address);

                    if (targetSession != null)
                    {
                        var messageBytes = ConvertMethod.StringToByteArray(jsonPackage.Message);
                        await targetSession.SendAsync(messageBytes);
                        //_logger..LogInformation(DateTime.Now.ToString()+messageBytes);
                    }
                    else
                    {
                        //_logger..LogInformation(DateTime.Now.ToString()+"找不到目标地址");
                        return;
                    }

                })
                .UsePerMessageCompression()
                .ConfigureLogging((hostCtx, loggingBuilder) =>
                {
                    loggingBuilder.AddConsole();
                })
                .Build();

            await server.RunAsync();
        }

    }



}
