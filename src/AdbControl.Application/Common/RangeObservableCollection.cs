using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace AdbControl.Application.Common;

/// <summary>
/// Коллекция с полной заменой содержимого за одно уведомление. Обычные Clear и Add
/// порождают событие на каждый элемент: при перефильтровке десятков тысяч строк
/// это десятки тысяч обходов привязок.
/// </summary>
public sealed class RangeObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> items)
    {
        CheckReentrancy();

        Items.Clear();
        foreach (var item in items)
        {
            Items.Add(item);
        }

        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
