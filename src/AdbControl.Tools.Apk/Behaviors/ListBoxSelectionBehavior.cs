using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace AdbControl.Tools.Apk.Behaviors;

public static class ListBoxSelectionBehavior
{
    public static readonly DependencyProperty SelectedItemsProperty =
        DependencyProperty.RegisterAttached(
            "SelectedItems",
            typeof(IList),
            typeof(ListBoxSelectionBehavior),
            new PropertyMetadata(null, OnSelectedItemsChanged));

    private static readonly DependencyProperty IsUpdatingProperty =
        DependencyProperty.RegisterAttached(
            "IsUpdating",
            typeof(bool),
            typeof(ListBoxSelectionBehavior),
            new PropertyMetadata(false));

    public static IList? GetSelectedItems(DependencyObject dependencyObject)
    {
        return (IList?)dependencyObject.GetValue(SelectedItemsProperty);
    }

    public static void SetSelectedItems(DependencyObject dependencyObject, IList? value)
    {
        dependencyObject.SetValue(SelectedItemsProperty, value);
    }

    private static bool GetIsUpdating(DependencyObject dependencyObject)
    {
        return (bool)dependencyObject.GetValue(IsUpdatingProperty);
    }

    private static void SetIsUpdating(DependencyObject dependencyObject, bool value)
    {
        dependencyObject.SetValue(IsUpdatingProperty, value);
    }

    private static void OnSelectedItemsChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not ListBox listBox)
        {
            return;
        }

        listBox.SelectionChanged -= OnListBoxSelectionChanged;
        listBox.SelectionChanged += OnListBoxSelectionChanged;
        listBox.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
        listBox.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
    }

    private static void OnListBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox listBox || GetIsUpdating(listBox))
        {
            return;
        }

        var selectedItems = GetSelectedItems(listBox);
        if (selectedItems is null)
        {
            return;
        }

        SetIsUpdating(listBox, true);
        try
        {
            selectedItems.Clear();
            foreach (var item in listBox.SelectedItems)
            {
                selectedItems.Add(item);
            }
        }
        finally
        {
            SetIsUpdating(listBox, false);
        }
    }

    private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox listBox ||
            listBox.SelectionMode == SelectionMode.Single ||
            Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            return;
        }

        if (IsInteractiveElement(e.OriginalSource as DependencyObject))
        {
            return;
        }

        var listBoxItem = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (listBoxItem is null)
        {
            return;
        }

        var item = listBox.ItemContainerGenerator.ItemFromContainer(listBoxItem);
        if (item == DependencyProperty.UnsetValue)
        {
            return;
        }

        if (listBoxItem.IsSelected)
        {
            if (Keyboard.Modifiers == ModifierKeys.None)
            {
                listBox.SelectedItems.Remove(item);
                listBox.Focus();
                e.Handled = true;
            }

            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.None)
        {
            listBox.SelectedItems.Add(item);
            listBox.Focus();
            e.Handled = true;
        }
    }

    private static T? FindAncestor<T>(DependencyObject? current)
        where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = current switch
            {
                Visual or Visual3D => VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current),
                FrameworkContentElement frameworkContentElement => frameworkContentElement.Parent ?? LogicalTreeHelper.GetParent(frameworkContentElement),
                ContentElement contentElement => ContentOperations.GetParent(contentElement) ?? LogicalTreeHelper.GetParent(contentElement),
                _ => LogicalTreeHelper.GetParent(current)
            };
        }

        return null;
    }

    private static bool IsInteractiveElement(DependencyObject? current)
    {
        return FindAncestor<ButtonBase>(current) is not null ||
               FindAncestor<TextBoxBase>(current) is not null ||
               FindAncestor<ScrollBar>(current) is not null;
    }
}
