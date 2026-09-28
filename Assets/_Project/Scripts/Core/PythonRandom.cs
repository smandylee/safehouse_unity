using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace Safehouse.Core
{
    /// <summary>
    /// Python's random.Random (MT19937), including getstate/setstate. Expedition files store that exact
    /// state, so a zone resolved after the game was closed is the same zone, not a new roll.
    /// </summary>
    public sealed class PythonRandom
    {
        public const int StateLength = 624;
        private const int M = 397;
        private const uint MatrixA = 0x9908b0dfU;
        private const uint UpperMask = 0x80000000U;
        private const uint LowerMask = 0x7fffffffU;

        private readonly uint[] _mt = new uint[StateLength];
        private int _index = StateLength;
        private double? _gaussNext;

        public static PythonRandom Create()
        {
            var bytes = new byte[16];
            using (var crypto = RandomNumberGenerator.Create())
            {
                crypto.GetBytes(bytes);
            }

            var key = new uint[4];
            for (var i = 0; i < 4; i++)
            {
                key[i] = BitConverter.ToUInt32(bytes, i * 4);
            }

            var random = new PythonRandom();
            random.InitByArray(key);
            return random;
        }

        public static PythonRandom Seed(int seed)
        {
            var random = new PythonRandom();
            random.InitByArray(seed == 0 ? new uint[] { 0 } : Words(seed < 0 ? -seed : seed));
            return random;
        }

        public (int Version, uint[] Words, int Index, double? GaussNext) GetState()
        {
            var words = new uint[StateLength];
            Array.Copy(_mt, words, StateLength);
            return (3, words, _index, _gaussNext);
        }

        public void SetState(int version, uint[] words, int index, double? gaussNext)
        {
            if (version != 3 || words == null || words.Length != StateLength || index < 0 || index > StateLength)
            {
                throw new ValidationException("Corrupt random-generator state. The expedition file may have been edited by hand.");
            }

            Array.Copy(words, _mt, StateLength);
            _index = index;
            _gaussNext = gaussNext;
        }

        public double Random()
        {
            var a = NextUInt32() >> 5;
            var b = NextUInt32() >> 6;
            return (a * 67108864.0 + b) * (1.0 / 9007199254740992.0);
        }

        /// <summary>Python randrange(stop): an int in [0, stop).</summary>
        public int RandRange(int stop) => RandBelow(stop);

        /// <summary>Python randint(low, high), inclusive.</summary>
        public int RandInt(int low, int high) => low + RandBelow(high - low + 1);

        public T Choice<T>(IReadOnlyList<T> sequence) => sequence[RandBelow(sequence.Count)];

        public T Choices<T>(IReadOnlyList<T> population, IReadOnlyList<double> weights)
        {
            var cumulative = new double[weights.Count];
            var total = 0.0;
            for (var i = 0; i < weights.Count; i++)
            {
                total += weights[i];
                cumulative[i] = total;
            }

            return population[BisectRight(cumulative, Random() * total)];
        }

        public double Uniform(double a, double b) => a + (b - a) * Random();

        private int RandBelow(int n)
        {
            if (n <= 0)
            {
                throw new ValidationException("empty range for randrange()");
            }

            var bits = BitLength(n);
            uint draw;
            do
            {
                draw = NextUInt32();
                if (bits < 32)
                {
                    draw >>= 32 - bits;
                }
            }
            while (draw >= (uint)n);

            return (int)draw;
        }

        private uint NextUInt32()
        {
            if (_index >= StateLength)
            {
                Twist();
                _index = 0;
            }

            var y = _mt[_index++];
            y ^= y >> 11;
            y ^= (y << 7) & 0x9d2c5680U;
            y ^= (y << 15) & 0xefc60000U;
            y ^= y >> 18;
            return y;
        }

        private void Twist()
        {
            for (var kk = 0; kk < StateLength - M; kk++)
            {
                var y = (_mt[kk] & UpperMask) | (_mt[kk + 1] & LowerMask);
                _mt[kk] = _mt[kk + M] ^ (y >> 1) ^ ((y & 1U) == 0U ? 0U : MatrixA);
            }

            for (var kk = StateLength - M; kk < StateLength - 1; kk++)
            {
                var y = (_mt[kk] & UpperMask) | (_mt[kk + 1] & LowerMask);
                _mt[kk] = _mt[kk + (M - StateLength)] ^ (y >> 1) ^ ((y & 1U) == 0U ? 0U : MatrixA);
            }

            var last = (_mt[StateLength - 1] & UpperMask) | (_mt[0] & LowerMask);
            _mt[StateLength - 1] = _mt[M - 1] ^ (last >> 1) ^ ((last & 1U) == 0U ? 0U : MatrixA);
        }

        private void InitByArray(uint[] key)
        {
            _mt[0] = 19650218U;
            for (var i = 1; i < StateLength; i++)
            {
                _mt[i] = 1812433253U * (_mt[i - 1] ^ (_mt[i - 1] >> 30)) + (uint)i;
            }

            var index = 1;
            var keyIndex = 0;
            for (var k = System.Math.Max(StateLength, key.Length); k > 0; k--)
            {
                _mt[index] = (_mt[index] ^ ((_mt[index - 1] ^ (_mt[index - 1] >> 30)) * 1664525U))
                    + key[keyIndex] + (uint)keyIndex;
                index++;
                keyIndex++;
                if (index >= StateLength)
                {
                    _mt[0] = _mt[StateLength - 1];
                    index = 1;
                }

                if (keyIndex >= key.Length)
                {
                    keyIndex = 0;
                }
            }

            for (var k = StateLength - 1; k > 0; k--)
            {
                _mt[index] = (_mt[index] ^ ((_mt[index - 1] ^ (_mt[index - 1] >> 30)) * 1566083941U))
                    - (uint)index;
                index++;
                if (index >= StateLength)
                {
                    _mt[0] = _mt[StateLength - 1];
                    index = 1;
                }
            }

            _mt[0] = 0x80000000U;
            _index = StateLength;
            _gaussNext = null;
        }

        private static uint[] Words(int value)
        {
            var words = new List<uint>();
            var remaining = (uint)value;
            if (value < 0)
            {
                remaining = (uint)(-value);
            }

            if (remaining == 0)
            {
                return new uint[] { 0 };
            }

            while (remaining > 0)
            {
                words.Add(remaining);
                remaining = 0; // values we seed with fit in 32 bits
            }

            return words.ToArray();
        }

        private static int BitLength(int n)
        {
            var bits = 0;
            var value = (uint)n;
            while (value > 0)
            {
                bits++;
                value >>= 1;
            }

            return bits;
        }

        private static int BisectRight(double[] cumulative, double needle)
        {
            var lo = 0;
            var hi = cumulative.Length - 1;
            while (lo < hi)
            {
                var mid = (lo + hi) / 2;
                if (needle < cumulative[mid])
                {
                    hi = mid;
                }
                else
                {
                    lo = mid + 1;
                }
            }

            return lo;
        }
    }
}
