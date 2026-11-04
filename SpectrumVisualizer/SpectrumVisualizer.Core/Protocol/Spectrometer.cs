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
	}
}
