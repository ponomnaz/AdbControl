using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace AdbControl.Tools.CommandLog.Views;

public partial class CommandLogToolView : UserControl
{
    public CommandLogToolView()
    {
        InitializeComponent();
    }

    private void OnRootPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        ClearSelectionsIfNeeded(e.OriginalSource as DependencyObject);
    }

    private void ClearSelectionsIfNeeded(DependencyObject? source)
    {
        if (source is null || ShouldKeepSelection(source))
        {
            return;
        }

        foreach (var listBox in FindVisualChildren<ListBox>(this))
        {
            if (listBox.SelectedItems.Count > 0)
            {
                listBox.UnselectAll();
            }
        }
    }

    private bool ShouldKeepSelection(DependencyObject source)
    {
        for (var current = source; current is not null && !ReferenceEquals(current, this); current = GetParentElement(current))
        {
            if (current is ListBoxItem or ScrollBar or ButtonBase or TextBoxBase or Selector or TabItem)
            {
                return true;
            }
        }

        return false;
    }

    private static DependencyObject? GetParentElement(DependencyObject current)
    {
        return current switch
        {
            Visual or Visual3D => VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current),
            FrameworkContentElement frameworkContentElement => frameworkContentElement.Parent ?? LogicalTreeHelper.GetParent(frameworkContentElement),
            ContentElement contentElement => ContentOperations.GetParent(contentElement) ?? LogicalTreeHelper.GetParent(contentElement),
            _ => LogicalTreeHelper.GetParent(current)
        };
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        var childrenCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childrenCount; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T target)
            {
                yield return target;
            }

            foreach (var nested in FindVisualChildren<T>(child))
            {
                yield return nested;
            }
        }
    }
}
