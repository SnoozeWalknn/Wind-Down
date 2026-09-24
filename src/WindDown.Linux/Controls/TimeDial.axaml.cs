using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace WindDown.App.Controls;
public partial class TimeDial : UserControl
{
    private int value;
    private bool updating;
    private double wheel, drag;
    private Avalonia.Point? touchStart;
    public int Minimum { get; set; }
    public int Maximum { get; set; } = 59;
    public bool IsValid => int.TryParse(Editor.Text, out var n) && n >= Minimum && n <= Maximum;
    public int Value { get => IsValid ? int.Parse(Editor.Text!) : value; set { this.value = value; Refresh(); } }
    public event EventHandler? Changed;
    public TimeDial()
    {
        InitializeComponent();
        Editor.TextChanged += TextChanged;
        Editor.GotFocus += (_, _) => Dispatcher.UIThread.Post(Editor.SelectAll);
        Editor.LostFocus += (_, _) => { if (IsValid) Refresh(); };
        Editor.AddHandler(TextInputEvent, (_, e) => { if (e.Text?.Any(c => c < '0' || c > '9') == true) e.Handled = true; }, RoutingStrategies.Tunnel);
        Editor.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        DialSurface.PointerWheelChanged += OnWheel;
        DialSurface.AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        DialSurface.AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel);
        DialSurface.AddHandler(PointerReleasedEvent, (_, _) => touchStart = null, RoutingStrategies.Tunnel);
        Refresh();
    }
    private void SetHelp(int minimum, int maximum) =>
        AutomationProperties.SetHelpText(Editor, $"{minimum} to {maximum}. Type a value, use up and down arrows, scroll, or swipe. Enter confirms.");
    public void Configure(string label, int minimum, int maximum, int initial)
    {
        Caption.Text = label; Minimum = minimum; Maximum = maximum;
        AutomationProperties.SetName(Editor, label);
        SetHelp(minimum, maximum);
        AutomationProperties.SetName(Previous, $"Decrease {label.ToLowerInvariant()}");
        AutomationProperties.SetName(Next, $"Increase {label.ToLowerInvariant()}");
        Value = initial;
    }
    public void SetRange(int minimum, int maximum)
    {
        Minimum = minimum; Maximum = maximum;
        SetHelp(minimum, maximum);
        value = Math.Clamp(Value, minimum, maximum);
        Refresh();
    }
    private int Wrap(int n) => n < Minimum ? Maximum : n > Maximum ? Minimum : n;
    private void Refresh()
    {
        updating = true; Editor.Text = value.ToString("00"); updating = false;
        Previous.Content = Wrap(value - 1).ToString("00"); Next.Content = Wrap(value + 1).ToString("00");
    }
    private void Step(int amount)
    {
        value = Wrap(Value + amount); Refresh(); Changed?.Invoke(this, EventArgs.Empty);
        if (Editor.IsFocused) Editor.SelectAll();
    }
    private void OnPrevious(object? sender, RoutedEventArgs e) => Step(-1);
    private void OnNext(object? sender, RoutedEventArgs e) => Step(1);
    private void TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (updating) return;
        var text = Editor.Text ?? "";
        if (text.Any(c => c < '0' || c > '9')) { Editor.Text = new string(text.Where(char.IsAsciiDigit).ToArray()); return; }
        if (IsValid) { value = int.Parse(text); Previous.Content = Wrap(value - 1).ToString("00"); Next.Content = Wrap(value + 1).ToString("00"); }
        Changed?.Invoke(this, EventArgs.Empty);
    }
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Up) { Step(1); e.Handled = true; }
        else if (e.Key == Key.Down) { Step(-1); e.Handled = true; }
        else if (e.Key == Key.Enter && IsValid) { Refresh(); Editor.SelectAll(); e.Handled = true; }
    }
    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        wheel += e.Delta.Y;
        while (Math.Abs(wheel) >= 1) { int direction = Math.Sign(wheel); Step(direction); wheel -= direction; }
        e.Handled = true;
    }
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Pointer.Type != PointerType.Touch) return;
        touchStart = e.GetPosition(DialSurface); drag = 0;
    }
    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (touchStart is not { } start || e.Pointer.Type != PointerType.Touch) return;
        var point = e.GetPosition(DialSurface);
        drag += point.Y - start.Y; touchStart = point;
        while (Math.Abs(drag) >= 26) { int direction = Math.Sign(drag); Step(-direction); drag -= direction * 26; }
        e.Handled = true;
    }
}
