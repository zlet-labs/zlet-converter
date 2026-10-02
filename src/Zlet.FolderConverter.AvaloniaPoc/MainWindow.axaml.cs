using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Zlet.FolderConverter.Core.Models;

namespace Zlet.FolderConverter.AvaloniaPoc;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly DispatcherTimer _timer;
    private int _cursor;
    private bool _stressEnabled;
    private QueueItemViewModel? _selectedItem;
    private double _overallProgress;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        SeedRows(2000);
        SelectedItem = Rows[1];
        CoreProbeText = $"Core linked · {typeof(OperationStatus).Assembly.GetName().Name}";

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += OnStressTick;
        _timer.Start();
    }

    public ObservableCollection<QueueItemViewModel> Rows { get; } = new();
    public string CoreProbeText { get; }

    public QueueItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set { if (_selectedItem == value) return; _selectedItem = value; Changed(); }
    }

    public double OverallProgress
    {
        get => _overallProgress;
        private set { _overallProgress = value; Changed(); Changed(nameof(OverallText)); }
    }

    public string OverallText => $"{Rows.Count:N0} files · {OverallProgress:0}% simulated";
    public string QueueSummary => $"{Rows.Count:N0} files";
    public string StressButtonText => _stressEnabled ? "Pause stress" : "Start stress";

    private void SeedRows(int count)
    {
        var names = new[]
        {
            "Project_Proposal.docx",
            "Research_Paper.pdf",
            "Договор_аренды_2026.docx",
            "Presentation.pptx",
            "Manual.docx",
            "Report.pdf",
            "Budget.xlsx",
            "Notes.txt"
        };

        for (var i = 0; i < count; i++)
        {
            var name = i < names.Length ? names[i] : $"Batch_{i + 1:D4}.docx";
            Rows.Add(new QueueItemViewModel(name, GuessOperation(name)));
        }

        Rows[0].MarkSucceeded();
        Rows[1].Progress = 62;
        Rows[1].MarkConverting();
        Rows[2].MarkReview();
        Rows[5].MarkFailed();
    }

    private void OnStressTick(object? sender, EventArgs e)
    {
        if (!_stressEnabled || Rows.Count == 0) return;

        for (var n = 0; n < 48; n++)
        {
            _cursor = (_cursor + 1) % Rows.Count;
            var row = Rows[_cursor];
            if (row.Status is "Failed" or "Converted") continue;

            row.MarkConverting();
            row.Progress = Math.Min(100, row.Progress + 3 + (_cursor % 5));

            if (row.Progress >= 100)
            {
                if (_cursor % 13 == 0) row.MarkReview();
                else row.MarkSucceeded();
            }
        }

        OverallProgress = Rows.Average(x => x.Progress);
    }

    private void Stress_Click(object? sender, RoutedEventArgs e)
    {
        _stressEnabled = !_stressEnabled;
        Changed(nameof(StressButtonText));
    }

    private async void AddFiles_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add files",
            AllowMultiple = true
        });

        foreach (var file in files)
            Rows.Insert(0, new QueueItemViewModel(file.Name, GuessOperation(file.Name)));

        Changed(nameof(QueueSummary));
        Changed(nameof(OverallText));
    }

    private async void AddFolder_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Add folder",
            AllowMultiple = false
        });

        if (folders.Count == 0) return;

        Rows.Insert(0, new QueueItemViewModel($"{folders[0].Name} / sample.docx", "DOCX → MD"));
        SelectedItem = Rows[0];
        Changed(nameof(QueueSummary));
        Changed(nameof(OverallText));
    }

    private static string GuessOperation(string name) =>
        Path.GetExtension(name).ToLowerInvariant() switch
        {
            ".pdf" => "PDF → MD",
            ".pptx" => "PPTX → MD",
            ".xlsx" => "XLSX → MD",
            ".txt" => "TXT → MD",
            _ => "DOCX → MD"
        };

    public new event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
