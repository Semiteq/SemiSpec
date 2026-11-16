using System.Reactive.Linq;

using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using SemiSpec.UI.Messages;
using SemiSpec.UI.Settings;

using Xunit;

namespace SemiSpec.Tests.Unit.UI.Settings;

[Trait("Component", "UI")]
[Trait("Area", "Settings")]
[Trait("Category", "Unit")]
public sealed class DisplaySettingsViewModelTests : IDisposable
{
	private readonly TempFolder _root = new();
	private readonly MessagePanelViewModel _messagePanel = new(TimeProvider.System);

	public void Dispose()
	{
		_messagePanel.Dispose();
		_root.Dispose();
	}

	[Fact]
	public void AMissingFileStartsWithTheQFactorHiddenAndDots()
	{
		using var display = Create(_root.FileIn(SettingsFile.FileName));

		display.ShowQFactor.Should().BeFalse();
		display.TrendAsLines.Should().BeFalse();
		_messagePanel.HasEntries.Should().BeFalse();
	}

	[Fact]
	public async Task EachToggleFlipsItsChoiceAndSavesTheFile()
	{
		var path = _root.FileIn(SettingsFile.FileName);
		using var display = Create(path);

		await display.ToggleQFactorCommand.Execute();
		await display.ToggleTrendAsLinesCommand.Execute();

		display.ShowQFactor.Should().BeTrue();
		display.TrendAsLines.Should().BeTrue();
		new SettingsFile(path).Load().Value.Should().Be(new DisplaySettings(ShowQFactor: true, TrendAsLines: true));
	}

	[Fact]
	public async Task AToggleRaisesOnlyItsOwnProperty()
	{
		using var display = Create(_root.FileIn(SettingsFile.FileName));
		var raised = new List<string?>();
		display.PropertyChanged += (_, args) => raised.Add(args.PropertyName);

		await display.ToggleQFactorCommand.Execute();

		raised.Should().Equal(nameof(DisplaySettingsViewModel.ShowQFactor));
	}

	[Fact]
	public void AStoredChoiceLoadsAtStart()
	{
		var path = _root.FileIn(SettingsFile.FileName);
		new SettingsFile(path).Save(new DisplaySettings(ShowQFactor: true, TrendAsLines: false));

		using var display = Create(path);

		display.ShowQFactor.Should().BeTrue();
		display.TrendAsLines.Should().BeFalse();
	}

	[Fact]
	public async Task AMalformedFileStartsWithTheDefaultsAWarningAndIsRewrittenByTheNextToggle()
	{
		var path = _root.FileIn(SettingsFile.FileName);
		File.WriteAllText(path, "show_q_factor: [");

		using var display = Create(path);

		display.ShowQFactor.Should().BeFalse();
		_messagePanel.NewestEntry!.View.Severity.Should().Be(MessageSeverity.Warning);

		await display.ToggleTrendAsLinesCommand.Execute();

		new SettingsFile(path).Load().Value.Should().Be(new DisplaySettings(ShowQFactor: false, TrendAsLines: true));
	}

	[Fact]
	public async Task AFailedSaveKeepsTheChoiceForTheRunAndWarns()
	{
		var blocker = _root.FileIn("blocker");
		File.WriteAllText(blocker, string.Empty);
		using var display = Create(Path.Combine(blocker, SettingsFile.FileName));

		await display.ToggleQFactorCommand.Execute();

		display.ShowQFactor.Should().BeTrue();
		_messagePanel.NewestEntry!.View.Severity.Should().Be(MessageSeverity.Warning);
	}

	private DisplaySettingsViewModel Create(string path)
	{
		return new DisplaySettingsViewModel(
			new SettingsFile(path), _messagePanel, NullLogger<DisplaySettingsViewModel>.Instance);
	}
}
