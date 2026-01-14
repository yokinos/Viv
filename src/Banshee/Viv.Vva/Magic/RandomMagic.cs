using System;
using System.Runtime.CompilerServices;

namespace Viv.Vva.Magic
{
    public static class RandomMagic
    {
        private static readonly Random _random = Random.Shared;

        /// <summary>
        /// 返回一个非负随机整数
        /// </summary>
        /// <returns>大于或等于0且小于System.Int32.MaxValue的32位有符号整数</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Next() => _random.Next();

        /// <summary>
        /// 返回一个小于所指定最大值的非负随机整数
        /// </summary>
        /// <param name="maxValue">要生成的随机数的上限(随机数不能取该上限值)，必须大于或等于0</param>
        /// <returns>大于或等于零且小于maxValue的32位有符号整数；如果maxValue等于0，则返回0</returns>
        /// <exception cref="ArgumentOutOfRangeException">maxValue为负数时抛出</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Next(int maxValue)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(maxValue, nameof(maxValue));
            return _random.Next(maxValue);
        }

        /// <summary>
        /// 返回在指定范围内的任意整数
        /// </summary>
        /// <param name="minValue">返回的随机数的下界(随机数可取该下界值)</param>
        /// <param name="maxValue">返回的随机数的上界(随机数不能取该上界值)，必须大于或等于 minValue</param>
        /// <returns>大于等于 minValue 且小于 maxValue 的 32 位带符号整数；如果minValue等于maxValue，则返回minValue</returns>
        /// <exception cref="ArgumentOutOfRangeException">minValue大于maxValue时抛出</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Next(int minValue, int maxValue)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(minValue, maxValue, nameof(minValue));
            return _random.Next(minValue, maxValue);
        }

        /// <summary>
        /// 用随机数填充指定字节数组的元素
        /// </summary>
        /// <param name="buffer">包含随机数的字节数组（不能为null）</param>
        /// <exception cref="ArgumentNullException">buffer为null时抛出</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void NextBytes(byte[] buffer)
        {
            ArgumentNullException.ThrowIfNull(buffer, nameof(buffer));
            _random.NextBytes(buffer);
        }

        /// <summary>
        /// 用随机数填充指定字节数组的元素
        /// </summary>
        /// <param name="buffer">包含随机数的字节数组</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void NextBytes(Span<byte> buffer) => _random.NextBytes(buffer);

        /// <summary>
        /// 返回一个大于或等于 0.0 且小于 1.0 的随机浮点数
        /// </summary>
        /// <returns>大于或等于 0.0 且小于 1.0 的双精度浮点数</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double NextDouble() => _random.NextDouble();

        /// <summary>
        /// 返回一个大于或等于0.0且小于1.0的随机浮点数
        /// </summary>
        /// <returns>大于或等于0.0且小于1.0的单精度浮点数</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float NextSingle() => _random.NextSingle();

        /// <summary>
        /// 返回一个非负随机整数
        /// </summary>
        /// <returns>大于或等于0且小于System.Int64.MaxValue的64位有符号整数</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long NextInt64() => _random.NextInt64();

        /// <summary>
        /// 返回一个小于指定最大值的非负随机整数
        /// </summary>
        /// <param name="maxValue">要生成的随机数的唯一上界，必须大于或等于0</param>
        /// <returns>大于或等于0且小于maxValue的64位带符号整数；如果maxValue等于0，则返回0</returns>
        /// <exception cref="ArgumentOutOfRangeException">maxValue为负数时抛出</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long NextInt64(long maxValue)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(maxValue, nameof(maxValue));
            return _random.NextInt64(maxValue);
        }

        /// <summary>
        /// 返回一个在指定范围内的随机整数
        /// </summary>
        /// <param name="minValue">返回的随机数的包含下限</param>
        /// <param name="maxValue">返回的随机数的独占上限，必须大于或等于minValue</param>
        /// <returns>大于或等于minValue且小于maxValue的64位有符号整数；如果minValue等于maxValue，则返回minValue</returns>
        /// <exception cref="ArgumentOutOfRangeException">minValue大于maxValue时抛出</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long NextInt64(long minValue, long maxValue)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(minValue, maxValue, nameof(minValue));
            return _random.NextInt64(minValue, maxValue);
        }
    }
}