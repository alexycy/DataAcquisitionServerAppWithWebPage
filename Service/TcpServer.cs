using SuperSocket;
using SuperSocket.ProtoBase;
using System.Buffers;
using System.Text;
using static DataAcquisitionServerAppWithWebPage.Protocol.KDonlinemonitoring;

namespace DataAcquisitionServerAppWithWebPage.Service
{
    public class TcpServer
    {
        public class MyCustomProtocolFilter : FixedHeaderPipelineFilter<MyCustomProtocolPackage>
        {
            public MyCustomProtocolFilter() : base(14) // Header size is 14 bytes
            {
            }

            protected override int GetBodyLengthFromHeader(ref ReadOnlySequence<byte> buffer)
            {
                // The position of the data length field in the header
                const int dataLengthFieldOffset = 6;

                // Create a reader to read the data length field
                var reader = new SequenceReader<byte>(buffer.Slice(dataLengthFieldOffset, 2));

                // Read the data length
                reader.TryReadBigEndian(out ushort dataLength);

                return dataLength;
            }

            protected override MyCustomProtocolPackage DecodePackage(ref ReadOnlySequence<byte> buffer)
            {
                var reader = new SequenceReader<byte>(buffer);

                var package = new MyCustomProtocolPackage();

                // read start code
                if (reader.TryRead(out byte firstByte) && reader.TryRead(out byte secondByte))
                {
                    package.StartCode = new byte[] { firstByte, secondByte };
                }

                // read protocol version
                reader.TryReadTo(out ReadOnlySpan<byte> protocolVersion, 2, advancePastDelimiter: true);
                package.ProtocolVersion = protocolVersion.ToArray();

                // read reserved field
                reader.TryReadTo(out ReadOnlySpan<byte> reservedField, 2, advancePastDelimiter: true);
                package.ReservedField = reservedField.ToArray();

                // read data length
                reader.TryReadBigEndian(out ushort dataLength);
                package.DataLength = dataLength;

                // read data
                reader.TryReadTo(out ReadOnlySpan<byte> data, (byte)dataLength, advancePastDelimiter: true);
                package.Data = startCode.ToArray();

                // read check code
                reader.TryReadTo(out ReadOnlySpan<byte> checkCode, 2, advancePastDelimiter: true);
                package.CheckCode = checkCode.ToArray();

                // read end code
                reader.TryReadTo(out ReadOnlySpan<byte> endCode, 2, advancePastDelimiter: true);
                package.EndCode = endCode.ToArray();

                return package;
            }
        }



        public async void StartServerAsync()
        {
            var host = SuperSocketHostBuilder.Create<MyCustomProtocolPackage, MyCustomProtocolFilter>()
            .UsePackageHandler(async (s, p) =>
            {
                //await s.SendAsync(Encoding.UTF8.GetBytes(p.Text + "\r\n"));
                await s.SendAsync(p.Data);
            })
            .ConfigureSuperSocket(options =>
            {
                options.Name = "Echo Server";
                options.AddListener(new ListenOptions
                {
                    Ip = "Any",
                    Port = 4040
                }
                );
            })
            .ConfigureLogging((hostCtx, loggingBuilder) =>
            {
                loggingBuilder.AddConsole();
            })
            .Build();

            await host.RunAsync();
        }


    }
}
