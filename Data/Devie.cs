using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Xml.Linq;
namespace DataAcquisitionServerAppWithWebPage.Data
{
    public class Device
    {
        public string Type { get; set; }
        public DeviceSettingsBase Settings { get; set; }
        public bool IsRegistered { get; set; }

        public DeviceType GetDeviceType()
        {
            return Enum.Parse<DeviceType>(Type);
        }

        public void SetDeviceSettings(DeviceType deviceType)
        {
            switch (deviceType)
            {
                case DeviceType.SerialPort:
                    Settings = new SerialPortSettings();
                    break;
                case DeviceType.TcpListener:
                    Settings = new TcpListenerSettings();
                    break;
                case DeviceType.HttpListener:
                    Settings = new HttpListenerSetting();
                    break;
                case DeviceType.MqttServer:
                    Settings = new MqttServerSetting();
                    break;
                // 添加其他设备类型的配置参数类
                default:
                    throw new ArgumentOutOfRangeException(nameof(deviceType), deviceType, "Invalid device type.");
            }
        }
    }

    public abstract class DeviceSettingsBase
    {
       public String IPAddress { get; set; }
       public int Port { get; set; }
        // 添加通用的设备配置参数
    }

    public class SerialPortSettings : DeviceSettingsBase
    {
        public string PortName { get; set; }
        public int BaudRate { get; set; }
        // 添加串口设备特定的配置参数
    }

    public class TcpListenerSettings : DeviceSettingsBase
    {

    }

    public class HttpListenerSetting : DeviceSettingsBase
    {

        // 添加HTTP设备特定的配置参数
    }

    public class MqttServerSetting : DeviceSettingsBase
    {

        // 添加MQTTServer设备特定的配置参数
    }

    public enum DeviceType
    {
        SerialPort,
        TcpListener,
        HttpListener,
        MqttServer
        // 添加其他设备类型...
    }

    public class DeviceManager
    {
        private List<Device> _devices;

        public DeviceManager()
        {
            _devices = new List<Device>();
        }

        public void LoadDevicesFromXml(string filename)
        {
            if (!File.Exists(filename))
            {
                throw new FileNotFoundException($"XML file '{filename}' not found.");
            }

            XDocument doc = XDocument.Load(filename);
            _devices = doc.Root.Elements("Device")
                .Select(e => new Device
                {
                    Type = e.Element("Type")?.Value,
                    IsRegistered = bool.Parse(e.Element("IsRegistered")?.Value),
                    Settings = CreateDeviceSettings(e.Element("Type")?.Value, e.Element("Settings"))
                })
                .ToList();
        }

        public void SaveDevicesToXml(string filename)
        {
            XDocument doc = new XDocument(
                new XElement("Devices",
                    _devices.Select(d =>
                        new XElement("Device",
                            new XElement("Type", d.Type),
                            new XElement("IsRegistered", d.IsRegistered),
                            SerializeDeviceSettings(d.Settings)
                        )
                    )
                )
            );

            doc.Save(filename);
        }

        private DeviceSettingsBase CreateDeviceSettings(string deviceType, XElement settingsElement)
        {
            DeviceType type = Enum.Parse<DeviceType>(deviceType);

            DeviceSettingsBase settings = null;
            switch (type)
            {
                case DeviceType.SerialPort:
                    settings = new SerialPortSettings();
                    break;
                case DeviceType.TcpListener:
                    settings = new TcpListenerSettings();
                    break;
                // 添加其他设备类型的配置参数类
                case DeviceType.HttpListener:
                    settings = new HttpListenerSetting();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(deviceType), deviceType, "Invalid device type.");
            }

            if (settingsElement != null)
            {
                // 解析配置参数
                // 根据具体的配置参数类进行设置
                switch (settings)
                {
                    case SerialPortSettings serialPortSettings:
                        serialPortSettings.PortName = settingsElement.Element("PortName")?.Value;
                        serialPortSettings.BaudRate = int.Parse(settingsElement.Element("BaudRate")?.Value);
                        break;
                    case TcpListenerSettings tcpListenerSettings:
                        tcpListenerSettings.IPAddress = settingsElement.Element("TcpAddress")?.Value;
                        tcpListenerSettings.Port = int.Parse(settingsElement.Element("TcpPort")?.Value);
                        break;
                    case HttpListenerSetting httpListenerSetting:
                        httpListenerSetting.IPAddress = settingsElement.Element("TcpAddress")?.Value;
                        httpListenerSetting.Port = int.Parse(settingsElement.Element("TcpPort")?.Value);
                        break;
                    case MqttServerSetting mqttServerSetting:
                        mqttServerSetting.IPAddress = settingsElement.Element("TcpAddress")?.Value;
                        mqttServerSetting.Port = int.Parse(settingsElement.Element("TcpPort")?.Value);
                        break;
                        // 添加其他设备类型的配置参数解析
                }
            }

            return settings;
        }

        private XElement SerializeDeviceSettings(DeviceSettingsBase settings)
        {
            if (settings == null)
            {
                return null;
            }

            XElement settingsElement = new XElement("Settings");

            // 根据具体的配置参数类进行序列化
            switch (settings)
            {
                case SerialPortSettings serialPortSettings:
                    settingsElement.Add(
                        new XElement("PortName", serialPortSettings.PortName),
                        new XElement("BaudRate", serialPortSettings.BaudRate)
                    );
                    break;
                case TcpListenerSettings tcpListenerSettings:
                    settingsElement.Add(
                        new XElement("TcpAddress", tcpListenerSettings.IPAddress),
                        new XElement("TcpPort", tcpListenerSettings.Port)
                    );
                    break;
                case HttpListenerSetting  httpListenerSetting:
                    settingsElement.Add(
                        new XElement("HttpAddress", httpListenerSetting.IPAddress),
                        new XElement("HttpPort", httpListenerSetting.Port)
                    );
                    break;
                case MqttServerSetting mqttServerSetting:
                    settingsElement.Add(
                        new XElement("MqttAddress", mqttServerSetting.IPAddress),
                        new XElement("MqttPort", mqttServerSetting.Port)
                    );
                    break;
                    // 添加其他设备类型的配置参数序列化
            }

            return settingsElement;
        }
    }

}
