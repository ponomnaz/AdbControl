namespace AdbControl.Shell.Views;

public partial class DetachedToolWindow
{
    public DetachedToolWindow()
    {
        InitializeComponent();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is IDisposable disposable)
        {
            disposable.Dispose();
        }

        base.OnClosed(e);
    }
}
