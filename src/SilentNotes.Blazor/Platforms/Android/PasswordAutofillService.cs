// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using Android;
using Android.App;
using Android.App.Assist;
using Android.Content;
using Android.OS;
using Android.Service.Autofill;
using Android.Text;
using Android.Views.Autofill;
using Android.Widget;
using Microsoft.Extensions.DependencyInjection;
using SilentNotes.Models;
using SilentNotes.Platforms.Services;
using SilentNotes.Services;
using FillResponse = Android.Service.Autofill.FillResponse;

namespace SilentNotes.Platforms
{
    /// <summary>
    /// Android Autofill service that offers unlocked vault logins system-wide.
    /// </summary>
    [Service(
        Exported = true,
        Label = "aNote Passwords",
        Permission = Manifest.Permission.BindAutofillService)]
    [IntentFilter(new[] { "android.service.autofill.AutofillService" })]
    [MetaData("android.autofill", Resource = "@xml/autofill_service_config")]
    public class PasswordAutofillService : AutofillService
    {
        /// <inheritdoc/>
        public override void OnFillRequest(FillRequest request, CancellationSignal cancellationSignal, FillCallback callback)
        {
            try
            {
                if (Build.VERSION.SdkInt < BuildVersionCodes.O)
                {
                    callback.OnSuccess(null);
                    return;
                }

                var fillContexts = request.FillContexts;
                if (fillContexts == null || fillContexts.Count == 0)
                {
                    callback.OnSuccess(null);
                    return;
                }
                AssistStructure structure = fillContexts[fillContexts.Count - 1]?.Structure;
                if (structure == null)
                {
                    callback.OnSuccess(null);
                    return;
                }

                var parser = new AssistStructureParser(structure);
                if (!parser.HasLoginFields)
                {
                    callback.OnSuccess(null);
                    return;
                }

                IPasswordVaultService vault = GetVaultService();
                if (vault == null)
                {
                    callback.OnSuccess(null);
                    return;
                }

                if (!vault.IsUnlocked)
                {
                    Intent authIntent = new Intent(this, typeof(VaultUnlockActivity));
                    authIntent.PutExtra(VaultUnlockActivity.ExtraPackageName, parser.PackageName);
                    authIntent.PutExtra(VaultUnlockActivity.ExtraWebDomain, parser.WebDomain);
                    PendingIntent pending = PendingIntent.GetActivity(
                        this,
                        0,
                        authIntent,
                        PendingIntentFlags.CancelCurrent | PendingIntentFlags.Mutable);
                    var responseBuilder = new FillResponse.Builder();
                    var presentation = CreateRemoteViews("Unlock aNote");
                    Dataset.Builder authDataset = new Dataset.Builder(presentation);
                    if (parser.UsernameId != null)
                        authDataset.SetValue(parser.UsernameId, AutofillValue.ForText(""));
                    if (parser.PasswordId != null)
                        authDataset.SetValue(parser.PasswordId, AutofillValue.ForText(""));
                    authDataset.SetAuthentication(pending.IntentSender);
                    responseBuilder.AddDataset(authDataset.Build());
                    callback.OnSuccess(responseBuilder.Build());
                    return;
                }

                string matchToken = !string.IsNullOrEmpty(parser.WebDomain) ? parser.WebDomain : parser.PackageName;
                IReadOnlyList<PasswordEntryModel> matches = vault.FindMatches(matchToken);
                if (matches.Count == 0)
                    matches = vault.Vault?.Entries?.Take(5).ToList() ?? (IReadOnlyList<PasswordEntryModel>)Array.Empty<PasswordEntryModel>();

                if (matches.Count == 0)
                {
                    callback.OnSuccess(null);
                    return;
                }

                var fillResponse = new FillResponse.Builder();
                foreach (PasswordEntryModel entry in matches.Take(8))
                {
                    string title = string.IsNullOrWhiteSpace(entry.Name) ? (entry.Username ?? "Login") : entry.Name;
                    if (!string.IsNullOrWhiteSpace(entry.Username))
                        title = title + " — " + entry.Username;
                    var datasetBuilder = new Dataset.Builder(CreateRemoteViews(title));
                    if (parser.UsernameId != null)
                        datasetBuilder.SetValue(parser.UsernameId, AutofillValue.ForText(entry.Username ?? string.Empty));
                    if (parser.PasswordId != null)
                        datasetBuilder.SetValue(parser.PasswordId, AutofillValue.ForText(entry.Password ?? string.Empty));
                    if (parser.OtpId != null && !string.IsNullOrWhiteSpace(entry.TotpSecret))
                    {
                        string code = Workers.TotpService.GenerateCode(
                            entry.TotpSecret, entry.TotpPeriod, entry.TotpDigits, DateTime.UtcNow);
                        if (!string.IsNullOrEmpty(code))
                            datasetBuilder.SetValue(parser.OtpId, AutofillValue.ForText(code));
                    }
                    fillResponse.AddDataset(datasetBuilder.Build());
                }
                callback.OnSuccess(fillResponse.Build());
            }
            catch (Exception)
            {
                callback.OnSuccess(null);
            }
        }

        /// <inheritdoc/>
        public override void OnSaveRequest(SaveRequest request, SaveCallback callback)
        {
            // Saving new credentials from Autofill is not implemented yet.
            callback.OnSuccess();
        }

        private RemoteViews CreateRemoteViews(string text)
        {
            var views = new RemoteViews(PackageName, global::Android.Resource.Layout.SimpleListItem1);
            views.SetTextViewText(global::Android.Resource.Id.Text1, text);
            return views;
        }

        private IPasswordVaultService GetVaultService()
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

        private sealed class AssistStructureParser
        {
            public AssistStructureParser(AssistStructure structure)
            {
                PackageName = structure.ActivityComponent?.PackageName;
                for (int i = 0; i < structure.WindowNodeCount; i++)
                {
                    AssistStructure.WindowNode window = structure.GetWindowNodeAt(i);
                    ParseNode(window.RootViewNode);
                }
            }

            public string PackageName { get; }
            public string WebDomain { get; private set; }
            public AutofillId UsernameId { get; private set; }
            public AutofillId PasswordId { get; private set; }
            public AutofillId OtpId { get; private set; }
            public bool HasLoginFields => UsernameId != null || PasswordId != null;

            private void ParseNode(AssistStructure.ViewNode node)
            {
                if (node == null)
                    return;

                if (string.IsNullOrEmpty(WebDomain) && !string.IsNullOrEmpty(node.WebDomain))
                    WebDomain = node.WebDomain;

                AutofillId id = node.AutofillId;
                string[] autofillHints = node.GetAutofillHints();
                string hint = string.Join(' ', autofillHints ?? Array.Empty<string>()).ToLowerInvariant();
                string html = node.HtmlInfo?.Tag?.ToLowerInvariant() ?? string.Empty;
                InputTypes inputType = (InputTypes)node.InputType;
                string idEntry = node.IdEntry?.ToLowerInvariant() ?? string.Empty;
                string hintText = node.Hint?.ToLowerInvariant() ?? string.Empty;

                bool isPassword =
                    hint.Contains("password") ||
                    idEntry.Contains("password") ||
                    hintText.Contains("password") ||
                    (inputType & InputTypes.TextVariationPassword) == InputTypes.TextVariationPassword ||
                    (inputType & InputTypes.TextVariationWebPassword) == InputTypes.TextVariationWebPassword ||
                    (inputType & InputTypes.NumberVariationPassword) == InputTypes.NumberVariationPassword;

                bool isUsername =
                    hint.Contains("username") ||
                    hint.Contains("email") ||
                    idEntry.Contains("user") ||
                    idEntry.Contains("email") ||
                    idEntry.Contains("login") ||
                    hintText.Contains("user") ||
                    hintText.Contains("email") ||
                    (inputType & InputTypes.TextVariationEmailAddress) == InputTypes.TextVariationEmailAddress ||
                    (inputType & InputTypes.TextVariationWebEmailAddress) == InputTypes.TextVariationWebEmailAddress;

                bool isOtp =
                    hint.Contains("otp") ||
                    hint.Contains("2fa") ||
                    hint.Contains("totp") ||
                    idEntry.Contains("otp") ||
                    idEntry.Contains("totp") ||
                    hintText.Contains("code");

                if (id != null)
                {
                    if (isPassword && PasswordId == null)
                        PasswordId = id;
                    else if (isOtp && OtpId == null)
                        OtpId = id;
                    else if (isUsername && UsernameId == null && !isPassword)
                        UsernameId = id;
                    else if (UsernameId == null && PasswordId == null &&
                             (html == "input" || node.ClassName?.Contains("EditText") == true) &&
                             !isPassword)
                    {
                        // Heuristic: first text field before password becomes username.
                        UsernameId = id;
                    }
                }

                for (int i = 0; i < node.ChildCount; i++)
                    ParseNode(node.GetChildAt(i));
            }
        }
    }
}
