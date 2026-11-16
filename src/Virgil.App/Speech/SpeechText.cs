using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Virgil.App.Speech;

/// <summary>Affichage progressif attaché à un TextBlock ordinaire.</summary>
public static class SpeechText
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(SpeechText), new PropertyMetadata(string.Empty, OnTextChanged));
    public static readonly DependencyProperty ClockProperty = DependencyProperty.RegisterAttached(
        "Clock", typeof(TimeProvider), typeof(SpeechText), new PropertyMetadata(TimeProvider.System));
    private static readonly DependencyProperty SessionProperty = DependencyProperty.RegisterAttached(
        "Session", typeof(Session), typeof(SpeechText));
    public static void SetText(DependencyObject owner, string value) => owner.SetValue(TextProperty, value);
    public static string GetText(DependencyObject owner) => (string)owner.GetValue(TextProperty);
    public static void SetClock(DependencyObject owner, TimeProvider value) => owner.SetValue(ClockProperty, value);
    public static TimeProvider GetClock(DependencyObject owner) => (TimeProvider)owner.GetValue(ClockProperty);
    public static SpeechReveal GetReveal(DependencyObject owner) => GetSession((TextBlock)owner).Model;
    private static Session GetSession(TextBlock block)
    {
        if (block.GetValue(SessionProperty) is Session session) return session;
        session = new Session(block); block.SetValue(SessionProperty, session); return session;
    }
    private static void OnTextChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args)
    {
        if (owner is TextBlock block) Begin(block, SystemParameters.ClientAreaAnimation);
    }
    private static void Begin(TextBlock block, bool animationsEnabled)
    {
        var session = GetSession(block);
        session.Model.Complete();
        AutomationProperties.SetName(block, GetText(block));
        session.Model.Start(GetText(block), animationsEnabled);
        if (block.IsLoaded && !session.Model.IsComplete) session.Timer.Start();
    }
    private sealed class Session
    {
        private readonly TextBlock _block;
        public SpeechReveal Model { get; }
        public DispatcherTimer Timer { get; }
        public Session(TextBlock block)
        {
            _block = block; Model = new SpeechReveal(GetClock(block));
            Timer = new DispatcherTimer(DispatcherPriority.Background, block.Dispatcher) { Interval = TimeSpan.FromMilliseconds(40) };
            Timer.Tick += (_, _) => Model.Tick();
            Model.Changed += () => { _block.Text = Model.Text; if (Model.IsComplete) Timer.Stop(); };
            block.MouseLeftButtonDown += Click;
            block.Loaded += Loaded;
            block.Unloaded += Unloaded;
        }
        private void Click(object sender, MouseButtonEventArgs args) { Model.Complete(); args.Handled = true; }
        private void Loaded(object sender, RoutedEventArgs args)
        {
            SystemParameters.StaticPropertyChanged += PreferenceChanged;
            if (!SystemParameters.ClientAreaAnimation) Model.Complete();
            else if (!Model.IsComplete) Timer.Start();
        }
        private void Unloaded(object sender, RoutedEventArgs args)
        {
            SystemParameters.StaticPropertyChanged -= PreferenceChanged;
            Timer.Stop(); Model.Complete();
        }
        private void PreferenceChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(SystemParameters.ClientAreaAnimation))
                _block.Dispatcher.InvokeAsync(() => { if (!SystemParameters.ClientAreaAnimation) Model.Complete(); });
        }
    }
}
