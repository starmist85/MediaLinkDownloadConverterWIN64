using System.Windows;
using MediaFileDLConverter.ViewModels;

namespace MediaFileDLConverter
{
    /// <summary>
    /// Main window code-behind. Sets up the MainViewModel as the DataContext.
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();
        }
    }
}