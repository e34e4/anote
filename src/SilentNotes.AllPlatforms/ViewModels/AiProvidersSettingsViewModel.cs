using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using SilentNotes.Models;
using SilentNotes.Services;

namespace SilentNotes.ViewModels
{
    /// <summary>
    /// View model for the AI providers settings sub-page.
    /// </summary>
    internal class AiProvidersSettingsViewModel : ViewModelBase
    {
        private readonly IAbroAiStoreService _storeService;
        private readonly IAbroAiSyncService _syncService;
        private readonly IFeedbackService _feedbackService;
        private readonly ILanguageService _language;

        /// <summary>
        /// Initializes a new instance of the <see cref="AiProvidersSettingsViewModel"/> class.
        /// </summary>
        public AiProvidersSettingsViewModel(
            IAbroAiStoreService storeService,
            IAbroAiSyncService syncService,
            IFeedbackService feedbackService,
            ILanguageService languageService)
        {
            _storeService = storeService;
            _syncService = syncService;
            _feedbackService = feedbackService;
            _language = languageService;
            Providers = new List<AbroAiProviderModel>();
            ReloadFromStore();
            AddProviderCommand = new RelayCommand(StartAddProvider);
            SyncNowCommand = new AsyncRelayCommand(SyncNowAsync);
            OverwriteRemoteCommand = new AsyncRelayCommand(OverwriteRemoteAsync);
        }

        /// <summary>Gets the provider list (copy of store order).</summary>
        public List<AbroAiProviderModel> Providers { get; private set; }

        /// <summary>Gets the id of the default provider, if any.</summary>
        public Guid? DefaultProviderId { get; private set; }

        /// <summary>Gets a value indicating whether the editor overlay is visible.</summary>
        public bool IsEditorOpen { get; private set; }

        /// <summary>Gets a value indicating whether we are editing an existing provider.</summary>
        public bool IsEditing { get; private set; }

        /// <summary>Gets or sets the editor display name.</summary>
        public string EditName { get; set; } = string.Empty;

        /// <summary>Gets or sets the editor API base URL.</summary>
        public string EditUrl { get; set; } = string.Empty;

        /// <summary>Gets or sets the editor model id.</summary>
        public string EditModelId { get; set; } = string.Empty;

        /// <summary>Gets or sets the editor API key.</summary>
        public string EditApiKey { get; set; } = string.Empty;

        /// <summary>Gets the command to add a new provider.</summary>
        public ICommand AddProviderCommand { get; }

        /// <summary>Gets the command to sync the AI store now.</summary>
        public ICommand SyncNowCommand { get; }

        /// <summary>Gets the command to overwrite the remote AI store.</summary>
        public ICommand OverwriteRemoteCommand { get; }

        /// <summary>Gets a value indicating whether sync can run.</summary>
        public bool CanSyncAi => _syncService?.IsConfigured == true;

        /// <summary>Reloads providers from disk.</summary>
        public void ReloadFromStore()
        {
            AbroAiStoreModel store = _storeService.LoadOrDefault();
            DefaultProviderId = store.DefaultProviderId;
            Providers = store.Providers?.ToList() ?? new List<AbroAiProviderModel>();
            OnPropertyChanged(nameof(Providers), nameof(DefaultProviderId), nameof(CanSyncAi));
        }

        /// <summary>Opens the editor for a new provider.</summary>
        public void StartAddProvider()
        {
            IsEditing = false;
            EditName = "Command Code";
            EditUrl = string.Empty;
            EditModelId = string.Empty;
            EditApiKey = string.Empty;
            IsEditorOpen = true;
            NotifyEditorState();
        }

        /// <summary>Opens the editor for an existing provider.</summary>
        public void StartEditProvider(AbroAiProviderModel provider)
        {
            if (provider == null)
                return;
            IsEditing = true;
            EditName = provider.Name ?? string.Empty;
            EditUrl = provider.Url ?? string.Empty;
            EditModelId = provider.ModelId ?? string.Empty;
            EditApiKey = provider.ApiKey ?? string.Empty;
            _editingId = provider.Id;
            IsEditorOpen = true;
            NotifyEditorState();
        }

        private Guid _editingId;

        /// <summary>Closes the editor without saving.</summary>
        public void CancelEditor()
        {
            IsEditorOpen = false;
            NotifyEditorState();
        }

        /// <summary>Saves the editor fields to the store.</summary>
        public bool TrySaveEditor()
        {
            if (string.IsNullOrWhiteSpace(EditName)
                || string.IsNullOrWhiteSpace(EditUrl)
                || string.IsNullOrWhiteSpace(EditModelId))
            {
                _feedbackService.ShowToast(_language["ai_providers_required_fields"]);
                return false;
            }

            AbroAiStoreModel store = _storeService.LoadOrDefault();
            AbroAiProviderModel provider;
            if (IsEditing)
            {
                provider = store.Providers.Find(p => p.Id == _editingId);
                if (provider == null)
                {
                    ReloadFromStore();
                    return false;
                }
            }
            else
            {
                provider = new AbroAiProviderModel();
                store.Providers.Add(provider);
                if (!store.DefaultProviderId.HasValue)
                    store.DefaultProviderId = provider.Id;
            }

            provider.Name = EditName.Trim();
            provider.Url = EditUrl.Trim();
            provider.ModelId = EditModelId.Trim();
            provider.ApiKey = (EditApiKey ?? string.Empty).Trim();
            provider.ModifiedAt = DateTime.UtcNow;

            if (!_storeService.TrySave(store))
            {
                _feedbackService.ShowToast(_language["ai_providers_save_failed"]);
                return false;
            }

            ScheduleSync();
            IsEditorOpen = false;
            ReloadFromStore();
            NotifyEditorState();
            _feedbackService.ShowToast(_language[IsEditing ? "ai_providers_saved" : "ai_providers_added"]);
            return true;
        }

        /// <summary>Sets the default provider.</summary>
        public void SetDefault(Guid providerId)
        {
            AbroAiStoreModel store = _storeService.LoadOrDefault();
            AbroAiProviderModel provider = store.Providers.Find(p => p.Id == providerId);
            if (provider == null)
                return;
            store.DefaultProviderId = providerId;
            provider.ModifiedAt = DateTime.UtcNow;
            _storeService.TrySave(store);
            ScheduleSync();
            ReloadFromStore();
            _feedbackService.ShowToast(_language["ai_providers_default_set"]);
        }

        /// <summary>Deletes a provider.</summary>
        public void DeleteProvider(Guid providerId)
        {
            AbroAiStoreModel store = _storeService.LoadOrDefault();
            store.Providers.RemoveAll(p => p.Id == providerId);
            if (store.DefaultProviderId == providerId)
                store.DefaultProviderId = store.Providers.FirstOrDefault()?.Id;
            _storeService.TrySave(store);
            ScheduleSync();
            ReloadFromStore();
            _feedbackService.ShowToast(_language["ai_providers_deleted"]);
        }

        private void NotifyEditorState()
        {
            OnPropertyChanged(
                nameof(IsEditorOpen),
                nameof(IsEditing),
                nameof(EditName),
                nameof(EditUrl),
                nameof(EditModelId),
                nameof(EditApiKey));
        }

        private void ScheduleSync()
        {
            if (_syncService?.IsConfigured == true)
                _ = _syncService.SyncAsync();
        }

        private async Task SyncNowAsync()
        {
            if (_syncService == null)
                return;
            AbroAiSyncResult result = await _syncService.SyncAsync();
            ShowSyncResult(result);
            ReloadFromStore();
        }

        private async Task OverwriteRemoteAsync()
        {
            if (_syncService == null)
                return;
            AbroAiSyncResult result = await _syncService.OverwriteRemoteAsync();
            ShowSyncResult(result);
        }

        private void ShowSyncResult(AbroAiSyncResult result)
        {
            switch (result)
            {
                case AbroAiSyncResult.Success:
                    _feedbackService.ShowToast(_language["ai_sync_success"]);
                    break;
                case AbroAiSyncResult.Disabled:
                    _feedbackService.ShowToast(_language["ai_sync_disabled"]);
                    break;
                case AbroAiSyncResult.NotConfigured:
                    _feedbackService.ShowToast(_language["ai_sync_not_configured"]);
                    break;
                default:
                    string msg = string.IsNullOrWhiteSpace(_syncService.LastError)
                        ? _language["ai_sync_failed"]
                        : _syncService.LastError;
                    _feedbackService.ShowToast(msg, FeedbackSeverity.Warning);
                    break;
            }
        }
    }
}
