using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AdbControl.Shell.Controls;

/// <summary>
/// Ввод нескольких условий плашками. Разделителя нет намеренно: любой символ-разделитель
/// может оказаться внутри самого условия и потребовал бы экранирования.
/// Enter добавляет плашку, крестик или Backspace в пустом поле убирают.
/// </summary>
public partial class TagInput : UserControl
{
    public static readonly DependencyProperty ItemsProperty =
        DependencyProperty.Register(
            nameof(Items),
            typeof(ObservableCollection<string>),
            typeof(TagInput),
            new PropertyMetadata(null));

    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.Register(
            nameof(Placeholder),
            typeof(string),
            typeof(TagInput),
            new PropertyMetadata(string.Empty));

    private static readonly DependencyPropertyKey HasInputTextPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(HasInputText),
            typeof(bool),
            typeof(TagInput),
            new PropertyMetadata(false));

    public static readonly DependencyProperty HasInputTextProperty = HasInputTextPropertyKey.DependencyProperty;

    public TagInput()
    {
        InitializeComponent();
    }

    public ObservableCollection<string>? Items
    {
        get => (ObservableCollection<string>?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public bool HasInputText
    {
        get => (bool)GetValue(HasInputTextProperty);
        private set => SetValue(HasInputTextPropertyKey, value);
    }

    private void OnInputPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                CommitInput();
                e.Handled = true;
                break;

            case Key.Back when InputBox.Text.Length == 0:
                RemoveLast();
                e.Handled = true;
                break;
        }
    }

    private void OnInputTextChanged(object sender, TextChangedEventArgs e)
    {
        HasInputText = InputBox.Text.Length > 0;
    }

    private void OnSurfaceMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Щелчок по свободному месту рамки ставит курсор в поле ввода.
        InputBox.Focus();
    }

    private void OnRemoveTagClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: string tag })
        {
            Items?.Remove(tag);
        }
    }

    private void CommitInput()
    {
        // Условие берётся дословно, без обрезки пробелов: " D " и "D" — разные условия,
        // и пробел здесь значащий символ, а не оформление.
        var text = InputBox.Text;
        if (text.Length == 0 || Items is null)
        {
            return;
        }

        if (!Items.Contains(text, StringComparer.OrdinalIgnoreCase))
        {
            Items.Add(text);
        }

        InputBox.Clear();
    }

    private void RemoveLast()
    {
        if (Items is { Count: > 0 })
        {
            Items.RemoveAt(Items.Count - 1);
        }
    }
}
