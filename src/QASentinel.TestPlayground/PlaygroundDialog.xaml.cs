using System.Windows;

namespace QASentinel.TestPlayground;

public partial class PlaygroundDialog : Window
{
    public PlaygroundDialog() => InitializeComponent();

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
