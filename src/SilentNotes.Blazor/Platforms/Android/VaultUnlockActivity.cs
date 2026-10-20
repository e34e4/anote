// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Linq;
using System.Security;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Text;
using Android.Widget;
using Microsoft.Extensions.DependencyInjection;
using SilentNotes.Crypto;
using SilentNotes.Platforms.Services;
using SilentNotes.Services;
using Orientation = Android.Widget.Orientation;

namespace SilentNotes.Platforms
{
    /// <summary>
    /// Lightweight unlock UI shown when Autofill needs the vault while it is locked.
    /// </summary>
    [Activity(
        Exported = false,
        Theme = "@android:style/Theme.DeviceDefault.Dialog.Alert",
        ExcludeFromRecents = true,
        NoHistory = true)]
    public class VaultUnlockActivity : Activity
    {
        /// <summary>Intent extra: requesting package name.</summary>
        public const string ExtraPackageName = "package";

        /// <summary>Intent extra: web domain from AssistStructure.</summary>
        public const string ExtraWebDomain = "domain";

        private EditText _pinOrPassword;
        private IPasswordVaultService _vault;
        private string _packageName;
        private string _webDomain;

        /// <inheritdoc/>
        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            _packageName = Intent?.GetStringExtra(ExtraPackageName);
            _webDomain = Intent?.GetStringExtra(ExtraWebDomain);
            _vault = ResolveVault();

            var layout = new LinearLayout(this) { Orientation = Orientation.Vertical };
            int pad = (int)(16 * Resources.DisplayMetrics.Density);
            layout.SetPadding(pad, pad, pad, pad);

            var title = new TextView(this);
            title.Text = "Unlock aNote vault";
            title.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 18);
            layout.AddView(title);

            _pinOrPassword = new EditText(this);
            _pinOrPassword.InputType = InputTypes.ClassText | InputTypes.TextVariationPassword;
            _pinOrPassword.Hint = (_vault != null && _vault.HasPin) ? "PIN or master password" : "Master password";
            layout.AddView(_pinOrPassword);

            var unlockButton = new global::Android.Widget.Button(this) { Text = "Unlock" };
            unlockButton.Click += (_, __) => TryUnlock();
            layout.AddView(unlockButton);

            var cancelButton = new global::Android.Widget.Button(this) { Text = "Cancel" };
            cancelButton.Click += (_, __) =>
            {
                SetResult(Result.Canceled);
                Finish();
            };
            layout.AddView(cancelButton);

            SetContentView(layout);
        }

        private void TryUnlock()
        {
            if (_vault == null)
            {
                Toast.MakeText(this, "Vault unavailable", ToastLength.Short).Show();
                return;
            }

            string text = _pinOrPassword.Text ?? string.Empty;
            if (string.IsNullOrEmpty(text))
                return;

            SecureString secret = CryptoUtils.StringToSecureString(text);
            bool ok = false;
            if (_vault.HasPin && text.Length >= 5 && text.Length <= 12 && text.All(char.IsDigit))
                ok = _vault.UnlockWithPin(secret);
            if (!ok)
                ok = _vault.UnlockWithMaster(secret);
            secret.Dispose();

            if (!ok)
            {
                Toast.MakeText(this, "Wrong PIN or password", ToastLength.Short).Show();
                return;
            }

            // After unlock, return to Autofill so it can re-query; empty auth result is enough
            // for the framework to call OnFillRequest again in many OEMs. Provide a minimal OK.
            var reply = new Intent();
            SetResult(Result.Ok, reply);
            Finish();
        }

        private IPasswordVaultService ResolveVault()
        {
            try
            {
                return Ioc.Instance.GetService<IPasswordVaultService>();
            }
            catch
            {
                try
                {
                    var services = new ServiceCollection();
                    MauiProgram.RegisterSharedServices(services);
                    MauiProgram.RegisterPlatformServices(services);
                    var provider = services.BuildServiceProvider();
                    provider.GetService<IAppContextService>()?.InitializeWithContextOnly(this);
                    return provider.GetService<IPasswordVaultService>();
                }
                catch
                {
                    return null;
                }
            }
        }
    }
}
