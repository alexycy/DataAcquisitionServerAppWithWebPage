using DataAcquisitionServerApp;
using DataAcquisitionServerAppWithWebPage.Protocol;
using DataAcquisitionServerAppWithWebPage.Utilities;
using Org.BouncyCastle.Ocsp;
using SuperSocket;
using SuperSocket.ProtoBase;
using SuperSocket.Server;
using System.Buffers;
using System.Buffers.Binary;
using System.Data;
using System.Net;
using System.Text;
using static DataAcquisitionServerAppWithWebPage.Protocol.KDonlinemonitoring;

namespace DataAcquisitionServerAppWithWebPage.Service
{
    public class TcpServer
    {

        public List<IAppSession> sessions = new List<IAppSession>();
        public class MyCustomProtocolFilter : FixedHeaderPipelineFilter<ProtocolPackage>
        {

            public ISuperSocketHostBuilder<ProtocolPackage> host;

            public MyCustomProtocolFilter() : base(8) // Header size is 8 bytes
            {

            }

            protected override int GetBodyLengthFromHeader(ref ReadOnlySequence<byte> buffer)
            {
                // The position of the start code in the header
                const int startCodeFieldOffset = 0;

                // Create a reader to read the start code field
                var startCodeReader = new SequenceReader<byte>(buffer.Slice(startCodeFieldOffset, 2));

                // Read the start code
                startCodeReader.TryReadBigEndian(out ushort startCode);

                // Check the start code
                if (startCode != 0xA55A) // Replace 0xA55A with the actual start code
                {
                    //throw new Exception("error startcode");
                    return -2;
                }

                // The position of the data length field in the header
                const int dataLengthFieldOffset = 6;

                // Create a reader to read the data length field
                var dataLengthReader = new SequenceReader<byte>(buffer.Slice(dataLengthFieldOffset, 2));

                // Read the data length
                dataLengthReader.TryReadBigEndian(out ushort dataLength);

                /*supersocket takes the header defined in the constructor and the length of the
                 * message read here as the length of the message but for the purposes of this 
                 * protocol, there are four bytes (check code, end code) that are not counted, 
                 * plus four*/
                return dataLength + 4;
            }

            protected override ProtocolPackage DecodePackage(ref ReadOnlySequence<byte> buffer)
            {
                var reader = new SequenceReader<byte>(buffer);

                var package = new ProtocolPackage();
                try
                {
                    // read start code
                    if (reader.TryRead(out byte firstByte) && reader.TryRead(out byte secondByte))
                    {
                        package.StartCode = new byte[] { firstByte, secondByte };
                        if (package.StartCode[0] != 0xA5 && package.StartCode[1]!=0x5A)
                        {
                            Console.WriteLine($"Receive the exception of the initial code, {package.StartCode}, and discard the current 1 byte messages。");
                            reader.Advance(1);
                            return null;
                        }
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
                    reader.TryReadBigEndian(out ushort dataLength);
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
                }
                catch (Exception e)
                {
                    Console.WriteLine($"{e}");
                }


                return package;
            }

        }


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

                    var checkSum = CheckAlgorithm.CalculateCrc16(result);
                    var originalcheckSum = BitConverter.ToUInt16(p.CheckCode, 0);
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
                                //创建一个新的异步任务来处理数据库操作
                                await Task.Run(async () =>
                                {
                                    //使用数据库帮助类来执行数据库操作
                                    var dbHelper = new DatabaseHelper("Server=192.168.1.105;Database=fct_db;Uid=root;Pwd=root;");

                                    //插入数据记录   
                                    var query = $"INSERT INTO fct_measure (imei, current,time,createTime,indexNumber) VALUES ('{BitConverter.ToString(rev.DeviceNumber).Replace("-", "").ToUpper()}','{quadraticRev.DCBiasCurrentMeasurementResult}','{quadraticRev.DataCollectionTime.ToString("yyyy-MM-dd HH:mm:ss.fff")}','{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}','{quadraticRev.IndexNum}')";
                                    var resault = dbHelper.ExecuteNonQuery(query);


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
                                break;
                            case 0x0004:
                                //创建一个新的异步任务来处理数据库操作
                                await Task.Run(async () =>
                                {
                                    //使用数据库帮助类来执行数据库操作
                                    var dbHelper = new DatabaseHelper("Server=192.168.1.105;Database=fct_db;Uid=root;Pwd=root;");

                                    var query = "SELECT imei, ipEndPoint FROM fct_device_code";
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
                                break;
                            case 0x1001:
                                break;
                            case 0x1002:
                                break;
                            case 0x1003:
                                await Task.Run(async () =>
                                {
                                    //使用数据库帮助类来执行数据库操作
                                    var dbHelper = new DatabaseHelper("Server=192.168.1.105;Database=fct_db;Uid=root;Pwd=root;");

                                    var query  = $"SELECT  code FROM fct_device_code WHERE imei = '{BitConverter.ToString(rev.DeviceNumber).Replace("-", "").ToUpper()}'";
                                   
                                    var dataTable = dbHelper.ExecuteQuery(query);
                                    string codeNum  =null;
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
                                            $"soc = '{quadraticRev.SamplingData.Frequency}'," +
                                            $"temprature = '{quadraticRev.SamplingData.Frequency}'," +
                                            $"sdSize = '{quadraticRev.SamplingData.SDUsedSpace}/{quadraticRev.SamplingData.SDTotalSpace}'," +
                                            $"workinghours = '{quadraticRev.SamplingData.WorkingDuration}'" +
                                            $" WHERE code = '{codeNum}'";
                                            dbHelper.ExecuteNonQuery(query);
                                        }
                                        // If both match, do not perform any database operations
                                    }


                                });
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
                    Port = 9009
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

                    // 使用数据库帮助类来执行数据库操作
                    var dbHelper = new DatabaseHelper("Server=192.168.1.105;Database=fct_db;Uid=root;Pwd=root;");

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

                if (ipEndPoint != null)
                {
                    // 使用数据库帮助类来执行数据库操作
                    var dbHelper = new DatabaseHelper("Server=192.168.1.105;Database=fct_db;Uid=root;Pwd=root;");

                    // 更新设备的在线状态
                    var query = $"UPDATE fct_device_code SET onlineState = '0' WHERE ipEndPoint = '{ipEndPoint}'";
                    dbHelper.ExecuteNonQuery(query);
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
