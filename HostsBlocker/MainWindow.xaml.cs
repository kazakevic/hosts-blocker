using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace HostsBlocker;

public partial class MainWindow : Window
{
    private static readonly string[] NewsPreset =
    [
        "news.ycombinator.com", "reddit.com", "cnn.com", "bbc.com",
        "nytimes.com", "theguardian.com", "foxnews.com",
    ];

    private static readonly Regex DomainPattern = new(
        @"^(?=.{1,253}$)([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,}$",
        RegexOptions.Compiled);

    private readonly ObservableCollection<BlockEntry> _entries = [];
    private bool _dirty;

    public MainWindow()
    {
        InitializeComponent();
        SiteList.ItemsSource = _entries;
        _entries.CollectionChanged += (_, _) => { MarkDirty(); UpdateEmptyState(); };
        Reload();
        _dirty = false;
        ApplyButton.IsEnabled = false;
        Status.Text = HostsFile.Path;
    }

    private void Reload()
    {
        _entries.Clear();
        try
        {
            foreach (var e in HostsFile.Load()) Track(e);
        }
        catch (Exception ex)
        {
            Status.Text = "Could not read hosts file: " + ex.Message;
        }
        UpdateEmptyState();
    }

    private void Track(BlockEntry entry)
    {
        entry.Changed += (_, _) => MarkDirty();
        _entries.Add(entry);
    }

    private void MarkDirty()
    {
        _dirty = true;
        ApplyButton.IsEnabled = true;
        Status.Text = "Unsaved changes";
    }

    private void UpdateEmptyState() =>
        EmptyState.Visibility = _entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Accepts pasted URLs as well as bare domains.</summary>
    private static string? Normalise(string input)
    {
        var s = input.Trim().ToLowerInvariant();
        if (s.Length == 0) return null;

        if (Uri.TryCreate(s.Contains("://") ? s : "http://" + s, UriKind.Absolute, out var uri))
            s = uri.Host;

        s = s.TrimEnd('.');
        return DomainPattern.IsMatch(s) ? s : null;
    }

    private void AddDomain(string raw)
    {
        var domain = Normalise(raw);
        if (domain is null)
        {
            Status.Text = $"\"{raw.Trim()}\" doesn't look like a domain.";
            return;
        }

        if (_entries.Any(e => e.Domain.Equals(domain, StringComparison.OrdinalIgnoreCase)))
        {
            Status.Text = $"{domain} is already on the list.";
            return;
        }

        Track(new BlockEntry { Domain = domain, IncludeWww = IncludeWww.IsChecked == true });
        DomainInput.Clear();
    }

    private void Add_Click(object sender, RoutedEventArgs e) => AddDomain(DomainInput.Text);

    private void DomainInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) AddDomain(DomainInput.Text);
    }

    private void DomainInput_TextChanged(object sender, TextChangedEventArgs e) =>
        Placeholder.Visibility = DomainInput.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void AddPreset_Click(object sender, RoutedEventArgs e)
    {
        foreach (var d in NewsPreset)
        {
            if (_entries.Any(x => x.Domain.Equals(d, StringComparison.OrdinalIgnoreCase))) continue;
            Track(new BlockEntry { Domain = d, IncludeWww = true });
        }
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is BlockEntry entry) _entries.Remove(entry);
    }

    private void Reload_Click(object sender, RoutedEventArgs e)
    {
        if (_dirty && MessageBox.Show(this, "Discard unsaved changes and re-read the hosts file?",
                "Hosts Blocker", MessageBoxButton.OKCancel, MessageBoxImage.Question)
            != MessageBoxResult.OK) return;

        Reload();
        _dirty = false;
        ApplyButton.IsEnabled = false;
        Status.Text = "Reloaded from disk.";
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            HostsFile.Save(_entries);
            HostsFile.FlushDns();
            _dirty = false;
            ApplyButton.IsEnabled = false;
            var blocked = _entries.Count(x => x.Enabled);
            Status.Text = $"Saved - {blocked} site{(blocked == 1 ? "" : "s")} blocked.";
        }
        catch (UnauthorizedAccessException)
        {
            Status.Text = "Access denied - run Hosts Blocker as administrator.";
        }
        catch (Exception ex)
        {
            Status.Text = "Save failed: " + ex.Message;
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_dirty && MessageBox.Show(this, "You have unsaved changes. Close anyway?",
                "Hosts Blocker", MessageBoxButton.OKCancel, MessageBoxImage.Warning)
            != MessageBoxResult.OK)
        {
            e.Cancel = true;
        }
        base.OnClosing(e);
    }
}
