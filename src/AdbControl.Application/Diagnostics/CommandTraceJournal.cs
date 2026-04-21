using System.Collections.ObjectModel;
using System.Collections.Specialized;
using AdbControl.Application.Common;

namespace AdbControl.Application.Diagnostics;

public sealed class CommandTraceJournal : ObservableObject
{
    private readonly ICommandTraceStore _store;
    private readonly int _maxEntriesInMemory;
    private int _unreadErrorCount;

    public CommandTraceJournal(ICommandTraceStore store, int maxEntriesInMemory = 200)
    {
        _store = store;
        _maxEntriesInMemory = maxEntriesInMemory;
        Entries.CollectionChanged += OnEntriesChanged;
    }

    public ObservableCollection<CommandTraceEntry> Entries { get; } = [];

    public int UnreadErrorCount
    {
        get => _unreadErrorCount;
        private set
        {
            if (!SetProperty(ref _unreadErrorCount, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HasUnreadErrors));
        }
    }

    public bool HasUnreadErrors => UnreadErrorCount > 0;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<CommandTraceEntry> entries;

        try
        {
            entries = await _store.ReadAllAsync(cancellationToken);
        }
        catch
        {
            entries = [];
        }

        Entries.Clear();

        foreach (var entry in entries.OrderByDescending(x => x.Timestamp))
        {
            Entries.Add(entry);
        }

        TrimInMemory();
        UnreadErrorCount = 0;
    }

    public async Task RecordAsync(CommandTraceEntry entry, CancellationToken cancellationToken = default)
    {
        Entries.Insert(0, entry);
        TrimInMemory();

        if (entry.IsError)
        {
            UnreadErrorCount++;
        }

        await _store.AppendAsync(entry, cancellationToken);
    }

    public async Task RemoveEntriesAsync(IReadOnlyCollection<Guid> entryIds, CancellationToken cancellationToken = default)
    {
        if (entryIds.Count == 0)
        {
            return;
        }

        var retainedEntries = Entries
            .Where(entry => !entryIds.Contains(entry.Id))
            .OrderByDescending(entry => entry.Timestamp)
            .ToArray();

        await _store.RewriteAsync(
            retainedEntries
                .OrderBy(entry => entry.Timestamp)
                .ToArray(),
            cancellationToken);

        Entries.Clear();
        foreach (var entry in retainedEntries)
        {
            Entries.Add(entry);
        }

        UnreadErrorCount = Math.Min(UnreadErrorCount, retainedEntries.Count(entry => entry.IsError));
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _store.RewriteAsync([], cancellationToken);
        Entries.Clear();
        UnreadErrorCount = 0;
    }

    public void MarkErrorsAsViewed()
    {
        UnreadErrorCount = 0;
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(Entries));
    }

    private void TrimInMemory()
    {
        while (Entries.Count > _maxEntriesInMemory)
        {
            Entries.RemoveAt(Entries.Count - 1);
        }
    }
}
