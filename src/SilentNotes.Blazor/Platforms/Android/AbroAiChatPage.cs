// Copyright © 2026 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using SilentNotes.Models;
using SilentNotes.Services;
using SilentNotes.Workers;
using MauiApplication = Microsoft.Maui.Controls.Application;

namespace SilentNotes.Platforms
{
    /// <summary>
    /// Full-screen Abro AI chat page (OpenAI-compatible providers).
    /// </summary>
    internal sealed class AbroAiChatPage : ContentPage
    {
        private readonly IAbroAiStoreService _storeService;
        private readonly IAbroAiSyncService _syncService;
        private readonly AbroAiChatClient _chatClient;
        private readonly VerticalStackLayout _messageStack;
        private readonly ScrollView _scrollView;
        private readonly Editor _input;
        private readonly Button _sendButton;
        private readonly Label _statusLabel;
        private readonly Label _providerLabel;
        private AbroAiStoreModel _store;
        private AbroAiConversationModel _conversation;
        private AbroAiProviderModel _provider;
        private bool _busy;
        private CancellationTokenSource _sendCts;
        private readonly Page _previousRootPage;
        private readonly TaskCompletionSource _closedSource;
        private readonly bool _rootPresentation;

        /// <summary>
        /// Opens a new or existing conversation. When <paramref name="initialUserText"/> is set,
        /// it is placed into the input (or sent as the first message draft).
        /// </summary>
        public AbroAiChatPage(Guid? conversationId = null, string initialUserText = null)
            : this(conversationId, initialUserText, null, null)
        {
        }

        private AbroAiChatPage(
            Guid? conversationId,
            string initialUserText,
            Page previousRootPage,
            TaskCompletionSource closedSource)
        {
            _previousRootPage = previousRootPage;
            _closedSource = closedSource;
            _rootPresentation = previousRootPage != null && closedSource != null;
            BackgroundColor = Colors.Black;
            _storeService = Ioc.Instance.GetService<IAbroAiStoreService>();
            _syncService = Ioc.Instance.GetService<IAbroAiSyncService>();
            _chatClient = Ioc.Instance.GetService<AbroAiChatClient>() ?? new AbroAiChatClient();

            _store = _storeService.LoadOrDefault();
            if (conversationId.HasValue)
                _conversation = _store.Conversations.Find(c => c.Id == conversationId.Value);
            if (_conversation == null)
            {
                _conversation = new AbroAiConversationModel
                {
                    Title = string.IsNullOrWhiteSpace(initialUserText) ? "New chat" : Truncate(initialUserText, 48),
                };
                _store.Conversations.Insert(0, _conversation);
            }

            _provider = ResolveProvider(_store, _conversation);
            _conversation.ProviderId = _provider?.Id;

            Button backButton = CreateIconButton("←", 22);
            backButton.Clicked += async (s, e) => await CloseAsync().ConfigureAwait(true);

            var titleLabel = new Label
            {
                Text = Truncate(_conversation.Title, 40),
                TextColor = Colors.White,
                FontSize = 16,
                VerticalOptions = LayoutOptions.Center,
                LineBreakMode = LineBreakMode.TailTruncation,
            };

            _providerLabel = new Label
            {
                Text = _provider == null ? "No provider" : (_provider.Name ?? "Provider"),
                TextColor = Color.FromArgb("#8e8e93"),
                FontSize = 12,
                VerticalOptions = LayoutOptions.Center,
            };
            var providerTap = new TapGestureRecognizer();
            providerTap.Tapped += async (s, e) => await PickProviderAsync().ConfigureAwait(true);
            _providerLabel.GestureRecognizers.Add(providerTap);

            var headerText = new VerticalStackLayout
            {
                Spacing = 0,
                VerticalOptions = LayoutOptions.Center,
                Children = { titleLabel, _providerLabel },
            };

            var header = new Grid
            {
                BackgroundColor = Color.FromArgb("#1c1c1e"),
                HeightRequest = 56,
                Padding = new Thickness(4, 0, 12, 0),
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Star),
                },
            };
            header.Add(backButton, 0, 0);
            header.Add(headerText, 1, 0);

            _messageStack = new VerticalStackLayout
            {
                Spacing = 10,
                Padding = new Thickness(12, 12, 12, 8),
            };
            _scrollView = new ScrollView
            {
                Content = _messageStack,
                VerticalOptions = LayoutOptions.Fill,
            };

            _statusLabel = new Label
            {
                Text = string.Empty,
                TextColor = Color.FromArgb("#8e8e93"),
                FontSize = 12,
                Margin = new Thickness(12, 0),
                IsVisible = false,
            };

            _input = new Editor
            {
                Placeholder = "Message…",
                PlaceholderColor = Color.FromArgb("#8e8e93"),
                TextColor = Colors.White,
                BackgroundColor = Color.FromArgb("#2c2c2e"),
                AutoSize = EditorAutoSizeOption.TextChanges,
                FontSize = 15,
                HeightRequest = 72,
                MaximumHeightRequest = 160,
            };
            if (!string.IsNullOrWhiteSpace(initialUserText) && _conversation.Messages.Count == 0)
                _input.Text = initialUserText;

            _sendButton = new Button
            {
                Text = "Send",
                TextColor = Colors.White,
                BackgroundColor = Color.FromArgb("#2481cc"),
                CornerRadius = 8,
                HeightRequest = 44,
                WidthRequest = 72,
                VerticalOptions = LayoutOptions.End,
            };
            _sendButton.Clicked += async (s, e) => await SendAsync().ConfigureAwait(true);

            var inputBar = new Grid
            {
                BackgroundColor = Color.FromArgb("#1c1c1e"),
                Padding = new Thickness(8, 8, 8, 12),
                ColumnSpacing = 8,
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto),
                },
            };
            inputBar.Add(_input, 0, 0);
            inputBar.Add(_sendButton, 1, 0);

            var root = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Star),
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto),
                },
            };
            root.Add(header, 0, 0);
            root.Add(_scrollView, 0, 1);
            root.Add(_statusLabel, 0, 2);
            root.Add(inputBar, 0, 3);
            Content = root;

            RebuildMessages();
            Persist(scheduleSync: false);
        }

        private static AbroAiProviderModel ResolveProvider(AbroAiStoreModel store, AbroAiConversationModel conversation)
        {
            if (conversation?.ProviderId.HasValue == true)
            {
                AbroAiProviderModel match = store.Providers.Find(p => p.Id == conversation.ProviderId.Value);
                if (match != null)
                    return match;
            }

            return store.GetDefaultProvider();
        }

        private void RebuildMessages()
        {
            _messageStack.Children.Clear();
            foreach (AbroAiMessageModel message in _conversation.Messages)
                _messageStack.Children.Add(CreateBubble(message));
            _ = ScrollToEndAsync();
        }

        private static View CreateBubble(AbroAiMessageModel message)
        {
            bool isUser = string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase);
            Color bubbleBg = isUser ? Color.FromArgb("#2481cc") : Color.FromArgb("#2c2c2e");
            Color textColor = Colors.White;
            View body = isUser
                ? CreatePlainLabel(message.Content ?? string.Empty, textColor, 15)
                : CreateMarkdownBody(message.Content ?? string.Empty, textColor, bubbleBg);

            return new Border
            {
                StrokeThickness = 0,
                BackgroundColor = bubbleBg,
                Padding = new Thickness(12, 10),
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
                Content = body,
                HorizontalOptions = isUser ? LayoutOptions.End : LayoutOptions.Start,
                MaximumWidthRequest = 360,
            };
        }

        private static View CreateMarkdownBody(string markdown, Color textColor, Color bubbleBg)
        {
            List<AbroAiMarkdown.Block> blocks = AbroAiMarkdown.Parse(markdown);
            if (blocks.Count == 0)
                return CreatePlainLabel(string.Empty, textColor, 15);

            // Single plain paragraph → keep a simple label.
            if (blocks.Count == 1
                && blocks[0].Kind == AbroAiMarkdown.BlockKind.Paragraph
                && !LooksLikeInlineMarkdown(blocks[0].Text))
            {
                return CreatePlainLabel(blocks[0].Text ?? string.Empty, textColor, 15);
            }

            var stack = new VerticalStackLayout { Spacing = 8 };
            int orderedIndex = 0;
            foreach (AbroAiMarkdown.Block block in blocks)
            {
                switch (block.Kind)
                {
                    case AbroAiMarkdown.BlockKind.Heading:
                        orderedIndex = 0;
                        stack.Children.Add(CreateInlineLabel(
                            block.Text,
                            textColor,
                            FontSizeForHeading(block.Level),
                            FontAttributes.Bold));
                        break;

                    case AbroAiMarkdown.BlockKind.Code:
                        orderedIndex = 0;
                        stack.Children.Add(CreateCodeBlock(block.Text, block.Language, bubbleBg));
                        break;

                    case AbroAiMarkdown.BlockKind.Quote:
                        orderedIndex = 0;
                        stack.Children.Add(CreateQuoteBlock(block.Text, textColor));
                        break;

                    case AbroAiMarkdown.BlockKind.ListItem:
                        bool numbered = block.Level == 1;
                        if (!block.ContinuesList)
                            orderedIndex = 0;
                        if (numbered)
                            orderedIndex++;
                        stack.Children.Add(CreateListItem(block.Text, textColor, numbered, orderedIndex));
                        break;

                    default:
                        orderedIndex = 0;
                        stack.Children.Add(CreateInlineLabel(block.Text, textColor, 15, FontAttributes.None));
                        break;
                }
            }

            return stack;
        }

        private static View CreateCodeBlock(string code, string language, Color bubbleBg)
        {
            var header = string.IsNullOrWhiteSpace(language)
                ? null
                : new Label
                {
                    Text = language.Trim(),
                    TextColor = Color.FromArgb("#8e8e93"),
                    FontSize = 11,
                    Margin = new Thickness(0, 0, 0, 4),
                };

            var codeLabel = new Label
            {
                Text = code ?? string.Empty,
                TextColor = Color.FromArgb("#e8e8ed"),
                FontSize = 12.5,
                FontFamily = "monospace",
                LineBreakMode = LineBreakMode.WordWrap,
            };

            var inner = new VerticalStackLayout { Spacing = 0 };
            if (header != null)
                inner.Children.Add(header);
            inner.Children.Add(codeLabel);

            // Slightly darker than the bubble so fences stand out.
            Color codeBg = Darken(bubbleBg, 0.18);
            return new Border
            {
                StrokeThickness = 0,
                BackgroundColor = codeBg,
                Padding = new Thickness(10, 8),
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
                Content = inner,
            };
        }

        private static View CreateQuoteBlock(string text, Color textColor)
        {
            var content = CreateInlineLabel(text, Color.FromArgb("#c7c7cc"), 14, FontAttributes.Italic);
            var row = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(new GridLength(3)),
                    new ColumnDefinition(GridLength.Star),
                },
                ColumnSpacing = 8,
            };
            row.Add(new BoxView { Color = Color.FromArgb("#636366") }, 0, 0);
            row.Add(content, 1, 0);
            return row;
        }

        private static View CreateListItem(string text, Color textColor, bool numbered, int number)
        {
            var row = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Star),
                },
                ColumnSpacing = 8,
            };
            row.Add(new Label
            {
                Text = numbered ? $"{Math.Max(1, number)}." : "•",
                TextColor = textColor,
                FontSize = 15,
            }, 0, 0);
            row.Add(CreateInlineLabel(text, textColor, 15, FontAttributes.None), 1, 0);
            return row;
        }

        private static Label CreateInlineLabel(string text, Color textColor, double fontSize, FontAttributes baseAttributes)
        {
            List<AbroAiMarkdown.Inline> inlines = AbroAiMarkdown.ParseInlines(text ?? string.Empty);
            if (inlines.Count == 0 || (inlines.Count == 1 && inlines[0].Kind == AbroAiMarkdown.InlineKind.Text))
            {
                return new Label
                {
                    Text = text ?? string.Empty,
                    TextColor = textColor,
                    FontSize = fontSize,
                    FontAttributes = baseAttributes,
                    LineBreakMode = LineBreakMode.WordWrap,
                };
            }

            var formatted = new FormattedString();
            foreach (AbroAiMarkdown.Inline span in inlines)
            {
                FontAttributes attrs = baseAttributes;
                Color color = textColor;
                double size = fontSize;
                switch (span.Kind)
                {
                    case AbroAiMarkdown.InlineKind.Bold:
                        attrs |= FontAttributes.Bold;
                        break;
                    case AbroAiMarkdown.InlineKind.Italic:
                        attrs |= FontAttributes.Italic;
                        break;
                    case AbroAiMarkdown.InlineKind.Code:
                        color = Color.FromArgb("#ffd60a");
                        size = fontSize - 1;
                        break;
                }

                var run = new Span
                {
                    Text = span.Text ?? string.Empty,
                    TextColor = color,
                    FontSize = size,
                    FontAttributes = attrs,
                };
                if (span.Kind == AbroAiMarkdown.InlineKind.Code)
                    run.FontFamily = "monospace";
                formatted.Spans.Add(run);
            }

            return new Label
            {
                FormattedText = formatted,
                LineBreakMode = LineBreakMode.WordWrap,
            };
        }

        private static Label CreatePlainLabel(string text, Color textColor, double fontSize)
        {
            return new Label
            {
                Text = text ?? string.Empty,
                TextColor = textColor,
                FontSize = fontSize,
                LineBreakMode = LineBreakMode.WordWrap,
            };
        }

        private static double FontSizeForHeading(int level)
        {
            return level switch
            {
                1 => 22,
                2 => 19,
                3 => 17,
                _ => 16,
            };
        }

        private static bool LooksLikeInlineMarkdown(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;
            return text.Contains("**", StringComparison.Ordinal)
                || text.Contains('`')
                || (text.Contains('*') && text.IndexOf('*') != text.LastIndexOf('*'));
        }

        private static Color Darken(Color color, double amount)
        {
            amount = Math.Clamp(amount, 0, 1);
            return new Color(
                (float)(color.Red * (1 - amount)),
                (float)(color.Green * (1 - amount)),
                (float)(color.Blue * (1 - amount)),
                color.Alpha);
        }

        private async Task SendAsync()
        {
            if (_busy)
                return;

            string text = (_input.Text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(text))
                return;

            if (_provider == null
                || string.IsNullOrWhiteSpace(_provider.Url)
                || string.IsNullOrWhiteSpace(_provider.ModelId))
            {
                SetStatus("Configure an AI provider in Abro Settings first.");
                return;
            }

            _busy = true;
            _sendButton.IsEnabled = false;
            _input.Text = string.Empty;
            SetStatus("Thinking…");

            var userMessage = new AbroAiMessageModel
            {
                Role = "user",
                Content = text,
                At = DateTime.UtcNow,
            };
            _conversation.Messages.Add(userMessage);
            if (_conversation.Title == "New chat" || string.IsNullOrWhiteSpace(_conversation.Title))
                _conversation.Title = Truncate(text, 48);
            _conversation.ModifiedAt = DateTime.UtcNow;
            _conversation.ProviderId = _provider.Id;
            _messageStack.Children.Add(CreateBubble(userMessage));
            Persist(scheduleSync: false);
            await ScrollToEndAsync().ConfigureAwait(true);

            _sendCts?.Cancel();
            _sendCts = new CancellationTokenSource();
            try
            {
                string reply = await _chatClient
                    .CompleteAsync(
                        _provider,
                        _conversation.Messages,
                        _sendCts.Token,
                        mode: AbroAiChatClient.DefaultMode,
                        threadId: _conversation.Id.ToString())
                    .ConfigureAwait(true);
                var assistant = new AbroAiMessageModel
                {
                    Role = "assistant",
                    Content = reply ?? string.Empty,
                    At = DateTime.UtcNow,
                };
                _conversation.Messages.Add(assistant);
                _conversation.ModifiedAt = DateTime.UtcNow;
                _messageStack.Children.Add(CreateBubble(assistant));
                Persist(scheduleSync: true);
                SetStatus(null);
                await ScrollToEndAsync().ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                SetStatus("Cancelled");
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
            }
            finally
            {
                _busy = false;
                _sendButton.IsEnabled = true;
            }
        }

        private async Task PickProviderAsync()
        {
            if (_store.Providers.Count == 0)
            {
                SetStatus("No providers configured.");
                return;
            }

            string[] names = _store.Providers.Select(p => p.Name ?? "Provider").ToArray();
            string choice = await DisplayActionSheetAsync("Provider", "Cancel", null, names).ConfigureAwait(true);
            int index = Array.IndexOf(names, choice);
            if (index < 0)
                return;

            _provider = _store.Providers[index];
            _conversation.ProviderId = _provider.Id;
            _conversation.ModifiedAt = DateTime.UtcNow;
            _providerLabel.Text = _provider.Name ?? "Provider";
            Persist(scheduleSync: true);
        }

        private void Persist(bool scheduleSync)
        {
            try
            {
                // Keep this conversation near the top of the local list.
                _store.Conversations.RemoveAll(c => c.Id == _conversation.Id);
                _store.Conversations.Insert(0, _conversation);
                _storeService.TrySave(_store);
                if (scheduleSync && _syncService?.IsConfigured == true)
                    _ = _syncService.SyncAsync();
            }
            catch
            {
            }
        }

        private void SetStatus(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                _statusLabel.IsVisible = false;
                _statusLabel.Text = string.Empty;
                return;
            }

            _statusLabel.Text = text;
            _statusLabel.IsVisible = true;
        }

        private async Task ScrollToEndAsync()
        {
            try
            {
                await Task.Delay(50).ConfigureAwait(true);
                await _scrollView.ScrollToAsync(0, _messageStack.Height, false).ConfigureAwait(true);
            }
            catch
            {
            }
        }

        private async Task CloseAsync()
        {
            _sendCts?.Cancel();
            Persist(scheduleSync: true);
            if (_rootPresentation)
            {
                try
                {
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        if (MauiApplication.Current?.Windows != null
                            && MauiApplication.Current.Windows.Count > 0
                            && ReferenceEquals(MauiApplication.Current.Windows[0].Page, this)
                            && _previousRootPage != null)
                        {
                            MauiApplication.Current.Windows[0].Page = _previousRootPage;
                        }
                    }).ConfigureAwait(true);
                }
                catch
                {
                }

                _closedSource?.TrySetResult();
                return;
            }

            try
            {
                await Navigation.PopModalAsync(false).ConfigureAwait(true);
            }
            catch
            {
            }
        }

        /// <summary>
        /// Presents AI chat on top of the aNote root page. Must run on the UI thread.
        /// </summary>
        internal static Task PresentFromAnoteAsync(string initialUserText)
        {
            if (MauiApplication.Current?.Windows == null || MauiApplication.Current.Windows.Count == 0)
                return Task.CompletedTask;

            try
            {
                Window window = MauiApplication.Current.Windows[0];
                Page previousPage = window.Page;
                var closedSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var page = new AbroAiChatPage(null, initialUserText, previousPage, closedSource);
                window.Page = page;
                return closedSource.Task;
            }
            catch
            {
                return Task.CompletedTask;
            }
        }

        private static Button CreateIconButton(string text, double fontSize)
        {
            return new Button
            {
                Text = text,
                FontSize = fontSize,
                TextColor = Colors.White,
                BackgroundColor = Colors.Transparent,
                WidthRequest = 44,
                HeightRequest = 44,
                Padding = 0,
                VerticalOptions = LayoutOptions.Center,
            };
        }

        private static string Truncate(string value, int max)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            string oneLine = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (oneLine.Length <= max)
                return oneLine;
            return oneLine.Substring(0, max - 1) + "…";
        }
    }
}
