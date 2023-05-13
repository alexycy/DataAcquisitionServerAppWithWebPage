using MQTTnet;
using MQTTnet.Internal;
using MQTTnet.Packets;
using MQTTnet.Server;
using System.Net;

namespace DataAcquisitionServerAppWithWebPage.Service
{
    public  class MqttService
    {
        public  MqttServer mqttServer;
        public  async Task StartMQTTService()
        {
            /*
             * This sample starts a MQTT server and attaches an event which gets fired whenever
             * a client received and acknowledged a PUBLISH packet.
             */

            var mqttFactory = new MqttFactory();
            var mqttServerOptions = new MqttServerOptionsBuilder()
                .WithDefaultEndpointPort(1889)
                .WithDefaultEndpointBoundIPAddress(IPAddress.Parse("127.0.0.1"))
                .Build();

            using (mqttServer = mqttFactory.CreateMqttServer(mqttServerOptions))
            {
                foreach (var item in mqttServer.ServerSessionItems.Keys)
                {
                    Console.WriteLine($"{item}");
                } 
                // Attach the event handler.
                mqttServer.ClientAcknowledgedPublishPacketAsync += e =>
                {
                    Console.WriteLine($"Client '{e.ClientId}' acknowledged packet {e.PublishPacket.PacketIdentifier} with topic '{e.PublishPacket.Topic}'");

                    // It is also possible to read additional data from the client response. This requires casting the response packet.
                    var qos1AcknowledgePacket = e.AcknowledgePacket as MqttPubAckPacket;
                    Console.WriteLine($"QoS 1 reason code: {qos1AcknowledgePacket?.ReasonCode}");

                    var qos2AcknowledgePacket = e.AcknowledgePacket as MqttPubCompPacket;
                    Console.WriteLine($"QoS 2 reason code: {qos1AcknowledgePacket?.ReasonCode}");
                    return CompletedTask.Instance;
                };

                // 附加客户端订阅主题事件处理器
                mqttServer.ClientSubscribedTopicAsync += e =>
                {
                    Console.WriteLine($"Client '{e.ClientId}' subscribed to topic '{e.TopicFilter.Topic}' with QoS level '{e.TopicFilter.QualityOfServiceLevel}'");
                    return Task.CompletedTask;
                };

                await mqttServer.StartAsync();

            }
        }

        public  async Task StopMQTTService()
        {
            await mqttServer.StopAsync();
        }
    }
}
