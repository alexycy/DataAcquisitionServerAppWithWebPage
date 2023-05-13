using MQTTnet.Server;
using MQTTnet;
using System.Net;
using MQTTnet.Internal;
using MQTTnet.Packets;
using MQTTnet.Protocol;

public class MqttService
{
    public MqttServer mqttServer;
    private MqttServerOptions mqttServerOptions;
    public bool ifValidating = false;
    public async Task StartMQTTService()
    {
        var mqttFactory = new MqttFactory();

        ifValidating = true;
       mqttServerOptions = new MqttServerOptionsBuilder()
            //WithDefaultEndpointPort方法是用来设置默认端口的端口号的，但是这个设置只有在默认端口被启用的时候才会生效。如果默认端口没有被启用，那么这个设置就不会有任何效果。
            .WithDefaultEndpoint()
            .WithDefaultEndpointPort(1885)
            //.WithDefaultEndpointBoundIPAddress(IPAddress.Parse("192.168.1.103"))
            .Build();

        mqttServer = mqttFactory.CreateMqttServer(mqttServerOptions);

        if (ifValidating)
        {
            mqttServer.ValidatingConnectionAsync += e =>
            {
                //if (e.ClientId != "ValidClientId")
                //{
                //    e.ReasonCode = MqttConnectReasonCode.ClientIdentifierNotValid;
                //}

                if (e.UserName != "admin")
                {
                    e.ReasonCode = MqttConnectReasonCode.BadUserNameOrPassword;
                }

                if (e.Password != "admin")
                {
                    e.ReasonCode = MqttConnectReasonCode.BadUserNameOrPassword;
                }

                Console.WriteLine($"{e.ReasonCode}");

                return Task.CompletedTask;
            };
        }
        mqttServer.ClientConnectedAsync += e =>
        {
            Console.WriteLine($"Client {e.ClientId} connected at {DateTime.UtcNow} with protocol version {e.ProtocolVersion}");
            return Task.CompletedTask;
        };

        //mqttServer.InterceptingPublishAsync += e =>
        //{
        //    Console.WriteLine($"Client {e.ClientId} published message to topic {e.ApplicationMessage.Topic} at {DateTime.UtcNow}");
        //    return Task.CompletedTask;
        //};

        // 附加客户端订阅主题事件处理器
        mqttServer.ClientSubscribedTopicAsync += e =>
        {
            Console.WriteLine($"Client '{e.ClientId}' subscribed to topic '{e.TopicFilter.Topic}' with QoS level '{e.TopicFilter.QualityOfServiceLevel}'");
            return Task.CompletedTask;
        };

        //服务启动事件
        //mqttServer.StartedHandler = new MqttServerStartedHandlerDelegate((Action<EventArgs>)StartedHandler);
        ////服务停止
        //mqttServer.StoppedHandler = new MqttServerStoppedHandlerDelegate((Action<EventArgs>)StoppedHandler);
        ////客户端连接事件
        //mqttServer.ClientConnectedHandler = new MqttServerClientConnectedHandlerDelegate((Action<MqttServerClientConnectedEventArgs>)ClientConnectedHandler);
        ////客户端断开连接事件
        //mqttServer.ClientDisconnectedHandler = new MqttServerClientDisconnectedHandlerDelegate((Action<MqttServerClientDisconnectedEventArgs>)ClientDisconnectedHandler);
        ////消息监听
        //mqttServer.ApplicationMessageReceivedHandler = new MqttApplicationMessageReceivedHandlerDelegate((Action<MqttApplicationMessageReceivedEventArgs>)MessageReceivedHandler);
        ////客户端订阅主题事件
        //mqttServer.ClientSubscribedTopicHandler = new MqttServerClientSubscribedHandlerDelegate((Action<MqttServerClientSubscribedTopicEventArgs>)ClientSubscribedTopicHandler);
        ////客户端取消订阅主题事件
        //mqttServer.ClientUnsubscribedTopicHandler = new MqttServerClientUnsubscribedTopicHandlerDelegate((Action<MqttServerClientUnsubscribedTopicEventArgs>)ClientUnsubscribedTopicHandler);

        await mqttServer.StartAsync();

       
        Console.WriteLine($"MQTT log server printed: {mqttFactory.DefaultLogger.ToString()}");
        Console.WriteLine($"MQTT server started: {mqttServer.IsStarted}");
        Console.WriteLine($"Endpoint: {mqttServerOptions.DefaultEndpointOptions.BoundInterNetworkAddress}");
        Console.WriteLine($"Endpoint port: {mqttServerOptions.DefaultEndpointOptions.Port}");
    }

    public async Task StopMQTTService()
    {
        await mqttServer.StopAsync();
    }
}
