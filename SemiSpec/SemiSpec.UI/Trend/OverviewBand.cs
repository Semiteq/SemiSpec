using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

using SemiSpec.Core.Trend;

using Pen = Avalonia.Media.Pen;

namespace SemiSpec.UI.Trend;

/// <summary>
/// Draws the overview band: each figure's outline filled at 0.35 of the band colour and its centre line.
/// </summary>
public sealed class OverviewBand : Control
{
	public static readonly StyledProperty<IReadOnlyList<BandFigure>> FiguresProperty =
		AvaloniaProperty.Register<OverviewBand, IReadOnlyList<BandFigure>>(nameof(Figures), []);

	public static readonly StyledProperty<IBrush?> BandColorProperty =
		AvaloniaProperty.Register<OverviewBand, IBrush?>(nameof(BandColor));

	private const double FillOpacity = 0.35;
	private const double CenterLineThickness = 1.0;

	static OverviewBand()
	{
		AffectsRender<OverviewBand>(FiguresProperty, BandColorProperty);
	}

	public IReadOnlyList<BandFigure> Figures
	{
		get => GetValue(FiguresProperty);
		set => SetValue(FiguresProperty, value);
	}

	public IBrush? BandColor
	{
		get => GetValue(BandColorProperty);
		set => SetValue(BandColorProperty, value);
	}

	public override void Render(DrawingContext context)
	{
		var brush = BandColor;
		var figures = Figures;
		if (brush is null || figures.Count == 0)
		{
			return;
		}

		using (context.PushOpacity(FillOpacity))
		{
			context.DrawGeometry(brush, null, Polylines(figures, figure => figure.Outline, isClosed: true));
		}

		context.DrawGeometry(
			null,
			new Pen(brush, CenterLineThickness),
			Polylines(figures, figure => figure.CenterLine, isClosed: false));
	}

	private static StreamGeometry Polylines(
		IReadOnlyList<BandFigure> figures,
		Func<BandFigure, IReadOnlyList<BandPoint>> pointsOf,
		bool isClosed)
	{
		var geometry = new StreamGeometry();
		using var geometryContext = geometry.Open();
		foreach (var figure in figures)
		{
			AddPolyline(geometryContext, pointsOf(figure), isClosed);
		}

		return geometry;
	}

	private static void AddPolyline(StreamGeometryContext context, IReadOnlyList<BandPoint> points, bool isClosed)
	{
		context.BeginFigure(ToPoint(points[0]), isFilled: isClosed);
		for (var index = 1; index < points.Count; index++)
		{
			context.LineTo(ToPoint(points[index]));
		}

		context.EndFigure(isClosed);
	}

	private static Point ToPoint(BandPoint point)
	{
		return new Point(point.X, point.Y);
	}
}
