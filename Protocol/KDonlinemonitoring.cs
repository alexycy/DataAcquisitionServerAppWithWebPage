using SuperSocket.ProtoBase;
using System.Buffers;

namespace DataAcquisitionServerAppWithWebPage.Protocol
{
    public class KDonlinemonitoring
    {

        public class MyCustomProtocolPackage
        {
            public byte[] StartCode { get; set; } // 起始码
            public byte[] ProtocolVersion { get; set; } // 协议版本号
            public byte[] ReservedField { get; set; } // 保留字段
            public ushort DataLength { get; set; } // 数据域长度
            public byte[] Data { get; set; } // 数据域
            public byte[] CheckCode { get; set; } // 校验码
            public byte[] EndCode { get; set; } // 结束码
        }


    }
}
