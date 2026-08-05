using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace AdbControl.Shell.Behaviors;

/// <summary>
/// Поиск по дереву элементов. Раньше жил четырьмя копиями в разных вкладках, и правки
/// в одной до остальных не доходили.
/// </summary>
public static class VisualTreeSearch
{
    /// <summary>
    /// Ближайший предок нужного типа. Идём по визуальному дереву, а где его нет —
    /// по логическому: источник события бывает содержимым, а не элементом.
    /// </summary>
    public static T? FindAncestor<T>(DependencyObject? current)
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

    /// <summary>Первый потомок нужного типа в глубину.</summary>
    public static T? FindDescendant<T>(DependencyObject? root)
        where T : DependencyObject
    {
        if (root is null)
        {
            return null;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);

        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);

            if (child is T match)
            {
                return match;
            }

            if (FindDescendant<T>(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }
}
