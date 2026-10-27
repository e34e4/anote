// Copyright © 2023 Martin Stoeckli.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;

namespace SilentNotes
{
    /// <summary>
    /// Message to inform the current page to store its unsaved data.
    /// </summary>
    public class StoreUnsavedDataMessage
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="StoreUnsavedDataMessage"/> class.
        /// Can be used for testing.
        /// </summary>
        internal StoreUnsavedDataMessage()
            : this(MessageSender.Unknown)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="StoreUnsavedDataMessage"/> class.
        /// </summary>
        /// <param name="sender">The origin from where the message is sent.</param>
        public StoreUnsavedDataMessage(MessageSender sender)
        {
            Sender = sender;
        }

        public MessageSender Sender { get; }
    }

    /// <summary>
    /// Message to inform the current page that it will be closed in a moment.
    /// </summary>
    public class ClosePageMessage
    {
    }

    /// <summary>
    /// Message to inform the current page that a call to StateHasChanged is necessary.
    /// </summary>
    public class RedrawCurrentPageMessage
    {
    }

    /// <summary>
    /// Message to inform the application MainLayout page that a call to StateHasChanged is necessary.
    /// </summary>
    public class RedrawMainPageMessage
    {
    }

    /// <summary>
    /// Message to inform the current page that the system back button was pressed.
    /// </summary>
    public class BackButtonPressedMessage
    {
        /// <summary>
        /// Gets or sets a value indicating whether the listener handled the press event (e.g. by
        /// closing an open menu).
        /// </summary>
        public bool Handled { get; set; }

        /// <summary>
        /// Gets or sets a target for backwards navigation. A page should pass its BackRoute,
        /// a value of null will close the application.
        /// </summary>
        public string BackRoute { get; set; }
    }

    /// <summary>
    /// Message to inform the application that the main layout has finished first rendering.
    /// At this moment the services are available form IOC, even if they are scoped services.
    /// </summary>
    public class MainLayoutReadyMessage
    {
    }

    public class SynchronizationIsRunningChangedMessage
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SynchronizationIsRunningChangedMessage"/> class.
        /// </summary>
        /// <param name="isRunning">Sets the <see cref="IsRunning"/> property.</param>
        public SynchronizationIsRunningChangedMessage(bool isRunning)
        {
            IsRunning = isRunning;
        }

        /// <summary>
        /// Gets the new state after the cange.
        /// </summary>
        public bool IsRunning { get; }
    }

    /// <summary>
    /// Messager to signal that the app resumed after pausing on mobile devices.
    /// </summary>
    public class AfterResumeMessage
    {
        /// <summary>
        /// Gets or sets a value indicating whether another app shared text with SilentNotes, to
        /// create a new note.
        /// </summary>
        public bool NewNoteShared { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the safe was closed because of the pause timeout.
        /// </summary>
        public bool SafesClosed { get; set; }
    }

    /// <summary>
    /// This message can be used to signal that a given note should be brought into view.
    /// </summary>
    internal class BringNoteIntoViewMessage
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="BringNoteIntoViewMessage"/> class.
        /// </summary>
        /// <param name="noteId">Sets the <see cref="NoteId"/> property.</param>
        /// <param name="smooth">Sets the <see cref="Smooth"/> property.</param>
        public BringNoteIntoViewMessage(Guid noteId, bool smooth)
        {
            NoteId = noteId;
            Smooth = smooth;
        }

        /// <summary>
        /// Gets the id of the note to bring into view.
        /// </summary>
        public Guid NoteId { get; }

        /// <summary>
        /// Gets a value indicating whether the scrooling should be done smooth, or immediately.
        /// </summary>
        public bool Smooth { get; }
    }

    /// <summary>
    /// Message to inform the clipboard tab that the clipboard history has changed and should be
    /// reloaded. It is sent by the background clipboard monitor.
    /// </summary>
    public class ClipboardChangedMessage
    {
    }

    /// <summary>
    /// Google OAuth redirect for a Mail account. The URL stays in <see cref="MailOAuthSession"/>
    /// until the mail tab consumes it, so a redirect that arrives before the tab is created is kept.
    /// </summary>
    public class MailGoogleOAuthRedirectMessage
    {
        public MailGoogleOAuthRedirectMessage(string redirectUrl)
        {
            RedirectUrl = redirectUrl;
        }

        public string RedirectUrl { get; }
    }

    /// <summary>Holds a Gmail OAuth redirect until the mail tab handles it.</summary>
    public static class MailOAuthSession
    {
        public const string StatePrefix = "mail.";

        private static string _redirectUrl;

        public static void Offer(string redirectUrl)
        {
            if (!string.IsNullOrEmpty(redirectUrl))
                _redirectUrl = redirectUrl;
        }

        public static string Peek()
        {
            return _redirectUrl;
        }

        public static void Clear(string redirectUrl)
        {
            if (_redirectUrl == redirectUrl)
                _redirectUrl = null;
        }

        public static bool IsMailRedirect(string redirectUrl)
        {
            return !string.IsNullOrEmpty(redirectUrl)
                && redirectUrl.IndexOf("state=" + StatePrefix, StringComparison.Ordinal) >= 0;
        }
    }

    /// <summary>
    /// Message to inform the booky tab that the bookmarks have changed and should be reloaded.
    /// It is sent after the bookmarks were merged with the shared Booky bookmarks file.
    /// </summary>
    public class BookyChangedMessage
    {
    }

    /// <summary>
    /// Message to inform the main page that a calendar intent was received from another
    /// application, and that its request should be handled by the calendar tab.
    /// </summary>
    public class CalendarIntentMessage
    {
    }

    /// <summary>
    /// Message to inform the main page that a clock intent was received, e.g. because an alarm is
    /// ringing, and that its request should be handled by the clock tab.
    /// </summary>
    public class ClockIntentMessage
    {
    }

    /// <summary>
    /// Message to inform the sms tab that the stored messages have changed, e.g. because an SMS
    /// was received by the background receiver, and that it should be reloaded.
    /// </summary>
    public class SmsChangedMessage
    {
    }

    /// <summary>
    /// Message to inform the main page that a phone/dialer intent was received, so the Phone tab
    /// can be opened.
    /// </summary>
    public class PhoneIntentMessage
    {
    }

    /// <summary>
    /// Message to inform the main page that an SMS compose intent was received (or a contact SMS
    /// action was used), so the SMS tab can open a new conversation.
    /// </summary>
    public class SmsIntentMessage
    {
    }

    /// <summary>
    /// Message to inform the Phone tab that a phone call state or call history has changed.
    /// </summary>
    public class PhoneCallChangedMessage
    {
    }

    /// <summary>
    /// Message to inform the main page that a specific tab should be activated (e.g. from the
    /// floating button quick menu).
    /// </summary>
    public class TabIntentMessage
    {
    }

    /// <summary>
    /// Message to inform the main page that a media VIEW/SEND intent was received, so the Player
    /// tab can open and play the file.
    /// </summary>
    public class MediaIntentMessage
    {
    }

    /// <summary>
    /// Message to inform the main page that a document VIEW/SEND intent was received, so the
    /// Reader tab can open the file.
    /// </summary>
    public class DocumentIntentMessage
    {
    }

    /// <summary>
    /// Message to inform the main page that a Telegram deep link should be opened in the TG tab
    /// (from Mini App navigation or an Android VIEW intent for t.me / tg://).
    /// </summary>
    public class TelegramLinkMessage
    {
    }

    /// <summary>
    /// Enumeration of possible sources of a sent message.
    /// </summary>
    public enum MessageSender
    {
        Unknown,
        ApplicationEventHandler,
        NavigationManager,
        ViewModel,
    }
}
