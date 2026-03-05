using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ImgCombiner;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool SetProperty<T>(ref T backing, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(backing, value)) return false;
        backing = value;
        OnPropertyChanged(name);
        return true;
    }
}