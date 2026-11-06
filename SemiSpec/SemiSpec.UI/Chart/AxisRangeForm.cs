using System.Globalization;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using ReactiveUI;

using SemiSpec.Core.Spectrum;
using SemiSpec.UI.Localization;

namespace SemiSpec.UI.Chart;

/// <summary>
/// The two bound fields of an axis flyout: seeded from the axis, written back when a valid pair differs from the
/// seed; ported from SemiPlot's axis scale panel.
/// </summary>
public sealed class AxisRangeForm : ReactiveObject, IAxisFlyoutForm, IDisposable
{
	private const string NumberFormat = "0.###";

	private readonly Func<AxisRange> _read;
	private readonly Action<AxisRange> _write;
	private readonly Func<AxisRange, string> _axisRule;
	private readonly Subject<Unit> _closeRequests = new();
	private AxisRange _seeded;
	private bool _isCancelled;

	/// <summary>The axis rule tells why the axis cannot show a pair, or returns an empty string.</summary>
	public AxisRangeForm(Func<AxisRange> read, Action<AxisRange> write, Func<AxisRange, string>? axisRule = null)
	{
		_read = read;
		_write = write;
		_axisRule = axisRule ?? (_ => string.Empty);
		CommitCommand = ReactiveCommand.Create(Commit);
		CancelCommand = ReactiveCommand.Create(Cancel);
	}

	/// <summary>The flyout host closes the flyout on each signal.</summary>
	public IObservable<Unit> CloseRequests => _closeRequests.AsObservable();

	public string MinimumText
	{
		get;
		set
		{
			this.RaiseAndSetIfChanged(ref field, value);
			this.RaisePropertyChanged(nameof(ValidationMessage));
		}
	} = string.Empty;

	public string MaximumText
	{
		get;
		set
		{
			this.RaiseAndSetIfChanged(ref field, value);
			this.RaisePropertyChanged(nameof(ValidationMessage));
		}
	} = string.Empty;

	/// <summary>The rule the pair breaks, or an empty string while it is valid.</summary>
	public string ValidationMessage => Validate(out _);

	public ReactiveCommand<Unit, Unit> CommitCommand { get; }

	public ReactiveCommand<Unit, Unit> CancelCommand { get; }

	/// <summary>Fills both fields from the axis as it is drawn now.</summary>
	public void Seed()
	{
		_isCancelled = false;
		_seeded = _read();
		MinimumText = _seeded.Min.ToString(NumberFormat, CultureInfo.CurrentCulture);
		MaximumText = _seeded.Max.ToString(NumberFormat, CultureInfo.CurrentCulture);
	}

	/// <summary>Writes a valid pair that differs from the seed, then seeds again; the flyout stays open.</summary>
	public void Commit()
	{
		if (Validate(out var typed).Length > 0 || typed == Rounded(_seeded))
		{
			return;
		}

		_write(typed);
		Seed();
	}

	/// <summary>A close commits pending fields unless Escape cancelled them.</summary>
	public void Close()
	{
		if (!_isCancelled)
		{
			Commit();
		}
	}

	public void Dispose()
	{
		CommitCommand.Dispose();
		CancelCommand.Dispose();
		_closeRequests.Dispose();
	}

	private string Validate(out AxisRange typed)
	{
		typed = default;
		if (!TryRead(MinimumText, out var minimum) || !TryRead(MaximumText, out var maximum))
		{
			return Resources.AxisScaleBoundInvalid;
		}

		typed = new AxisRange(minimum, maximum);
		return minimum < maximum ? _axisRule(typed) : Resources.AxisScaleMinimumBelowMaximum;
	}

	private static bool TryRead(string text, out double value)
	{
		return double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) && double.IsFinite(value);
	}

	/// <summary>The seed as its fields show it, so an untouched field never counts as a change.</summary>
	private static AxisRange Rounded(AxisRange range)
	{
		TryRead(range.Min.ToString(NumberFormat, CultureInfo.CurrentCulture), out var minimum);
		TryRead(range.Max.ToString(NumberFormat, CultureInfo.CurrentCulture), out var maximum);
		return new AxisRange(minimum, maximum);
	}

	private void Cancel()
	{
		_isCancelled = true;
		_closeRequests.OnNext(Unit.Default);
	}
}
