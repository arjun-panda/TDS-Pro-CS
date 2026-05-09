using System.Windows;
using System.Windows.Input;

namespace TDSPro.App
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Ensure WebView gets focus on window activation
            blazorWebView.Focus();
            Keyboard.Focus(blazorWebView);
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            blazorWebView.Focus();
        }
    }
}
