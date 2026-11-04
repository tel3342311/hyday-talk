using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;

namespace HydayTalk;

/// Handler names written in the XAML files, e.g. Click="Save_Click". The loader rewrites those
/// attributes to these attached properties, then routes the events to the owner's methods.
public static class On
{
    public static readonly DependencyProperty ClickProperty = DependencyProperty.RegisterAttached("Click", typeof(string), typeof(On));
    public static readonly DependencyProperty SelectionChangedProperty = DependencyProperty.RegisterAttached("SelectionChanged", typeof(string), typeof(On));
    public static readonly DependencyProperty LostFocusProperty = DependencyProperty.RegisterAttached("LostFocus", typeof(string), typeof(On));
    public static void SetClick(DependencyObject d, string v) => d.SetValue(ClickProperty, v);
    public static string? GetClick(DependencyObject d) => (string?)d.GetValue(ClickProperty);
    public static void SetSelectionChanged(DependencyObject d, string v) => d.SetValue(SelectionChangedProperty, v);
    public static string? GetSelectionChanged(DependencyObject d) => (string?)d.GetValue(SelectionChangedProperty);
    public static void SetLostFocus(DependencyObject d, string v) => d.SetValue(LostFocusProperty, v);
    public static string? GetLostFocus(DependencyObject d) => (string?)d.GetValue(LostFocusProperty);
}

/// Loads the embedded XAML files at runtime instead of compiling them to BAML, so the project
/// needs no WPF markup compiler. Named elements are assigned to the owner's fields of the same
/// name, and event attributes call the owner's methods of that name.
internal static class Xaml
{
    private static readonly Regex EventAttribute = new(@"\b(Click|SelectionChanged|LostFocus)=""(\w+)""");

    public static string Source(string name)
    {
        using var stream = typeof(Xaml).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("Missing XAML resource " + name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static T Parse<T>(string name) where T : class => (T)XamlReader.Parse(Source(name));

    public static T Load<T>(object owner, string name) where T : FrameworkElement
    {
        var text = EventAttribute.Replace(Source(name), "h:On.$1=\"$2\"");
        // Declare the handler namespace on the root element.
        var rootEnd = Regex.Match(text, @"<\w+").Index + Regex.Match(text, @"<\w+").Length;
        text = text.Insert(rootEnd, " xmlns:h=\"clr-namespace:HydayTalk;assembly=HydayTalk\"");
        var root = (T)XamlReader.Parse(text);
        foreach (var field in owner.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            if (typeof(DependencyObject).IsAssignableFrom(field.FieldType) && root.FindName(field.Name) is { } element
                && field.FieldType.IsInstanceOfType(element))
                field.SetValue(owner, element);
        Route(owner, root, ButtonBase.ClickEvent, On.ClickProperty);
        Route(owner, root, Selector.SelectionChangedEvent, On.SelectionChangedProperty);
        Route(owner, root, UIElement.LostFocusEvent, On.LostFocusProperty);
        return root;
    }

    private static void Route(object owner, UIElement root, RoutedEvent routed, DependencyProperty property)
    {
        root.AddHandler(routed, new RoutedEventHandler((_, e) =>
        {
            var source = e.Source as DependencyObject;
            var handler = source?.GetValue(property) as string;
            if (handler == null && e.OriginalSource is DependencyObject original) { source = original; handler = original.GetValue(property) as string; }
            if (handler == null) return;
            var method = owner.GetType().GetMethod(handler, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                ?? throw new MissingMethodException(owner.GetType().Name, handler);
            method.Invoke(owner, [source, e]);
        }), handledEventsToo: true);
    }
}
