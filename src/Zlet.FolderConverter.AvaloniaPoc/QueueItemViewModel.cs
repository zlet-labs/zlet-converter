using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Zlet.FolderConverter.AvaloniaPoc;

public sealed class QueueItemViewModel : INotifyPropertyChanged
{
    private int _progress;
    private string _status = "Queued";
    private string _quality = "Not evaluated";

    public QueueItemViewModel(string name, string operation)
    {
        Name = name;
        Operation = operation;
    }

    public string Name { get; }
    public string Operation { get; }
    public string Output => Name + ".md";

    public int Progress
    {
        get => _progress;
        set
        {
            if (_progress == value) return;
            _progress = value;
            Changed();
            Changed(nameof(ProgressText));
        }
    }

    public string ProgressText => $"{Progress}%";
    public string Status { get => _status; private set { _status = value; Changed(); } }
    public string Quality { get => _quality; private set { _quality = value; Changed(); } }

    public void MarkConverting() => Status = "Converting";
    public void MarkSucceeded() { Progress = 100; Status = "Converted"; Quality = "OK"; }
    public void MarkReview() { Progress = 100; Status = "Converted"; Quality = "Needs Review"; }
    public void MarkFailed() { Status = "Failed"; Quality = "Failed"; }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
