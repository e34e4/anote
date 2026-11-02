using Microsoft.Maui.Controls;
#if ANDROID
using SilentNotes.Platforms;
#endif

namespace SilentNotes
{
    public partial class App : Application
    {
        public App(IServiceProvider serviceProvider)
        {
            InitializeComponent();
        }

        protected override Window CreateWindow(IActivationState activationState)
        {
#if ANDROID
            if (activationState?.Context?.Context is AbroBrowserActivity abroActivity)
            {
                AbroBrowserPage page = AbroBrowserPage.CreateFromIntent(abroActivity.Intent);
                return new Window(page) { Title = "Abro" };
            }
#endif
            var mainPage = new MainPage();
            return new Window(mainPage) { Title = "SilentNotes" };
        }
    }
}