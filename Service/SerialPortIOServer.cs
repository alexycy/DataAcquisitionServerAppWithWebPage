using SuperSocket;
using SuperSocket.ProtoBase;
using SuperSocket.SerialIO;
using System.IO.Ports;
using System.Text;

namespace DataAcquisitionServerAppWithWebPage.Service
{
    public class JsonPackage
    {
        public string Address { get; set; }
        public string Message { get; set; }
    }
    public class SerialPortIOServer
    {


        public async void StartSerialPortIOServer()
        {
            var host = SuperSocketHostBuilder
                .Create<StringPackageInfo, CommandLinePipelineFilter>()
                .UsePackageHandler(async (s, package) =>
                {
                    // 将接收到的字符串消息反序列化为JsonPackage对象
                    var jsonPackage = System.Text.Json.JsonSerializer.Deserialize<JsonPackage>(package.Body);

                    // 提取Message和Address
                    var message = jsonPackage.Message;
                    var address = jsonPackage.Address;

                    // 打印Message和Address
                    Console.WriteLine($"Message: {message}, Address: {address}");

                    // 你可以根据你的需求处理这个消息
                    // 例如，你可以将消息发送回客户端：
                    var response = System.Text.Json.JsonSerializer.Serialize(jsonPackage);
                    await s.SendAsync(Encoding.UTF8.GetBytes(response + "\r\n"));
                })
                .ConfigureSuperSocket(options =>
                {
                    options.Name = "SIOServer";
                    options.AddListener(new SerialIOListenOptions
                    {
                        PortName = "COM1",
                        BaudRate = 9600,
                        Parity = Parity.None,
                        StopBits = StopBits.One,
                        Databits = 8
                    });
                })
                .UseSerialIO()
                .ConfigureLogging((hostCtx, loggingBuilder) =>
                {
                    loggingBuilder.AddConsole();
                })
                .Build();

            await host.RunAsync();


        }
    }






}
