using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace AdbControl.Tools.Devices.Behaviors;

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
}
