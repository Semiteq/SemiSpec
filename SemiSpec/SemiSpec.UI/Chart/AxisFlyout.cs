using System.Reactive;
using System.Reactive.Disposables;

using Avalonia.Controls;
using Avalonia.LogicalTree;

namespace SemiSpec.UI.Chart;

/// <summary>The form behind an axis flyout: seeded on open, committed on close unless Escape cancelled it.</summary>
public interface IAxisFlyoutForm
{
	IObservable<Unit> CloseRequests { get; }

	void Seed();

	void Close();
}

/// <summary>Opens an axis flyout at the pointer over the plot and runs its form's open and close steps.</summary>
internal sealed class AxisFlyout
{
	private readonly Control _target;
	private readonly Flyout _flyout;
	private IAxisFlyoutForm? _form;

	public AxisFlyout(Control target, Flyout flyout)
	{
		_target = target;
		_flyout = flyout;
		_flyout.Closed += (_, _) => _form?.Close();
	}

	/// <summary>
	/// Hides the flyouts when the window starts to close: Avalonia 12 throws when a closing window detaches a
	/// drop-down still open inside a flyout.
	/// </summary>
	public static IDisposable HideOnClosing(Window window, params AxisFlyout[] flyouts)
	{
		void OnClosing(object? sender, WindowClosingEventArgs eventArgs)
		{
			foreach (var flyout in flyouts)
			{
				flyout.Hide();
			}
		}

		window.Closing += OnClosing;
		return Disposable.Create(() => window.Closing -= OnClosing);
	}

	/// <summary>Binds the form of the current DataContext; disposing unbinds it.</summary>
	public IDisposable Attach(IAxisFlyoutForm form)
	{
		_form = form;
		var closeRequests = form.CloseRequests.Subscribe(_ => _flyout.Hide());
		return Disposable.Create(() =>
		{
			closeRequests.Dispose();
			_form = null;
		});
	}

	public void Open()
	{
		if (_form is null)
		{
			return;
		}

		_form.Seed();
		_flyout.ShowAt(_target, true);
	}

	/// <summary>Closes the drop-downs inside the flyout first, then the flyout.</summary>
	public void Hide()
	{
		if (_flyout.Content is ILogical content)
		{
			foreach (var nested in content.GetSelfAndLogicalDescendants())
			{
				switch (nested)
				{
					case CalendarDatePicker picker:
						picker.IsDropDownOpen = false;
						break;
					case DropDownButton button:
						button.Flyout?.Hide();
						break;
				}
			}
		}

		_flyout.Hide();
	}
}
