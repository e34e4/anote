// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using SilentNotes.Services;

namespace SilentNotes.ViewModels
{
    /// <summary>
    /// View model for the Abro tab, which launches the light multi-tab web browser.
    /// </summary>
    public class AbroViewModel : ViewModelBase
    {
        public const int TabIndex = 19;

        private readonly IAbroBrowserService _browserService;

        /// <summary>
        /// Initializes a new instance of the <see cref="AbroViewModel"/> class.
        /// </summary>
        public AbroViewModel(IAbroBrowserService browserService)
        {
            _browserService = browserService;
            OpenBrowserCommand = new AsyncRelayCommand(OpenBrowserAsync);
            OpenBookmarksCommand = new AsyncRelayCommand(OpenBookmarksAsync);
            OpenHistoryCommand = new AsyncRelayCommand(OpenHistoryAsync);
        }

        /// <summary>Gets a value indicating whether the browser is available on this platform.</summary>
        public bool IsSupported => _browserService?.IsSupported == true;

        /// <summary>Gets the command which opens the browser.</summary>
        public ICommand OpenBrowserCommand { get; }

        /// <summary>Gets the command which opens Abro bookmarks.</summary>
        public ICommand OpenBookmarksCommand { get; }

        /// <summary>Gets the command which opens Abro history.</summary>
        public ICommand OpenHistoryCommand { get; }

        private async Task OpenBrowserAsync()
        {
            if (!IsSupported)
                return;
            await _browserService.PresentAsync();
        }

        private async Task OpenBookmarksAsync()
        {
            if (!IsSupported)
                return;
            await _browserService.PresentBookmarksAsync();
        }

        private async Task OpenHistoryAsync()
        {
            if (!IsSupported)
                return;
            await _browserService.PresentHistoryAsync();
        }
    }
}
