using MinorShift.Emuera.Runtime.Utils.EvilMask;

namespace MinorShift.Emuera.GameData.Function;

internal static partial class BitArrayManager
{
	private static readonly long[] BIT;

	static BitArrayManager()
	{
		BIT = new long[65];
		for (int i = 1; i <= 64; i++)
			BIT[i] = 1L << (i - 1);
	}

	public static void BitSet(long[] array, long idx, long val, long length)
	{
		if (array == null) return;
		long size = array.LongLength * 64;
		for (long i = 0; i < length; i++)
		{
			long index = idx + i;
			if (index >= size) break;
			long udx = index / 64;
			long j = index % 64 + 1;
			if (val != 0)
				array[udx] |= BIT[j];
			else
				array[udx] &= ~BIT[j];
		}
	}

	public static long BitGet(long[] array, long idx)
	{
		if (array == null) return -1;
		long size = array.LongLength * 64;
		if (idx >= size || idx < 0) return -1;
		long udx = idx / 64;
		idx = idx % 64 + 1;
		return (array[udx] & BIT[idx]) != 0 ? 1 : 0;
	}

	public static long BitToggle(long[] array, long idx)
	{
		if (array == null) return 0;
		long size = array.LongLength * 64;
		if (idx >= size || idx < 0) return 0;
		long udx = idx / 64;
		idx = idx % 64 + 1;
		array[udx] ^= BIT[idx];
		return 1;
	}

	public static long BitIndexOfFirst(long[] array, long val)
	{
		if (array == null) return -1;
		bool searchForSet = val != 0;
		long arraySize = array.LongLength;
		long _j = 0;
		while (_j < arraySize && array[_j] == (searchForSet ? 0 : -1))
			_j++;
		for (long i = 0; i < 64; i++)
		{
			long bitVal = BitGet(array, _j * 64 + i);
			if (bitVal == -1) return -1;
			if ((bitVal == 1) == searchForSet)
				return _j * 64 + i;
		}
		return -1;
	}
}