using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace WindDown.App.Controls;
public sealed partial class TimeDial : UserControl
{
    private int value;
    private bool updating;
    private double wheel, drag;
    public int Minimum { get; set; }
    public int Maximum { get; set; } = 59;
    public bool IsValid => int.TryParse(Editor.Text, out var n) && n >= Minimum && n <= Maximum;
    public int Value { get => IsValid ? int.Parse(Editor.Text) : value; set { this.value = value; Refresh(); } }
    public event EventHandler? Changed;
    public TimeDial() { InitializeComponent(); Refresh(); }
    public void Configure(string label, int minimum, int maximum, int initial)
    {
        Caption.Text = label; Minimum = minimum; Maximum = maximum;
        AutomationProperties.SetName(Editor, label);
        AutomationProperties.SetHelpText(Editor, $"{minimum} to {maximum}. Type a value, use up and down arrows, scroll, or swipe. Enter confirms.");
        AutomationProperties.SetName(Previous, $"Decrease {label.ToLowerInvariant()}");
        AutomationProperties.SetName(Next, $"Increase {label.ToLowerInvariant()}");
        Value = initial;
    }
    public void SetRange(int minimum, int maximum)
    {
        Minimum = minimum; Maximum = maximum;
        AutomationProperties.SetHelpText(Editor, $"{minimum} to {maximum}. Type a value, use up and down arrows, scroll, or swipe. Enter confirms.");
        value = Math.Clamp(Value, minimum, maximum);
        Refresh();
    }
    private int Wrap(int n) => n < Minimum ? Maximum : n > Maximum ? Minimum : n;
    private void Refresh()
    {
        if (Editor == null) return;
        updating = true; Editor.Text = value.ToString("00"); updating = false;
        Previous.Content = Wrap(value - 1).ToString("00"); Next.Content = Wrap(value + 1).ToString("00");
    }
    private void Step(int amount)
    {
        value = Wrap(Value + amount); Refresh(); Changed?.Invoke(this, EventArgs.Empty);
        if (Editor.FocusState != FocusState.Unfocused) Editor.SelectAll();
    }
    private void OnPrevious(object sender, RoutedEventArgs e) => Step(-1);
    private void OnNext(object sender, RoutedEventArgs e) => Step(1);
    private void BeforeTextChanging(TextBox sender, TextBoxBeforeTextChangingEventArgs args) => args.Cancel = args.NewText.Any(c => c < '0' || c > '9');
    private void TextChanged(object sender, TextChangedEventArgs e)
    {
        if (updating || Editor == null || Previous == null || Next == null) return;
        if (IsValid) { value = int.Parse(Editor.Text); Previous.Content = Wrap(value - 1).ToString("00"); Next.Content = Wrap(value + 1).ToString("00"); }
        Changed?.Invoke(this, EventArgs.Empty);
    }
    private void OnFocus(object sender, RoutedEventArgs e) => Editor.SelectAll();
    private void OnBlur(object sender, RoutedEventArgs e) { if (IsValid) Refresh(); }
    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Up) { Step(1); e.Handled = true; }
        else if (e.Key == VirtualKey.Down) { Step(-1); e.Handled = true; }
        else if (e.Key == VirtualKey.Enter && IsValid) { Refresh(); Editor.SelectAll(); e.Handled = true; }
    }
    private void OnWheel(object sender, PointerRoutedEventArgs e)
    {
        wheel += e.GetCurrentPoint(DialSurface).Properties.MouseWheelDelta;
        while (Math.Abs(wheel) >= 120) { int direction = Math.Sign(wheel); Step(direction); wheel -= direction * 120; }
        e.Handled = true;
    }
    private void OnManipulation(object sender, ManipulationDeltaRoutedEventArgs e)
    {
        drag += e.Delta.Translation.Y;
        while (Math.Abs(drag) >= 26) { int direction = Math.Sign(drag); Step(-direction); drag -= direction * 26; }
        e.Handled = true;
    }
}

