using AwesomeAssertions;

using FluentResults;

using SpectrumVisualizer.UI.Localization;
using SpectrumVisualizer.UI.Messages;

using Xunit;

namespace SpectrumVisualizer.Tests.Unit.UI.Messages;

[Trait("Component", "UI")]
[Trait("Area", "Messages")]
[Trait("Category", "Unit")]
public sealed class ErrorMessageMapperTests
{
	[Fact]
	public void MapKeepsTheExceptionTypeAndMessageInTheDetailOfAThrownError()
	{
		var exception = new InvalidOperationException("the frame was cut short");

		var view = ErrorMessageMapper.Map(new ExceptionalError(exception.Message, exception));

		view.Title.Should().Be(Resources.FailureThrownTitle);
		view.Detail.Should().Be(
			Resources.FormatFailureThrownDetail(nameof(InvalidOperationException), exception.Message));
		view.Detail.Should().Contain(exception.Message);
		view.Remedy.Should().Be(Resources.FailureThrownRemedy);
		view.Severity.Should().Be(MessageSeverity.Error);
	}

	[Fact]
	public void MapKeepsThePlainErrorMessageAsTheDetail()
	{
		var view = ErrorMessageMapper.Map(new Error("the port is taken"));

		view.Title.Should().Be(Resources.FailureUnknownTitle);
		view.Detail.Should().Be("the port is taken");
		view.Remedy.Should().Be(Resources.FailureUnknownRemedy);
		view.Severity.Should().Be(MessageSeverity.Error);
	}

	[Fact]
	public void MapGivesTwoThrownErrorsWithDifferentMessagesDifferentViews()
	{
		var first = new InvalidOperationException("the port is taken");
		var second = new InvalidOperationException("the device stopped answering");

		var firstView = ErrorMessageMapper.Map(new ExceptionalError(first.Message, first));
		var secondView = ErrorMessageMapper.Map(new ExceptionalError(second.Message, second));

		firstView.Should().NotBe(secondView);
	}
}
