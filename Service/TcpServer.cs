using DataAcquisitionServerApp;
using DataAcquisitionServerAppWithWebPage.Data;
using DataAcquisitionServerAppWithWebPage.Protocol;
using DataAcquisitionServerAppWithWebPage.Utilities;
using Org.BouncyCastle.Ocsp;
using Org.BouncyCastle.Utilities;
using SuperSocket;
using SuperSocket.ProtoBase;
using SuperSocket.Server;
using System.Buffers;
using System.Buffers.Binary;
using System.Data;
using System.Net;
using System.Text;
using System.Xml;
using DataAcquisitionServerAppWithWebPage.Data;
using static DataAcquisitionServerAppWithWebPage.Protocol.KDonlinemonitoring;

namespace DataAcquisitionServerAppWithWebPage.Service
{
    public class TcpServer
    {

        public List<IAppSession> sessions = new List<IAppSession>();


        public class MyCustomProtocolFilter : PipelineFilterBase<ProtocolPackage>
        {
            private int headerLength = 8; // Initial header length
            private int startCodePosition = -1; // Position of the start code



            public override ProtocolPackage Filter(ref SequenceReader<byte> reader)
            {
                // Look for the start code in the current buffer
                SequencePosition position = reader.Position;
                bool startCodeFound = false;
                bool endCodeFound = false;
                int startCodePosition = 0;
                int endCodePosition = 0;
                ushort dataLength = 0;

                try
                {
                    int loopCount = 0;
                    while (reader.TryRead(out byte b))
                    {
                        loopCount++;
                        if (!startCodeFound && b == 0xA5) // First byte of the start code
                        {
                            if (reader.TryRead(out byte nextB) && nextB == 0x5A) // Second byte of the start code
                            {
                                startCodePosition = (int)reader.Consumed - 2; // Update the position of the start code
                                startCodeFound = true;

                                // Skip the next 4 bytes (protocol version and reserved field)
                                try
                                {
                                    reader.Advance(4);
                                }
                                catch (Exception)
                                {

                                    continue;
                                } 

                                // Read the data length
                                if (!reader.TryReadBigEndian(out  dataLength))
                                {
                                    continue; // If out of bounds, continue to the next loop
                                }

                                // Skip the data field
                                try
                                {
                                    reader.Advance(dataLength+2-1);
                                }
                                catch (Exception)
                                {

                                    continue;
                                }
                            }
                        }
                        else if (startCodeFound && !endCodeFound)
                        {
                            if (!reader.TryRead(out byte endFirstByte)) // Try to read the first byte of the end code
                            {
                                continue; // If out of bounds, continue to the next loop
                            }
                            if (endFirstByte == 0x5A) // Check if the first byte of the end code is correct
                            {
                                if (!reader.TryRead(out byte endSecondByte)) // Try to read the second byte of the end code
                                {
                                    continue; // If out of bounds, continue to the next loop
                                }
                                if (endSecondByte == 0xA5) // Check if the second byte of the end code is correct
                                {
                                    endCodePosition = (int)reader.Consumed - 2; // Update the position of the end code
                                    endCodeFound = true;
                                    // Reset the reader to the start code position
                                    reader.Rewind((int)reader.Consumed);
                                    reader.Advance(startCodePosition);

                                    break;
                                }
                            }
                        }

                        if (loopCount >= 100)
                        {
                            Thread.Sleep(10);
                            continue;
                        }
                    }

                    //if (reader.Sequence.Length>255*2)
                    //{
                    //    return null;
                    //}
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    Reset();
                }

                

                // Now you can parse the package with the updated header length
                if (startCodeFound&& endCodeFound)
                {
                    var package = new ProtocolPackage();
                    try
                    {
                        // read start code
                        if (reader.TryRead(out byte firstByte) && reader.TryRead(out byte secondByte))
                        {
                            package.StartCode = new byte[] { firstByte, secondByte };
                            //if (package.StartCode[0] != 0xA5 && package.StartCode[1] != 0x5A)
                            //{
                            //    Console.WriteLine($"Receive the exception of the initial code, {package.StartCode}, and discard the current 1 byte messages。");
                            //    reader.Advance(1);
                            //    return null;
                            //}
                        }
                        // read protocol version
                        if (reader.TryRead(out firstByte) && reader.TryRead(out secondByte))
                        {
                            package.ProtocolVersion = new byte[] { firstByte, secondByte };
                        }
                        // read reserved field
                        if (reader.TryRead(out firstByte) && reader.TryRead(out secondByte))
                        {
                            package.ReservedField = new byte[] { firstByte, secondByte };
                        }
                        // read data length
                        reader.TryReadBigEndian(out  dataLength);
                        package.DataLength = dataLength;

                        // read data
                        byte[] data = new byte[dataLength];
                        if (reader.TryCopyTo(data))
                        {
                            reader.Advance(dataLength);
                        }

                        package.Data = data.ToArray();

                        // read check code
                        if (reader.TryRead(out firstByte) && reader.TryRead(out secondByte))
                        {
                            var value = new byte[] { secondByte, firstByte };
                            package.CheckCode = value;

                        }

                        // read end code
                        if (reader.TryRead(out firstByte) && reader.TryRead(out secondByte))
                        {
                            package.EndCode = new byte[] { firstByte, secondByte };
                        }

                        List<byte> list = new List<byte>();
                        list.AddRange(package.StartCode);
                        list.AddRange(package.ProtocolVersion);
                        list.AddRange(package.ReservedField);
                        list.AddRange(BitConverter.GetBytes(BinaryPrimitives.ReverseEndianness(package.DataLength)));
                        list.AddRange(package.Data);

                        byte[] result = list.ToArray();

                        var checkSum = CheckAlgorithm.CalculateCrc16(result);
                        var originalcheckSum = BitConverter.ToUInt16(package.CheckCode, 0);
                        if (originalcheckSum != checkSum && (endCodePosition-startCodePosition == 10+dataLength))
                        {
                            Console.WriteLine("Check failed, the message is invalid!!");
                            string hex = BitConverter.ToString(result).Replace("-", "");
                            Console.WriteLine($"{DateTime.Now}");
                            Console.WriteLine(hex);
                            Console.WriteLine($"checkSum:{originalcheckSum.ToString("X4")}");
                            Console.WriteLine($"cal checkSum:{checkSum.ToString("X4")}");
                            return null;
                        }
                        else if (originalcheckSum != checkSum && (endCodePosition - startCodePosition > 10 + dataLength))
                        {
                            reader.Advance(endCodePosition+2);
                            return null;
                        }

                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"{DateTime.Now}");
                        Console.WriteLine($"{e}");
                        return null;
                    }


                    return package;
                }
                else
                {
                    return null;  // 或者抛出一个异常
                }
            }

            public override void Reset()
            {
                // Reset the state of the filter
                headerLength = 8;
                startCodePosition = -1;
            }
        }



        //public class MyCustomProtocolFilter : FixedHeaderPipelineFilter<ProtocolPackage>
        //{

        //    public ISuperSocketHostBuilder<ProtocolPackage> host;

        //    public MyCustomProtocolFilter() : base(8) // Header size is 8 bytes
        //    {

        //    }

        //    protected override int GetBodyLengthFromHeader(ref ReadOnlySequence<byte> buffer)
        //    {
        //        // The position of the start code in the header
        //        const int startCodeFieldOffset = 0;

        //        // Create a reader to read the start code field
        //        var startCodeReader = new SequenceReader<byte>(buffer.Slice(startCodeFieldOffset, 2));

        //        // Read the start code
        //        startCodeReader.TryReadBigEndian(out ushort startCode);

        //        // Check the start code
        //        if (startCode != 0xA55A) // Replace 0xA55A with the actual start code
        //        {
        //            //throw new Exception("error startcode");
        //            return -2;
        //        }

        //        // The position of the data length field in the header
        //        const int dataLengthFieldOffset = 6;

        //        // Create a reader to read the data length field
        //        var dataLengthReader = new SequenceReader<byte>(buffer.Slice(dataLengthFieldOffset, 2));

        //        // Read the data length
        //        dataLengthReader.TryReadBigEndian(out ushort dataLength);

        //        /*supersocket takes the header defined in the constructor and the length of the
        //         * message read here as the length of the message but for the purposes of this 
        //         * protocol, there are four bytes (check code, end code) that are not counted, 
        //         * plus four*/
        //        return dataLength + 4;
        //    }

        //    protected override ProtocolPackage DecodePackage(ref ReadOnlySequence<byte> buffer)
        //    {
        //        var reader = new SequenceReader<byte>(buffer);

        //        var package = new ProtocolPackage();
        //        try
        //        {
        //            // read start code
        //            if (reader.TryRead(out byte firstByte) && reader.TryRead(out byte secondByte))
        //            {
        //                package.StartCode = new byte[] { firstByte, secondByte };
        //                if (package.StartCode[0] != 0xA5 && package.StartCode[1] != 0x5A)
        //                {
        //                    Console.WriteLine($"Receive the exception of the initial code, {package.StartCode}, and discard the current 1 byte messages。");
        //                    reader.Advance(1);
        //                    return null;
        //                }
        //            }
        //            // read protocol version
        //            if (reader.TryRead(out firstByte) && reader.TryRead(out secondByte))
        //            {
        //                package.ProtocolVersion = new byte[] { firstByte, secondByte };
        //            }
        //            // read reserved field
        //            if (reader.TryRead(out firstByte) && reader.TryRead(out secondByte))
        //            {
        //                package.ReservedField = new byte[] { firstByte, secondByte };
        //            }
        //            // read data length
        //            reader.TryReadBigEndian(out ushort dataLength);
        //            package.DataLength = dataLength;

        //            // read data
        //            byte[] data = new byte[dataLength];
        //            if (reader.TryCopyTo(data))
        //            {
        //                reader.Advance(dataLength);
        //            }

        //            package.DataGlobal = data.ToArray();

        //            // read check code
        //            if (reader.TryRead(out firstByte) && reader.TryRead(out secondByte))
        //            {
        //                var value = new byte[] { secondByte, firstByte };
        //                package.CheckCode = value;

        //            }

        //            // read end code
        //            if (reader.TryRead(out firstByte) && reader.TryRead(out secondByte))
        //            {
        //                package.EndCode = new byte[] { firstByte, secondByte };
        //            }
        //        }
        //        catch (Exception e)
        //        {
        //            Console.WriteLine($"{e}");
        //        }


        //        return package;
        //    }

        //}


        public IHost host;

        public bool IsRunning { get; private set; }

        public async void StartServerAsync()
        {
            host = SuperSocketHostBuilder.Create<ProtocolPackage, MyCustomProtocolFilter>()
            .UsePackageHandler(async (s, p) =>
            {
                try
                {

                    List<byte> list = new List<byte>();
                    list.AddRange(p.StartCode);
                    list.AddRange(p.ProtocolVersion);
                    list.AddRange(p.ReservedField);
                    list.AddRange(BitConverter.GetBytes(BinaryPrimitives.ReverseEndianness(p.DataLength)));
                    list.AddRange(p.Data);
                    
                    byte[] result = list.ToArray();

                    //StringBuilder str = new StringBuilder(result.Length * 2);
                    //foreach (byte b in result)
                    //{
                    //    str.AppendFormat("{0:x2}", b);
                    //}
                    //Console.WriteLine(str);

                    var checkSum = CheckAlgorithm.CalculateCrc16(result);
                    UInt16 originalcheckSum = 0;
                    try
                    {
                        originalcheckSum = BitConverter.ToUInt16(p.CheckCode, 0);
                    }
                    catch (Exception)
                    {

                    }
                  
                    if (originalcheckSum != checkSum)
                    {
                        Console.WriteLine("校验失败，报文无效!");
                    }
                    else
                    {
                        var dataFieldParser = new DataFieldParser();
                        DataFieldPackage rev = dataFieldParser.Parse(p.Data);
                        CustomDataParser parser = null;
                        switch (rev.ControlWord)
                        {
                            case 0x0001:
                                parser = new TimeSyncParser();
                                break;
                            case 0x0002:
                                parser = new DCBiasCurrentMeasurementResultParser();
                                break;
                            case 0x0004:
                                parser = new HeartBeatParser();
                                break;
                            case 0x1001:
                                parser = new SoftwareVersionParser();
                                break;
                            case 0x1002:
                                parser = new WorkModeParser();
                                break;
                            case 0x1003:
                                parser = new SamplingDataParser();
                                break;
                            case 0x2001:
                                parser = new SamplingFrequencyAndWorkModeParser();
                                break;
                            case 0x2002:
                                parser = new DeviceResetParser();
                                break;
                            default:
                                parser = new UnDeal();
                                //throw new Exception("Unknown control word: " + rev.ControlWord);
                                break;
                                
                        }
                        ParsedData quadraticRev = parser.ParseData(rev.CustomData);

                        switch (rev.ControlWord)
                        {
                            case 0x0001:
                                break;
                            case 0x0002:

                                if (SystemState.canConnectSQL)
                                {
                                    //创建一个新的异步任务来处理数据库操作
                                    await Task.Run(async () =>
                                    {
                                        //使用数据库帮助类来执行数据库操作
                                        var dbHelper = new DatabaseHelper();
                                        string query;
                                        //插入数据记录   
                                        lock (DataGlobal.dbLock)
                                        {
                                            query = $"INSERT INTO `{DataGlobal.nowRecordTableName}`(imei, current,time,createTime,indexNumber) VALUES ('{BitConverter.ToString(rev.DeviceNumber).Replace("-", "").ToUpper()}','{quadraticRev.DCBiasCurrentMeasurementResult}','{quadraticRev.DataCollectionTime.ToString("yyyy-MM-dd HH:mm:ss.fff")}','{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}','{quadraticRev.IndexNum}')";
                                            var resault = dbHelper.ExecuteNonQuery(query);
                                        }
                                      


                                        query = "SELECT imei, ipEndPoint FROM fct_device_code";
                                        var dataTable = dbHelper.ExecuteQuery(query);

                                        var deviceList = new List<(string Imei, string IpEndPoint)>();
                                        foreach (DataRow row in dataTable.Rows)
                                        {
                                            deviceList.Add((row["imei"].ToString(), row["ipEndPoint"].ToString()));
                                        }

                                        var ipEndPoint = s.RemoteEndPoint as IPEndPoint;
                                        if (ipEndPoint != null)
                                        {
                                            var imei = BitConverter.ToString(rev.DeviceNumber).Replace("-", "").ToUpper();

                                            var device = deviceList.FirstOrDefault(d => d.Imei == imei);


                                            if (device.Imei == null)
                                            {

                                                    // If the IMEI does not exist, insert it into the fct_device_code
                                                    query = $"INSERT INTO fct_device_code (imei, ipEndPoint,onlineState) VALUES ('{BitConverter.ToString(rev.DeviceNumber).Replace("-", "").ToUpper()}', '{ipEndPoint}','1')";
                                                    dbHelper.ExecuteNonQuery(query);
                                                
               
                                            }
                                            else if (device.IpEndPoint != ipEndPoint.ToString())
                                            {
                                    
                                                    // If the IMEI exists but the ipEndPoint does not match, update the current ipEndPoint in the fct_device_code
                                                    query = $"UPDATE fct_device_code SET ipEndPoint = '{ipEndPoint}' WHERE imei = '{BitConverter.ToString(rev.DeviceNumber).Replace("-", "").ToUpper()}'";
                                                    dbHelper.ExecuteNonQuery(query);
                                                
                                            }
                                            // If both match, do not perform any database operations
                                        }


                                    });
                                }
                               
                                break;
                            case 0x0004:
                                if (SystemState.canConnectSQL)
                                {
                                    //创建一个新的异步任务来处理数据库操作
                                    await Task.Run(async () =>
                                    {
                                        //使用数据库帮助类来执行数据库操作
                                        var dbHelper = new DatabaseHelper();

                                        var query = "SELECT imei, ipEndPoint FROM fct_device_code";
                                        var dataTable = dbHelper.ExecuteQuery(query);

                                        var deviceList = new List<(string Imei, string IpEndPoint)>();
                                        foreach (DataRow row in dataTable.Rows)
                                        {
                                            deviceList.Add((row["imei"].ToString(), row["ipEndPoint"].ToString()));
                                        }

                                        var ipEndPoint = s.RemoteEndPoint as IPEndPoint;

                                        // 更新设备的在线状态
                                        query = $"UPDATE fct_device_code SET onlineState = '1' WHERE ipEndPoint = '{ipEndPoint}'";
                                        dbHelper.ExecuteNonQuery(query);


                                        if (ipEndPoint != null)
                                        {
                                            var imei = BitConverter.ToString(rev.DeviceNumber).Replace("-", "").ToUpper();

                                            var device = deviceList.FirstOrDefault(d => d.Imei == imei);


                                            if (device.Imei == null)
                                            {
                                                // If the IMEI does not exist, insert it into the fct_device_code
                                                query = $"INSERT INTO fct_device_code (imei, ipEndPoint,onlineState) VALUES ('{BitConverter.ToString(rev.DeviceNumber).Replace("-", "").ToUpper()}', '{ipEndPoint}','1')";
                                                dbHelper.ExecuteNonQuery(query);
                                            }
                                            else if (device.IpEndPoint != ipEndPoint.ToString())
                                            {
                                                // If the IMEI exists but the ipEndPoint does not match, update the current ipEndPoint in the fct_device_code
                                                query = $"UPDATE fct_device_code SET ipEndPoint = '{ipEndPoint}' WHERE imei = '{BitConverter.ToString(rev.DeviceNumber).Replace("-", "").ToUpper()}'";
                                                dbHelper.ExecuteNonQuery(query);
                                            }
                                            // If both match, do not perform any database operations
                                        }


                                    });
                                }
                             
                                break;
                            case 0x1001:
                                break;
                            case 0x1002:
                                break;
                            case 0x1003:
                                if (SystemState.canConnectSQL)
                                {
                                    await Task.Run(async () =>
                                    {
                                        //使用数据库帮助类来执行数据库操作
                                        var dbHelper = new DatabaseHelper();

                                        var query = $"SELECT  code FROM fct_device_code WHERE imei = '{BitConverter.ToString(rev.DeviceNumber).Replace("-", "").ToUpper()}'";

                                        var dataTable = dbHelper.ExecuteQuery(query);
                                        string codeNum = null;
                                        foreach (DataRow row in dataTable.Rows)
                                        {
                                            codeNum = row["code"].ToString();
                                        }
                                        if (codeNum != null)
                                        {

                                            query = $"SELECT  code FROM fct_setting WHERE code = '{codeNum}'";

                                            var rec = dbHelper.ExecuteQuery(query);

                                            if (rec != null)
                                            {
                                                // If the IMEI exists but the ipEndPoint does not match, update the current ipEndPoint in the fct_device_code
                                                query = $"UPDATE fct_setting SET `interval` = '{quadraticRev.SamplingData.Frequency}'," +
                                                $"soc = '{quadraticRev.SamplingData.BatteryVoltage}'," +
                                                $"temprature = '{quadraticRev.SamplingData.Temperature}'," +
                                                $"sdSize = '{quadraticRev.SamplingData.SDUsedSpace}/{quadraticRev.SamplingData.SDTotalSpace}'," +
                                                $"workinghours = '{quadraticRev.SamplingData.WorkingDuration}'," +
                                                $"runState = '{quadraticRev.SamplingData.SamplingState}'" +
                                                $" WHERE code = '{codeNum}'";
                                                dbHelper.ExecuteNonQuery(query);
                                            }
                                            // If both match, do not perform any database operations
                                        }


                                    });
                                }
                               
                                break;
                            case 0x2001:
                                break;
                            case 0x2002:
                                break;
                            default:
                                break;

                        }

                     



                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e); 
                    //throw;
                }

               
            })
            .ConfigureSuperSocket(options =>
            {
                options.Name = "Kangda Electric data acquisition server";
                options.AddListener(new ListenOptions
                {
                    Ip = "Any",
                    Port = Convert.ToInt16(ConfigurationHelper.GetPortValue("webServiceConfig.xml", "tcpServerPort", "add", "port"))
                }
                ); 

            })
            .UseSessionHandler(async (s) =>
            {
                // 这个方法在新的会话连接时被调用
                Console.WriteLine("A new session connected: " + s.SessionID);

                // 获取设备的 IP 端点
                var ipEndPoint = s.RemoteEndPoint as IPEndPoint;

                if (ipEndPoint != null)
                {

                    // 检查列表中是否已经有这个会话，如果没有，就添加到列表中
                    if (!sessions.Contains(s))
                    {
                        sessions.Add(s);
                    }
                    if (SystemState.canConnectSQL)
                    {
                        // 使用数据库帮助类来执行数据库操作
                        var dbHelper = new DatabaseHelper();

                        // 更新设备的在线状态
                        var query = $"UPDATE fct_device_code SET onlineState = '1' WHERE ipEndPoint = '{ipEndPoint}'";
                        dbHelper.ExecuteNonQuery(query);

                        query = "SELECT ipEndPoint FROM fct_device_code";
                        var dataTable = dbHelper.ExecuteQuery(query);

                        var deviceList = new List<(string ip, string port)>();
                        foreach (DataRow row in dataTable.Rows)
                        {
                            var ipEndPointTmp = row["ipEndPoint"].ToString().Split(':');
                            deviceList.Add((ipEndPointTmp[0], ipEndPointTmp[1]));
                        }


                        var device = deviceList.FirstOrDefault(d => d.ip == ipEndPoint.Address.ToString());

                        if (device.port != ipEndPoint.Port.ToString())
                        {
                            // If the IP exists but the port does not match, update the current ipEndPoint in the fct_device_code
                            query = $"UPDATE fct_device_code SET ipEndPoint = '{ipEndPoint}' WHERE ipEndPoint = '{device.ip}:{device.port}'";
                            dbHelper.ExecuteNonQuery(query);
                        }
                    }
                   

                }
            }, async (s, e) =>
            {
                // 从列表中移除这个会话
                if (sessions.Contains(s))
                {
                    sessions.Remove(s);
                }
                // 这个方法在会话关闭时被调用
                Console.WriteLine("A session closed: " + s.SessionID);

                // 获取设备的 IP 端点
                var ipEndPoint = s.RemoteEndPoint as IPEndPoint;
                if (SystemState.canConnectSQL)
                {
                    if (ipEndPoint != null)
                    {
                        // 使用数据库帮助类来执行数据库操作
                        var dbHelper = new DatabaseHelper();

                        // 更新设备的在线状态
                        var query = $"UPDATE fct_device_code SET onlineState = '0' WHERE ipEndPoint = '{ipEndPoint}'";
                        dbHelper.ExecuteNonQuery(query);
                    }
                }

            })
            .ConfigureLogging((hostCtx, loggingBuilder) =>
            {
                loggingBuilder.AddConsole();
            })
            .Build();
            // 在服务器启动时设置 IsRunning 为 true
            IsRunning = true;



            await host.RunAsync();

            // 如果 RunAsync 方法返回，表示服务器已停止，所以设置 IsRunning 为 false
            IsRunning = false;

        }

        // 添加一个新的方法来获取所有的会话
        //public IEnumerable<IAppSession> GetAllSessions()
        //{
        //    if (host!=null)
        //    {
        //        var service = host.Services.GetService<IServer>() as IServer;
        //        return service.GetSessionContainer().GetSessions();
        //    }

        //    return Enumerable.Empty<IAppSession>(); 

        //}
        public IEnumerable<IAppSession> GetAllSessions()
        {
            if (host == null || !IsRunning)
            {
                throw new Exception("Server is not running.");
            }

            var service = host.Services.GetService<ISuperSocketHostBuilder<ProtocolPackage>>() as IServer;
            if (service == null)
            {
                throw new Exception("IServer is not initialized.");
            }

            try
            {
                return service.GetSessionContainer().GetSessions();
            }
            catch (NullReferenceException ex)
            {
                // Log the exception
                Console.WriteLine("Error in GetAllSessions: " + ex.Message);
                return Enumerable.Empty<IAppSession>();
            }
        }

    }
}
