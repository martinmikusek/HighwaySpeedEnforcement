using System.Windows;
using HighwaySpeedEnforcement.Client.ViewModels;

namespace HighwaySpeedEnforcement.Client.Wpf.Views;

/// <summary>Main dashboard window; everything it shows is bound to <see cref="MainViewModel"/>.</summary>
public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
