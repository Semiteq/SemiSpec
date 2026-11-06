using System.Globalization;

using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using SemiSpec.Core.Spectrum;
using SemiSpec.UI.Chart;
using SemiSpec.UI.Localization;

using Xunit;

namespace SemiSpec.Tests.Unit.UI.Chart;

[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class AxisRangeFormTests : IDisposable
{
	private const string NegativeRule = "no negative bounds";

	private readonly List<AxisRange> _written = [];
	private readonly AxisRangeForm _form;
	private AxisRange _axis = new(10.0, 20.0);

	public AxisRangeFormTests()
	{
		_form = new AxisRangeForm(
			() => _axis,
			_written.Add,
			range => range.Min < 0.0 ? NegativeRule : string.Empty);
	}

	public void Dispose()
	{
		_form.Dispose();
	}

	[AvaloniaFact]
	public void TheSeedShowsTheAxisAndIsValid()
	{
		_form.Seed();

		_form.MinimumText.Should().Be(Text(10.0));
		_form.MaximumText.Should().Be(Text(20.0));
		_form.ValidationMessage.Should().BeEmpty();
	}

	[AvaloniaFact]
	public void AnUntouchedSeedWritesNothingOnClose()
	{
		_form.Seed();
		_axis = new AxisRange(30.0, 40.0);

		_form.Close();

		_written.Should().BeEmpty("the fields show the limits of the opening, which the operator did not change");
	}

	[AvaloniaFact]
	public void AnEditedBoundWritesThePairWithTheOtherBoundOfTheOpening()
	{
		_form.Seed();
		_axis = new AxisRange(30.0, 40.0);

		_form.MaximumText = Text(50.0);
		_form.Close();

		_written.Should().Equal(new AxisRange(10.0, 50.0));
	}

	[AvaloniaFact]
	public void EscapeDiscardsTheEditOnClose()
	{
		_form.Seed();
		_form.MaximumText = Text(50.0);

		_form.CancelCommand.Execute().Subscribe();
		_form.Close();

		_written.Should().BeEmpty();
	}

	[AvaloniaFact]
	public void AnUnreadableBoundShowsItsMessageAndWritesNothing()
	{
		_form.Seed();

		_form.MinimumText = "abc";
		_form.Commit();

		_form.ValidationMessage.Should().Be(Resources.AxisScaleBoundInvalid);
		_written.Should().BeEmpty();
	}

	[AvaloniaFact]
	public void AMinimumNotBelowTheMaximumShowsItsMessageAndWritesNothing()
	{
		_form.Seed();

		_form.MinimumText = Text(20.0);
		_form.Commit();

		_form.ValidationMessage.Should().Be(Resources.AxisScaleMinimumBelowMaximum);
		_written.Should().BeEmpty();
	}

	[AvaloniaFact]
	public void TheAxisRuleShowsItsMessageAndKeepsTheTypedText()
	{
		_form.Seed();

		_form.MinimumText = Text(-1.0);
		_form.Commit();

		_form.ValidationMessage.Should().Be(NegativeRule);
		_form.MinimumText.Should().Be(Text(-1.0), "a refused pair stays in the field for the operator to correct");
		_written.Should().BeEmpty();
	}

	[AvaloniaTheory]
	[InlineData("1.5")]
	[InlineData("1,5")]
	public void APointOrACommaReadsAsTheDecimalSeparatorInRussian(string typed)
	{
		using var cultureScope = new CultureScope("ru-RU");
		_form.Seed();

		_form.MinimumText = typed;
		_form.Commit();

		_written.Should().Equal(new AxisRange(1.5, 20.0));
	}

	private static string Text(double value)
	{
		return value.ToString("0.###", CultureInfo.CurrentCulture);
	}
}
