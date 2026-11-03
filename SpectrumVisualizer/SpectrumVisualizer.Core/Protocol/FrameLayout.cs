namespace SpectrumVisualizer.Core.Protocol;

/// <summary>Byte layout of a board-to-PC frame, shared by the encoder and the decoder.</summary>
internal static class FrameLayout
{
	public const byte SyncFirst = 0xAA;
	public const byte SyncSecond = 0x55;
	public const int LengthOffset = 2;
	public const int SyncAndLengthSize = 4;
	public const int StateOffset = SyncAndLengthSize;
	public const int SeqOffset = StateOffset + 1;
	public const int StateAndSeqSize = 2;
	public const int MeasurementOffset = StateOffset + StateAndSeqSize;
	public const int MeasurementHeaderSize = 16;
	public const int CrcSize = 2;
	public const int HeartbeatLength = StateAndSeqSize;

	public static int SpectrumLength(Spectrometer device)
	{
		return StateAndSeqSize + MeasurementHeaderSize + (2 * device.PixelCount);
	}

	public static int FrameSize(int length)
	{
		return SyncAndLengthSize + length + CrcSize;
	}
}
