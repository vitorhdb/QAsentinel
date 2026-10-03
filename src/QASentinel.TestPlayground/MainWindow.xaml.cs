using System.Collections.ObjectModel;
using System.Windows;

namespace QASentinel.TestPlayground;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        Rows = new ObservableCollection<SampleRow>
        {
            new(1, "Grupo Operacional", "Operacional", true),
            new(2, "Cobrança", "Financeiro", true),
            new(3, "Registro inativo", "Administrativo", false)
        };
        SampleGrid.ItemsSource = Rows;
    }

    public ObservableCollection<SampleRow> Rows { get; }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        LastActionText.Text = $"Salvo: {NameTextBox.Text}";
        MessageBox.Show(
            $"Registro '{NameTextBox.Text}' salvo com sucesso.",
            "Test Playground",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        NameTextBox.Clear();
        NotesTextBox.Clear();
        PasswordTextBox.Clear();
        CategoryComboBox.SelectedIndex = 0;
        ActiveCheckBox.IsChecked = true;
        LastActionText.Text = "Formulário limpo";
        NameTextBox.Focus();
    }

    private void OpenDialog_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PlaygroundDialog { Owner = this };
        var result = dialog.ShowDialog();
        LastActionText.Text = result == true ? "Dialog confirmado" : "Dialog cancelado";
    }

    private void About_Click(object sender, RoutedEventArgs e) => MessageBox.Show(
        "QA Sentinel Test Playground\nAplicação controlada para testes de automação desktop.",
        "Sobre",
        MessageBoxButton.OK,
        MessageBoxImage.Information);

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();
}

public sealed record SampleRow(int Code, string Description, string Category, bool IsActive);
