using System.Text;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using AndroidX.Activity;
using SilentNotes.Platforms;
using SilentNotes.Services;
using static Android.Views.ViewTreeObserver;

namespace SilentNotes
{
    [Activity(
        Label = "aNote",
        Theme = "@style/Maui.SplashTheme",
        MainLauncher = true,
        LaunchMode = LaunchMode.SingleTask,
        Exported = true,
        ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation)]
    [IntentFilter(
        new[] { "android.intent.action.VIEW" },
        Categories = new[] { "android.intent.category.DEFAULT" },
        DataMimeType = "vnd.android.cursor.item/event")]
    [IntentFilter(
        new[] { "android.intent.action.VIEW" },
        Categories = new[] { "android.intent.category.DEFAULT" },
        DataMimeType = "vnd.android.cursor.dir/event")]
    [IntentFilter(
        new[] { "android.intent.action.VIEW" },
        Categories = new[] { "android.intent.category.DEFAULT" },
        DataScheme = "content", DataHost = "com.android.calendar", DataPathPrefix = "/time")]
    [IntentFilter(
        new[] { "android.intent.action.EDIT" },
        Categories = new[] { "android.intent.category.DEFAULT" },
        DataMimeType = "vnd.android.cursor.item/event")]
    [IntentFilter(
        new[] { "android.intent.action.INSERT" },
        Categories = new[] { "android.intent.category.DEFAULT" },
        DataMimeType = "vnd.android.cursor.dir/event")]
    [IntentFilter(
        new[] { "android.intent.action.INSERT_OR_EDIT" },
        Categories = new[] { "android.intent.category.DEFAULT" },
        DataMimeType = "vnd.android.cursor.item/event")]
    [IntentFilter(
        new[] { "android.intent.action.INSERT_OR_EDIT" },
        Categories = new[] { "android.intent.category.DEFAULT" },
        DataMimeType = "vnd.android.cursor.dir/event")]
    [IntentFilter(
        new[] { "android.intent.action.SENDTO" },
        Categories = new[] { "android.intent.category.DEFAULT" },
        DataScheme = "sms")]
    [IntentFilter(
        new[] { "android.intent.action.SENDTO" },
        Categories = new[] { "android.intent.category.DEFAULT" },
        DataScheme = "smsto")]
    [IntentFilter(
        new[] { "android.intent.action.SENDTO" },
        Categories = new[] { "android.intent.category.DEFAULT" },
        DataScheme = "mms")]
    [IntentFilter(
        new[] { "android.intent.action.SENDTO" },
        Categories = new[] { "android.intent.category.DEFAULT" },
        DataScheme = "mmsto")]
    [IntentFilter(
        new[] { "android.intent.action.DIAL" },
        Categories = new[] { "android.intent.category.DEFAULT" })]
    [IntentFilter(
        new[] { "android.intent.action.DIAL", "android.intent.action.VIEW" },
        Categories = new[] { "android.intent.category.DEFAULT", "android.intent.category.BROWSABLE" },
        DataScheme = "tel")]
    // Register aNote as an audio / video player (system "Open with" chooser).
    [IntentFilter(
        new[] { "android.intent.action.VIEW" },
        Categories = new[] { "android.intent.category.DEFAULT" },
        DataMimeType = "audio/*",
        DataScheme = "content")]
    [IntentFilter(
        new[] { "android.intent.action.VIEW" },
        Categories = new[] { "android.intent.category.DEFAULT" },
        DataMimeType = "audio/*",
        DataScheme = "file")]
    [IntentFilter(
        new[] { "android.intent.action.VIEW" },
        Categories = new[] { "android.intent.category.DEFAULT" },
        DataMimeType = "video/*",
        DataScheme = "content")]
    [IntentFilter(
        new[] { "android.intent.action.VIEW" },
        Categories = new[] { "android.intent.category.DEFAULT" },
        DataMimeType = "video/*",
        DataScheme = "file")]
    [IntentFilter(
        new[] { "android.intent.action.SEND" },
        Categories = new[] { "android.intent.category.DEFAULT" },
        DataMimeType = "audio/*")]
    [IntentFilter(
        new[] { "android.intent.action.SEND" },
        Categories = new[] { "android.intent.category.DEFAULT" },
        DataMimeType = "video/*")]
    // Register aNote as a document reader (text / PDF / Office / EPUB / FB2 / DjVu).
    [IntentFilter(
        new[] { "android.intent.action.VIEW" },
        Categories = new[] { "android.intent.category.DEFAULT" },
        DataMimeType = "text/*",
        DataScheme = "content")]
    [IntentFilter(
        new[] { "android.intent.action.VIEW" },
        Categories = new[] { "android.intent.category.DEFAULT" },
        DataMimeType = "text/*",
        DataScheme = "file")]
    [IntentFilter(
        new[] { "android.intent.action.VIEW" },
        Categories = new[] { "android.intent.category.DEFAULT" },
        DataMimeType = "application/pdf",
        DataScheme = "content")]
    [IntentFilter(
        new[] { "android.intent.action.VIEW" },
        Categories = new[] { "android.intent.category.DEFAULT" },
        DataMimeType = "application/pdf",
        DataScheme = "file")]
    [IntentFilter(
        new[] { "android.intent.action.VIEW" },
        Categories = new[] { "android.intent.category.DEFAULT" },
        DataMimeType = "application/epub+zip",
        DataScheme = "content")]
    [IntentFilter(
        new[] { "android.intent.action.VIEW" },
        Categories = new[] { "android.intent.category.DEFAULT" },
        DataMimeType = "application/epub+zip",
        DataScheme = "file")]
    [IntentFilter(
        new[] { "android.intent.action.VIEW" },
        Categories = new[] { "android.intent.category.DEFAULT" },
        DataMimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        DataScheme = "content")]
    [IntentFilter(
        new[] { "android.intent.action.VIEW" },
        Categories = new[] { "android.intent.category.DEFAULT" },
        DataMimeType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        DataScheme = "content")]
    [IntentFilter(
        new[] { "android.intent.action.VIEW" },
        Categories = new[] { "android.intent.category.DEFAULT" },
        DataMimeType = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        DataScheme = "content")]
    [IntentFilter(
        new[] { "android.intent.action.VIEW" },
        Categories = new[] { "android.intent.category.DEFAULT" },
        DataMimeType = "application/msword",
        DataScheme = "content")]
    [IntentFilter(
        new[] { "android.intent.action.VIEW" },
        Categories = new[] { "android.intent.category.DEFAULT" },
        DataMimeType = "application/vnd.ms-excel",
        DataScheme = "content")]
    [IntentFilter(
        new[] { "android.intent.action.VIEW" },
        Categories = new[] { "android.intent.category.DEFAULT" },
        DataMimeType = "application/vnd.oasis.opendocument.text",
        DataScheme = "content")]
    [IntentFilter(
        new[] { "android.intent.action.VIEW" },
        Categories = new[] { "android.intent.category.DEFAULT" },
        DataMimeType = "application/vnd.oasis.opendocument.spreadsheet",
        DataScheme = "content")]
    [IntentFilter(
        new[] { "android.intent.action.VIEW" },
        Categories = new[] { "android.intent.category.DEFAULT" },
        DataMimeType = "application/x-fictionbook+xml",
        DataScheme = "content")]
    [IntentFilter(
        new[] { "android.intent.action.VIEW" },
        Categories = new[] { "android.intent.category.DEFAULT" },
        DataMimeType = "image/vnd.djvu",
        DataScheme = "content")]
    [IntentFilter(
        new[] { "android.intent.action.SEND" },
        Categories = new[] { "android.intent.category.DEFAULT" },
        DataMimeType = "text/*")]
    [IntentFilter(
        new[] { "android.intent.action.SEND" },
        Categories = new[] { "android.intent.category.DEFAULT" },
        DataMimeType = "application/pdf")]
    [IntentFilter(
        new[] { "android.intent.action.SEND" },
        Categories = new[] { "android.intent.category.DEFAULT" },
        DataMimeType = "application/epub+zip")]
    public class MainActivity : MauiAppCompatActivity, IOnPreDrawListener
    {
        private readonly ApplicationEventHandler _applicationEventHandler = new ApplicationEventHandler();
        public Guid Id { get; } = Guid.NewGuid();
        private Android.Views.View _contentView;
        private bool _splashScreenCanBeClosed = false;
        private BackPressedHandler _backPressedHandler;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            AndroidX.Activity.EdgeToEdge.Enable(this);

            // Delay closing of splash screen until app is ready:
            // https://developer.android.com/develop/ui/views/launch/splash-screen#suspend-drawing
            var messenger = Ioc.Instance.GetService<IMessengerService>();
            messenger.Register<MainLayoutReadyMessage>(
                this, (recipient, message) => OnMainLayoutReady());
            _contentView = FindViewById<Android.Views.View>(Android.Resource.Id.Content);
            _contentView.ViewTreeObserver.AddOnPreDrawListener(this);

            _applicationEventHandler.OnCreate(this);

            _backPressedHandler = new BackPressedHandler(this);
            OnBackPressedDispatcher.AddCallback(this, _backPressedHandler);
        }

        protected override void OnNewIntent(Intent intent)
        {
            base.OnNewIntent(intent);
            _applicationEventHandler.OnNewIntent(intent);
        }

        protected override void OnResume()
        {
            base.OnResume();
            _applicationEventHandler.OnResume(this);
        }

        protected override void OnPause()
        {
            _applicationEventHandler.OnPause(this);
            base.OnPause();
        }

        protected override void OnStop()
        {
            _applicationEventHandler.OnStop(this);
            base.OnStop();
        }
        protected override void OnDestroy()
        {
            _applicationEventHandler.OnDestroy(this);
            base.OnDestroy();
        }

        protected override void OnActivityResult(int requestCode, Result resultCode, Intent data)
        {
            base.OnActivityResult(requestCode, resultCode, data);
            _applicationEventHandler.OnActivityResult(this, requestCode, resultCode, data);
        }

        private void OnMainLayoutReady()
        {
            var messenger = Ioc.Instance.GetService<IMessengerService>();
            messenger.Unregister<MainLayoutReadyMessage>(this); // This is a one time event.

            _splashScreenCanBeClosed = true;
            _applicationEventHandler.OnMainLayoutReady();
        }

        /// <summary>
        /// By overwriting this method, we can keep the splashscreen open until the application is
        /// ready, avoiding unnecessary flickering.
        /// </summary>
        /// <returns>Returns true if the app is ready and the splash screen can be closed.</returns>
        public bool OnPreDraw()
        {
            if (_splashScreenCanBeClosed)
            {
                _contentView.ViewTreeObserver.RemoveOnPreDrawListener(this);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Implements Androids <see cref="OnBackPressedCallback"/>.
        /// Handles the back button press event by closing open menus or dialogs, navigating to
        /// a previous page if available, or exiting the application.
        /// </summary>
        private class BackPressedHandler : OnBackPressedCallback
        {
            private readonly MainActivity _activity;

            /// <summary>
            /// Initializes a new instance of the <see cref="BackPressedHandler"/> class.
            /// </summary>
            /// <param name="activity">The owner activity.</param>
            public BackPressedHandler(MainActivity activity)
                : base(true)
            {
                _activity = activity;
            }

            /// <inheritdoc/>
            public override void HandleOnBackPressed()
            {
                // Ask the page to close currently open menus and dialogs.
                var message = new BackButtonPressedMessage { Handled = false };
                var messenger = Ioc.Instance.GetService<IMessengerService>();
                messenger.Send(message);
                if (message.Handled)
                    return;

                // Navigate backwards to the page which is defined by the current page. If the
                // back route is null, the application will be closed.
                if (!string.IsNullOrEmpty(message.BackRoute))
                {
                    var navigation = Ioc.Instance.GetService<INavigationService>();
                    navigation.NavigateTo(message.BackRoute);
                    return;
                }

                // Default action, close the app
                Enabled = false;
                _activity.OnBackPressedDispatcher.OnBackPressed();
                Enabled = true;
            }
        }
    }
}
