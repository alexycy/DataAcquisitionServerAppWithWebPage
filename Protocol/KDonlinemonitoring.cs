using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace DataAcquisitionServerAppWithWebPage.Protocol
{
    public class KDonlinemonitoring
    {
        public class ParsedData
        {
            public string SoftwareVersion { get; set; }
            public string WorkMode { get; set; }
            public SamplingData SamplingData { get; set; }
            public DateTime TimeSync { get; set; }
            public float DCBiasCurrentMeasurementResult { get; set; }
            public int IndexNum { get; set; }
            public DateTime DataCollectionTime { get; set; }


        }


        public class DataFieldPackage
        {
            public byte DataVersion { get; set; }
            public byte Reserved { get; set; }
            public ushort DeviceType { get; set; }
            public ushort ControlWord { get; set; }
            public byte[] DeviceNumber { get; set; }
            public byte[] DataSendTime { get; set; }
            public byte[] CustomData { get; set; }
        }

        public class DataFieldParser
        {
            public DataFieldPackage Parse(byte[] data)
            {
                var package = new DataFieldPackage();

                int index = 0;

                package.DataVersion = data[index++];
                package.Reserved = data[index++];
                package.DeviceType = BinaryPrimitives.ReverseEndianness(BitConverter.ToUInt16(data, index)); index += 2;
                package.ControlWord = BinaryPrimitives.ReverseEndianness(BitConverter.ToUInt16(data, index)); index += 2;
                package.DeviceNumber = data.Skip(index).Take(6).ToArray(); index += 6;
                package.DataSendTime = data.Skip(index).Take(8).ToArray(); index += 8;
                package.CustomData = data.Skip(index).ToArray();




                return package;
            }
        }
        public class ProtocolPacker
        {
            public byte[] Pack(DataFieldPackage package)
            {
                List<byte> data = new List<byte>();

                data.Add(package.DataVersion);
                data.Add(package.Reserved);
                data.AddRange(BitConverter.GetBytes(package.DeviceType));
                data.AddRange(BitConverter.GetBytes(package.ControlWord));
                data.AddRange(package.DeviceNumber);
                data.AddRange(package.DataSendTime);
                data.AddRange(package.CustomData);

                return data.ToArray();
            }
        }



        public class ProtocolPackage
        {
            public byte[] StartCode { get; set; } // 起始码
            public byte[] ProtocolVersion { get; set; } // 协议版本号
            public byte[] ReservedField { get; set; } // 保留字段
            public UInt16 DataLength { get; set; } // 数据域长度
            public byte[] Data { get; set; } // 数据域
            public byte[] CheckCode { get; set; } // 校验码
            public byte[] EndCode { get; set; } // 结束码
        }

        public abstract class CustomDataParser
        {
            public abstract ParsedData ParseData(byte[] data);

            protected byte[] ParseHexString(string hexString)
            {
                int NumberChars = hexString.Length;
                byte[] bytes = new byte[NumberChars / 2];
                for (int i = 0; i < NumberChars; i += 2)
                    bytes[i / 2] = Convert.ToByte(hexString.Substring(i, 2), 16);
                return bytes;
            }
        }

        public class SoftwareVersionParser : CustomDataParser
        {
            public override ParsedData ParseData(byte[] data)
            {
                string version = Encoding.ASCII.GetString(data);
                //Console.WriteLine("Software Version: " + version);
                return new ParsedData { SoftwareVersion = version };
            }
        }

        public class WorkModeParser : CustomDataParser
        {
            public override ParsedData ParseData(byte[] data)
            {
                string mode = data[0] == 0x01 ? "Continuous Work" : "Intermittent Work";
                //Console.WriteLine("Work Mode: " + mode);
                return new ParsedData { WorkMode = mode };
            }
        }

        public class SamplingDataParser : CustomDataParser
        {
            public override ParsedData ParseData(byte[] data)
            {
                SamplingData samplingData = new SamplingData
                {

                };
                samplingData.Frequency = data[0];
                byte[] reversedCurrentBytes = data.Skip(1).Take(4).Reverse().ToArray();
                samplingData.BatteryVoltage = BitConverter.ToSingle(reversedCurrentBytes, 0);
                reversedCurrentBytes = data.Skip(5).Take(4).Reverse().ToArray();
                samplingData.Temperature = BitConverter.ToSingle(reversedCurrentBytes, 0);
                reversedCurrentBytes = data.Skip(9).Take(2).Reverse().ToArray();
                samplingData.SDUsedSpace = BitConverter.ToUInt16(reversedCurrentBytes, 0);
                reversedCurrentBytes = data.Skip(11).Take(2).Reverse().ToArray();
                samplingData.SDTotalSpace = BitConverter.ToUInt16(reversedCurrentBytes, 0);
                reversedCurrentBytes = data.Skip(13).Take(4).Reverse().ToArray();
                samplingData.WorkingDuration = BitConverter.ToUInt32(reversedCurrentBytes, 0);
                reversedCurrentBytes = data.Skip(17).Take(1).Reverse().ToArray();

                if (BitConverter.ToBoolean(reversedCurrentBytes, 0))
                {
                    samplingData.SamplingState = 1;
                }
                else
                {
                    samplingData.SamplingState = 0;
                }

                //Console.WriteLine("Sampling DataGlobal:");
                //Console.WriteLine("Frequency: " + samplingData.Frequency);
                //Console.WriteLine("Battery Voltage: " + samplingData.BatteryVoltage);
                //Console.WriteLine("Temperature: " + samplingData.Temperature);
                //Console.WriteLine("SD Used Space: " + samplingData.SDUsedSpace);
                //Console.WriteLine("SD Total Space: " + samplingData.SDTotalSpace);
                //Console.WriteLine("Working Duration: " + samplingData.WorkingDuration);

                return new ParsedData { SamplingData = samplingData };


            }
        }

        public class TimeSyncParser : CustomDataParser
        {
            public override ParsedData ParseData(byte[] data)
            {
                int year = data[0] + 2000;
                int month = data[1];
                int day = data[2];
                int hour = data[3];
                int minute = data[4];
                int second = data[5];
                int millisecond = BitConverter.ToUInt16(data, 6);

                DateTime timestamp = new DateTime(year, month, day, hour, minute, second, millisecond);
                //Console.WriteLine("Time Sync: " + timestamp);
                return new ParsedData { TimeSync = timestamp };
            }
        }



        public class DCBiasCurrentMeasurementResultParser : CustomDataParser
        {
            public override ParsedData ParseData(byte[] data)
            {
                int year = data[0] + 2000;
                int month = data[1];
                int day = data[2];
                int hour = data[3];
                int minute = data[4];
                int second = data[5];
                int millisecond = BinaryPrimitives.ReverseEndianness(BitConverter.ToUInt16(data, 6));
                byte[] reversedCurrentBytes = data.Skip(8).Take(4).Reverse().ToArray();
                float current = BitConverter.ToSingle(reversedCurrentBytes, 0);
                int indexNum = data[12];
                DateTime timestamp = new DateTime(year, month, day, hour, minute, second, millisecond);
                //Console.WriteLine("DataGlobal Collection Time: " + timestamp);
                //Console.WriteLine("DC Bias Current Measurement Result: " + current);

                return new ParsedData { DataCollectionTime = timestamp, DCBiasCurrentMeasurementResult = current, IndexNum = indexNum };


            }
        }


        public class SamplingFrequencyAndWorkModeParser : CustomDataParser
        {
            public override ParsedData ParseData(byte[] data)
            {
                // TODO: Implement the parsing of sampling frequency and work mode data
                return new ParsedData { };
            }
        }

        public class HeartBeatParser : CustomDataParser
        {
            public override ParsedData ParseData(byte[] data)
            {
                // TODO: Implement the parsing of sampling frequency and work mode data
                var package = new DataFieldPackage();

                return new ParsedData { };
            }
        }

        public class DeviceResetParser : CustomDataParser
        {
            public override ParsedData ParseData(byte[] data)
            {
                var package = new DataFieldPackage();
                // TODO: Implement the parsing of device reset data
                return new ParsedData { };
            }
        }


        public class UnDeal : CustomDataParser
        {
            public override ParsedData ParseData(byte[] data)
            {
                // TODO: Implement the parsing of device reset data
                var package = new DataFieldPackage();
                return new ParsedData { };
            }
        }

        public class SamplingData
        {
            public byte Frequency { get; set; }
            public float BatteryVoltage { get; set; }
            public float Temperature { get; set; }
            public ushort SDUsedSpace { get; set; }
            public ushort SDTotalSpace { get; set; }
            public uint WorkingDuration { get; set; }
            public int SamplingState { get; set; }
        }






    }
}
