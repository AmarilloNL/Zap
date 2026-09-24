using System.Windows;
using System.Windows.Input;

namespace Zap.App;

public enum DeleteChoice { Permanent, RecycleBin }

public partial class DeleteConfirmDialog : Window
{
    public DeleteChoice Choice { get; private set; }

    public DeleteConfirmDialog(string summary, string detail)
    {
        InitializeComponent();
        SummaryText.Text = summary;
        DetailText.Text = detail;
    }

    void Permanent_Click(object sender, RoutedEventArgs e) { Choice = DeleteChoice.Permanent; DialogResult = true; }
    void Recycle_Click(object sender, RoutedEventArgs e) { Choice = DeleteChoice.RecycleBin; DialogResult = true; }
    void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();
}
