using System.Windows;
using QASentinel.Desktop.ViewModels;

namespace QASentinel.Desktop;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
