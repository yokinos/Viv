using System;
using System.Collections.Generic;
using System.Text;

namespace Viv.Vva
{
    public class Crc64Helper
    {
        // CRC64参数（ECMA标准，保证分布均匀）
        private const ulong Crc64Polynomial = 0xC96C5795D7870F42;
        private static readonly ulong[] Crc64Table = CreateCrc64Table();

        /// <summary>
        /// 预生成CRC64查表（仅初始化一次，提升性能）
        /// </summary>
        private static ulong[] CreateCrc64Table()
        {
            var table = new ulong[256];
            for (ulong i = 0; i < 256; i++)
            {
                ulong value = i;
                for (int j = 0; j < 8; j++)
                {
                    value = (value >> 1) ^ ((value & 1) * Crc64Polynomial);
                }
                table[i] = value;
            }
            return table;
        }

        /// <summary>
        /// 高性能CRC64哈希计算（无堆分配，纯栈操作）
        /// </summary>
        public static ulong ComputeCrc64(ReadOnlySpan<char> key)
        {
            ulong crc = 0xFFFFFFFFFFFFFFFF;
            foreach (char c in key)
            {
                // 直接处理char，避免转换为byte数组（减少内存分配）
                crc = (crc >> 8) ^ Crc64Table[(crc ^ (byte)c) & 0xFF];
                crc = (crc >> 8) ^ Crc64Table[(crc ^ (byte)(c >> 8)) & 0xFF];
            }
            return ~crc;
        }

        public static ulong ComputeCrc64(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return 0;

            return ComputeCrc64(key.AsSpan());
        }

        public static ulong ComputeCrc64(byte[] keyBytes)
        {
            if (keyBytes == null || keyBytes.Length == 0)
                return 0;

            ulong crc = 0xFFFFFFFFFFFFFFFF;
            foreach (byte b in keyBytes)
            {
                crc = (crc >> 8) ^ Crc64Table[(crc ^ b) & 0xFF];
            }
            return ~crc;
        }
    }
}
