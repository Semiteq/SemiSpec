using System.Reactive;
using System.Reactive.Disposables;

using Avalonia.Controls;

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

	public void Hide()
	{
		_flyout.Hide();
	}
}
