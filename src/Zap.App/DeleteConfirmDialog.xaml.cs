using System.Windows;

namespace Zap.App;

public enum DeleteChoice { Permanent, RecycleBin }

public partial class DeleteConfirmDialog : Window
{
    public DeleteChoice Choice { get; private set; }

    public DeleteConfirmDialog(string summary)
    {
        InitializeComponent();
        SummaryText.Text = summary;
    }

    void Permanent_Click(object sender, RoutedEventArgs e) { Choice = DeleteChoice.Permanent; DialogResult = true; }
    void Recycle_Click(object sender, RoutedEventArgs e) { Choice = DeleteChoice.RecycleBin; DialogResult = true; }
}
