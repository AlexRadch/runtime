// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace System.Linq
{
    public static partial class Enumerable
    {
        public static IEnumerable<int> Range(int start, int count)
        {
            long max = ((long)start) + count - 1;
            if (count < 0 || max > int.MaxValue)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.count);
            }

            if (count == 0)
            {
                return [];
            }

            return new RangeIterator<int>(start, count);
        }

        /// <summary>Generates a sequence of integral numbers within a specified range.</summary>
        /// <typeparam name="T">The <see cref="IBinaryInteger{TSelf}"/> type of the elements in the sequence.</typeparam>
        /// <param name="start">The value of the first <see cref="IBinaryInteger{TSelf}"/> in the sequence.</param>
        /// <param name="count">The number of sequential <see cref="IBinaryInteger{TSelf}"/> to generate.</param>
        /// <returns>An <see cref="IAsyncEnumerable{T}"/> that contains a range of sequential integral numbers.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is less than 0</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="start"/> + <paramref name="count"/> -1 is larger than <see cref="IMinMaxValue{TSelf}.MaxValue"/>.</exception>
        public static IEnumerable<T> Range<T>(T start, int count) where T : IBinaryInteger<T>
        {
            if (count < 0)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.count);
            }

            if (count == 0)
            {
                return [];
            }

            T max = start + T.CreateTruncating(count - 1);
            if (start > max || RangeIterator<T>.StartMaxCount(start, max) + 1 != count)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.count);
            }

            return new RangeIterator<T>(start, count);
        }

        /// <summary>
        /// An iterator that yields a range of consecutive integers.
        /// </summary>
        [DebuggerDisplay("Count = {CountForDebugger}")]
        private sealed partial class RangeIterator<T> : Iterator<T> where T : IBinaryInteger<T>
        {
            // _start can be equal to _end
            // _start <= _end - T.One
            private readonly T _start;
            private readonly T _end;
            private readonly int _count;

            public RangeIterator(T start, int count)
            {
                Debug.Assert(count > 0);
                Debug.Assert(start <= start + T.CreateTruncating(count - 1));
                Debug.Assert(StartMaxCount(start, start + T.CreateTruncating(count - 1)) + 1 == count);

                _start = start;
                _end = start + T.CreateTruncating(count);
                _count = count;
            }

            private int CountForDebugger => _count; // Count(_start, _end - T.One) + 1;

            private protected override Iterator<T> Clone() => new RangeIterator<T>(_start, _count);

            public override bool MoveNext()
            {
                switch (_state)
                {
                    case 1:
                        Debug.Assert(_start <= _end - T.One); // _start can be equal to _end
                        _current = _start;
                        _state = 2;
                        return true;
                    case 2:
                        if (++_current == _end)
                        {
                            break;
                        }

                        return true;
                }

                _state = -1;
                return false;
            }

            public override void Dispose()
            {
                _state = -1; // Don't reset current
            }

            internal static int StartMaxCount(T start, T max)
            {
                int count = int.CreateTruncating(max - start);
                if (count < 0)
                    count = int.CreateTruncating(max) - int.CreateTruncating(start);
                return count;
            }
        }

        /// <summary>Fills the <paramref name="destination"/> with incrementing numbers, starting from <paramref name="value"/>.</summary>
        private static void FillIncrementing<T>(Span<T> destination, T value) where T : IBinaryInteger<T>
        {
            ref T pos = ref MemoryMarshal.GetReference(destination);
            ref T end = ref Unsafe.Add(ref pos, destination.Length);

            if (Vector.IsHardwareAccelerated && Vector<T>.IsSupported && destination.Length >= Vector<T>.Count)
            {
                Vector<T> current = new Vector<T>(value) + Vector<T>.Indices;
                Vector<T> increment = new Vector<T>(T.CreateTruncating(Vector<T>.Count));

                ref T oneVectorFromEnd = ref Unsafe.Subtract(ref end, Vector<T>.Count);
                do
                {
                    current.StoreUnsafe(ref pos);
                    current += increment;
                    pos = ref Unsafe.Add(ref pos, Vector<T>.Count);
                }
                while (!Unsafe.IsAddressGreaterThan(ref pos, ref oneVectorFromEnd));

                value = current[0];
            }

            while (Unsafe.IsAddressLessThan(ref pos, ref end))
            {
                pos = value++;
                pos = ref Unsafe.Add(ref pos, 1);
            }
        }
    }
}
