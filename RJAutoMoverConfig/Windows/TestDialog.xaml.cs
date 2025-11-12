using System.Windows;

namespace RJAutoMoverConfig.Windows;

public partial class TestDialog : Window
{
    public TestDialog()
    {
        InitializeComponent();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
