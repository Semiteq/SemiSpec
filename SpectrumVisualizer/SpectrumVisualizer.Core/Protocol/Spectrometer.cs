namespace SpectrumVisualizer.Core.Protocol;

public enum Spectrometer : byte
{
	Atp2000 = 1,
	Atp8000 = 2,
}

public static class SpectrometerExtensions
{
	extension(Spectrometer device)
	{
		public int PixelCount => device switch
		{
			Spectrometer.Atp2000 => 2048,
			Spectrometer.Atp8000 => 512,
			_ => throw new ArgumentOutOfRangeException(nameof(device), device, "Unknown spectrometer."),
		};

		/// <summary>The bytes of this spectrometer's spectrum frame on the wire.</summary>
		public int FrameSize => FrameLayout.FrameSize(FrameLayout.SpectrumLength(device));

		/// <summary>The model name the operator reads on the device and in the status bar.</summary>
		public string DisplayName => device switch
		{
			Spectrometer.Atp2000 => "ATP-2000",
			Spectrometer.Atp8000 => "ATP-8000",
			_ => throw new ArgumentOutOfRangeException(nameof(device), device, "Unknown spectrometer."),
		};

		/// <summary>The wavelength the tools use when none is given: inside the device's firmware table range.</summary>
		public ushort DefaultWavelengthNm => device switch
		{
			Spectrometer.Atp2000 => 650,
			Spectrometer.Atp8000 => 1550,
			_ => throw new ArgumentOutOfRangeException(nameof(device), device, "Unknown spectrometer."),
		};

		/// <summary>The board state bit that says this spectrometer is plugged in.</summary>
		public BoardState ConnectedBit => device switch
		{
			Spectrometer.Atp2000 => BoardState.Atp2000Connected,
			Spectrometer.Atp8000 => BoardState.Atp8000Connected,
			_ => throw new ArgumentOutOfRangeException(nameof(device), device, "Unknown spectrometer."),
		};

		/// <summary>The board state bit that says this spectrometer's last measurement failed.</summary>
		public BoardState FailedBit => device switch
		{
			Spectrometer.Atp2000 => BoardState.Atp2000MeasurementFailed,
			Spectrometer.Atp8000 => BoardState.Atp8000MeasurementFailed,
			_ => throw new ArgumentOutOfRangeException(nameof(device), device, "Unknown spectrometer."),
		};
	}
}
