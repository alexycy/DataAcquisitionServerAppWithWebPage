using DataAcquisitionServerApp;
using DataAcquisitionServerAppWithWebPage.Utilities;
using Newtonsoft.Json;
using SuperSocket;
using SuperSocket.Server;
using SuperSocket.WebSocket;
using SuperSocket.WebSocket.Server;
using System.Text;
using System.Text.Json;
using static DataAcquisitionServerAppWithWebPage.Service.WebSocketServer;

namespace DataAcquisitionServerAppWithWebPage.Service
{


    public class WebSocketServer
    {
        private readonly TcpServer _tcpServer;
        public class JsonPackage
        {
            public string Address { get; set; }
            public string Message { get; set; }
        }
        public WebSocketServer(TcpServer tcpServer)
        {
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
                    Console.WriteLine($"{message.Message}");

                    // 在这里处理接收到的消息
                    var receivedText = message.Message;

                        JsonPackage jsonPackage = JsonConvert.DeserializeObject<JsonPackage>(receivedText);

// 
                        Console.WriteLine($"{jsonPackage.Message}");
                        Console.WriteLine($"{jsonPackage.Address}");

                        // 获取 TcpServer 的所有会话
                        var targetSession = _tcpServer.sessions.FirstOrDefault(s => s.RemoteEndPoint.ToString() == jsonPackage.Address);

                        if (targetSession != null)
                        {
                            var messageBytes = ConvertMethod.StringToByteArray(jsonPackage.Message);
                            await targetSession.SendAsync(messageBytes);
                            Console.WriteLine(messageBytes);
                        }
                        else
                        {
                            Console.WriteLine("找不到目标地址");
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
