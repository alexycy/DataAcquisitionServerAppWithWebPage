namespace DataAcquisitionServerAppWithWebPage.Utilities
{
    public static class CheckAlgorithm
    {
        public static UInt16 CalculateCrc16(byte[] bytes)
        {
            UInt16 crc = 0xffff;

            for (int n = 0; n < bytes.Length; n++)      /*此处的len -- 要校验的位数为len个*/
            {
                crc = (UInt16)(bytes[n] ^ crc);

                for (int i = 0; i < 8; i++)
                {  /*此处的8 -- 指每一个bytesar类型有8bit，每bit都要处理*/
                    if ((crc & 0x01) > 0)
                    {
                        crc = (UInt16)(crc >> 1);
                        crc = (UInt16)(crc ^ 0xa001);
                    }
                    else
                    {
                        crc = (UInt16)(crc >> 1);
                    }
                }
            }
            /*返回CRC校验后的值*/
            return crc;

        }

    }
}
