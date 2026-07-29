namespace MinorShift.Emuera.Runtime.Utils;

internal sealed class WinInput
{
	static readonly int[] _keyState = new int[256];
	static readonly short[] _keyToggle = new short[256];
	// Latch: records that a key was pressed since last check by GETKEYTRIGGERED.
	// This prevents lost clicks when MouseDown and MouseUp fire in the same DoEvents().
	static readonly int[] _keyLatch = new int[256];

	public static void SetKeyPressed(int keyCode)
	{
		if (keyCode < 0 || keyCode >= 256) return;
		System.Threading.Thread.VolatileWrite(ref _keyState[keyCode], 0x8000);
		System.Threading.Thread.VolatileWrite(ref _keyLatch[keyCode], 1);
	}

	public static void SetKeyReleased(int keyCode)
	{
		if (keyCode < 0 || keyCode >= 256) return;
		System.Threading.Thread.VolatileWrite(ref _keyState[keyCode], 0);
	}

	public static short GetKeyState(int nVirtKey)
	{
		if (nVirtKey < 0 || nVirtKey >= 256) return 0;
		return (short)System.Threading.Thread.VolatileRead(ref _keyState[nVirtKey]);
	}

	/// <summary>
	/// Consume the latch for a key. Returns 1 if the key was pressed since last check, 0 otherwise.
	/// </summary>
	public static int ConsumeKeyLatch(int nVirtKey)
	{
		if (nVirtKey < 0 || nVirtKey >= 256) return 0;
		return System.Threading.Interlocked.Exchange(ref _keyLatch[nVirtKey], 0);
	}

	/// <summary>
	/// Clear all latches. Called at the start of AWAIT to prevent latch leakage
	/// from previous input mode (INPUTS/TINPUTS) into AWAIT+GETKEYTRIGGERED loops.
	/// </summary>
	public static void ClearLatches()
	{
		for (int i = 0; i < 256; i++)
			System.Threading.Volatile.Write(ref _keyLatch[i], 0);
	}

	public static short GetKeyToggle(int nVirtKey)
	{
		if (nVirtKey < 0 || nVirtKey >= 256) return 0;
		return _keyToggle[nVirtKey];
	}

	public static void SetKeyToggle(int nVirtKey, short value)
	{
		if (nVirtKey < 0 || nVirtKey >= 256) return;
		_keyToggle[nVirtKey] = value;
	}

	public static void ResetAllKeys()
	{
		for (int i = 0; i < 256; i++)
		{
			_keyState[i] = 0;
			_keyToggle[i] = 0;
			_keyLatch[i] = 0;
		}
	}
}
