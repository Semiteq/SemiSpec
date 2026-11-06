using AwesomeAssertions;

using FluentResults;

using SemiSpec.UI.Link;
using SemiSpec.UI.Localization;
using SemiSpec.UI.Messages;
using SemiSpec.UI.Store;
using SemiSpec.UI.Trend;

using Xunit;

namespace SemiSpec.Tests.Unit.UI.Messages;

[Trait("Component", "UI")]
[Trait("Area", "Messages")]
[Trait("Category", "Unit")]
public sealed class ErrorMessageMapperTests
{
	private const string DistinctPort = "COM987";

	public static TheoryData<LinkError, string, string, string, MessageSeverity> LinkErrors => new()
	{
		{
			new PortBusyError(DistinctPort),
			Resources.PortBusyTitle, Resources.PortBusyDetail, Resources.PortBusyRemedy, MessageSeverity.Error
		},
		{
			new PortNotFoundError(DistinctPort),
			Resources.PortNotFoundTitle, Resources.PortNotFoundDetail, Resources.PortNotFoundRemedy,
			MessageSeverity.Error
		},
		{
			new PortLostError(DistinctPort),
			Resources.PortLostTitle, Resources.PortLostDetail, Resources.PortLostRemedy, MessageSeverity.Warning
		},
		{
			new BoardNotRespondingError(DistinctPort),
			Resources.BoardNotRespondingTitle, Resources.BoardNotRespondingDetail, Resources.BoardNotRespondingRemedy,
			MessageSeverity.Warning
		},
		{
			new UnrecognisedDataError(DistinctPort),
			Resources.UnrecognisedDataTitle, Resources.UnrecognisedDataDetail, Resources.UnrecognisedDataRemedy,
			MessageSeverity.Warning
		}
	};

	[Fact]
	public void MapKeepsTheExceptionTypeAndMessageInTheDetailOfAThrownError()
	{
		var exception = new InvalidOperationException("the frame was cut short");

		var view = ErrorMessageMapper.Map(new ExceptionalError(exception.Message, exception));

		view.Title.Should().Be(Resources.FailureThrownTitle);
		view.Detail.Should().Be(
			Resources.FormatFailureThrownDetail(nameof(InvalidOperationException), exception.Message));
		view.Detail.Should().Contain(nameof(InvalidOperationException)).And.Contain(exception.Message);
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

	[Theory]
	[MemberData(nameof(LinkErrors))]
	public void MapGivesEachLinkErrorItsOwnTextAndSeverityWithoutThePortName(
		LinkError error,
		string title,
		string detail,
		string remedy,
		MessageSeverity severity)
	{
		var view = ErrorMessageMapper.Map(error);

		view.Should().Be(new MessageView(title, detail, remedy, severity));
		view.Title.Should().NotContain(DistinctPort);
		view.Detail.Should().NotContain(DistinctPort);
		view.Remedy.Should().NotContain(DistinctPort);
	}

	[Theory]
	[InlineData("en", "firmware")]
	[InlineData("ru", "\u043F\u0440\u043E\u0448\u0438\u0432\u043A")]
	public void TheRemediesOfASilentAndAForeignBoardNameTheBoardFirmware(string culture, string firmware)
	{
		using var cultureScope = new CultureScope(culture);

		ErrorMessageMapper.Map(new BoardNotRespondingError(DistinctPort)).Remedy.Should().Contain(firmware);
		ErrorMessageMapper.Map(new UnrecognisedDataError(DistinctPort)).Remedy.Should().Contain(firmware);
	}

	[Fact]
	public void MapGivesAFailedPortListingItsOwnTextAtWarning()
	{
		var view = ErrorMessageMapper.Map(new PortListError().CausedBy(new Error("the port registry key is gone")));

		view.Should().Be(new MessageView(
			Resources.PortListTitle,
			Resources.PortListDetail,
			Resources.PortListRemedy,
			MessageSeverity.Warning));
	}

	[Fact]
	public void MapGivesAFailedStoreOpenItsOwnTextAtErrorWithoutThePath()
	{
		const string StorePath = @"C:\Store987\spectra-987.db";

		var view = ErrorMessageMapper.Map(
			new StoreOpenError(StorePath).CausedBy(new IOException("the folder is a file")));

		view.Should().Be(new MessageView(
			Resources.StoreOpenTitle,
			Resources.StoreOpenDetail,
			Resources.StoreOpenRemedy,
			MessageSeverity.Error));
		view.Detail.Should().NotContain("987");
	}

	[Fact]
	public void MapGivesAFailedStoreWriteItsOwnTextAtWarning()
	{
		var view = ErrorMessageMapper.Map(new StoreWriteError().CausedBy(new IOException("the disk is full")));

		view.Should().Be(new MessageView(
			Resources.StoreWriteTitle,
			Resources.StoreWriteDetail,
			Resources.StoreWriteRemedy,
			MessageSeverity.Warning));
	}

	[Fact]
	public void MapGivesAStoppedStoreItsOwnTextAtError()
	{
		var view = ErrorMessageMapper.Map(
			new StoreStoppedError().CausedBy(new InvalidOperationException("the clock failed")));

		view.Should().Be(new MessageView(
			Resources.StoreStoppedTitle,
			Resources.StoreStoppedDetail,
			Resources.StoreStoppedRemedy,
			MessageSeverity.Error));
	}

	[Fact]
	public void MapGivesAFailedTrendHistoryReadItsOwnTextAtWarningWithNoRemedy()
	{
		var view = ErrorMessageMapper.Map(new TrendHistoryError().CausedBy(new Error("the store file is locked")));

		view.Should().Be(new MessageView(
			Resources.TrendHistoryTitle,
			Resources.TrendHistoryDetail,
			string.Empty,
			MessageSeverity.Warning));
	}

	[Fact]
	public void MapGivesAFailedTrendMomentReadItsOwnTextAtWarning()
	{
		var view = ErrorMessageMapper.Map(new TrendMomentError().CausedBy(new Error("the store file is locked")));

		view.Should().Be(new MessageView(
			Resources.TrendMomentTitle,
			Resources.TrendMomentDetail,
			Resources.TrendMomentRemedy,
			MessageSeverity.Warning));
	}

	[Fact]
	public void MapGivesTwoPortLostErrorsOnDifferentPortsEqualViews()
	{
		var first = ErrorMessageMapper.Map(new PortLostError("COM3"));
		var second = ErrorMessageMapper.Map(new PortLostError("COM12"));

		first.Should().Be(second);
	}
}
