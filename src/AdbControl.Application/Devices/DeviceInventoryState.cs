using System.Collections.ObjectModel;
using System.Collections.Specialized;
using AdbControl.Application.Common;
using AdbControl.Core.Devices;

namespace AdbControl.Application.Devices;

public sealed class DeviceInventoryState : ObservableObject
{
    public DeviceInventoryState()
    {
        KnownDevices.CollectionChanged += OnCollectionChanged;
        SelectedDevices.CollectionChanged += OnCollectionChanged;
    }

    public ObservableCollection<TvDeviceProfile> KnownDevices { get; } = [];

    public ObservableCollection<TvDeviceProfile> SelectedDevices { get; } = [];

    public bool HasKnownDevices => KnownDevices.Count > 0;

    public bool HasSelection => SelectedDevices.Count > 0;

    public string SelectionSummary =>
        SelectedDevices.Count switch
        {
            0 => "Устройства не выбраны",
            1 => SelectedDevices[0].DisplayName,
            _ => $"Выбрано устройств: {SelectedDevices.Count}"
        };

    public void ReplaceKnownDevices(IEnumerable<TvDeviceProfile> devices)
    {
        KnownDevices.Clear();
        foreach (var device in devices)
        {
            KnownDevices.Add(device);
        }
    }

    /// <summary>
    /// Добавляет или обновляет устройства. Запись с теми же значениями не переприсваивается:
    /// фоновая синхронизация идёт каждые несколько секунд, а каждое присваивание — это
    /// событие Replace, из-за которого списки в UI пересобираются и теряют выделение.
    /// </summary>
    public void UpsertKnownDevices(IEnumerable<TvDeviceProfile> devices)
    {
        foreach (var device in devices)
        {
            var knownIndex = FindDeviceIndex(KnownDevices, device);
            if (knownIndex < 0)
            {
                KnownDevices.Add(device);
            }
            else if (KnownDevices[knownIndex] != device)
            {
                KnownDevices[knownIndex] = device;
            }

            var selectedIndex = FindDeviceIndex(SelectedDevices, device);
            if (selectedIndex >= 0 && SelectedDevices[selectedIndex] != device)
            {
                SelectedDevices[selectedIndex] = device;
            }
        }
    }

    public void ReplaceSelection(IEnumerable<TvDeviceProfile> devices)
    {
        SelectedDevices.Clear();
        foreach (var device in devices)
        {
            SelectedDevices.Add(device);
        }
    }

    public void ClearSelection()
    {
        SelectedDevices.Clear();
    }

    public void RemoveKnownDevicesByEndpoint(IEnumerable<string> endpoints)
    {
        var endpointSet = endpoints
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (endpointSet.Count == 0)
        {
            return;
        }

        RemoveMatchingDevices(KnownDevices, endpointSet);
        RemoveMatchingDevices(SelectedDevices, endpointSet);
    }

    /// <summary>
    /// Убирает устройства, которых больше нет в <c>adb devices</c>. Сверка идёт по
    /// <see cref="TvDeviceProfile.Id"/>, поэтому одинаково работает и для сетевых
    /// адресов, и для USB-серийников.
    /// </summary>
    public void RemoveDevicesMissingFrom(IEnumerable<string> deviceIds)
    {
        var idSet = deviceIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        RemoveAdbDevicesMissing(KnownDevices, idSet);
        RemoveAdbDevicesMissing(SelectedDevices, idSet);
    }

    private static void RemoveAdbDevicesMissing(IList<TvDeviceProfile> devices, HashSet<string> idSet)
    {
        for (var index = devices.Count - 1; index >= 0; index--)
        {
            var device = devices[index];

            // Устройства неизвестного происхождения не трогаем: их не adb добавлял.
            if (device.PreferredConnection is not (DeviceConnectionKind.Network or DeviceConnectionKind.Usb))
            {
                continue;
            }

            if (!idSet.Contains(device.Id))
            {
                devices.RemoveAt(index);
            }
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasKnownDevices));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectionSummary));
    }

    private static int FindDeviceIndex(IList<TvDeviceProfile> devices, TvDeviceProfile candidate)
    {
        for (var index = 0; index < devices.Count; index++)
        {
            if (IsSameDevice(devices[index], candidate))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsSameDevice(TvDeviceProfile left, TvDeviceProfile right)
    {
        if (string.Equals(left.Id, right.Id, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(left.NetworkEndpoint) &&
               string.Equals(left.NetworkEndpoint, right.NetworkEndpoint, StringComparison.OrdinalIgnoreCase);
    }

    private static void RemoveMatchingDevices(IList<TvDeviceProfile> devices, HashSet<string> endpointSet)
    {
        for (var index = devices.Count - 1; index >= 0; index--)
        {
            var endpoint = devices[index].NetworkEndpoint;
            if (!string.IsNullOrWhiteSpace(endpoint) && endpointSet.Contains(endpoint))
            {
                devices.RemoveAt(index);
            }
        }
    }
}
