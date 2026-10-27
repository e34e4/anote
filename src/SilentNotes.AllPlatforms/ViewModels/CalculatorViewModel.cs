// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using SilentNotes.Models;
using SilentNotes.Services;
using SilentNotes.Workers;

namespace SilentNotes.ViewModels
{
    /// <summary>
    /// View model of the calculator tab. It handles the entered expression, the live preview, the
    /// angle mode and a persisted history of calculations.
    /// </summary>
    public class CalculatorViewModel : ViewModelBase
    {
        private const int MaxHistoryItems = 50;
        private static readonly char[] Operators = { '+', '-', '×', '÷', '^' };
        private static readonly string[] Functions = { "sin", "cos", "tan", "cot", "asin", "acos", "atan", "acot", "ln", "log", "exp", "√" };

        private readonly ILanguageService _languageService;
        private readonly ISettingsService _settingsService;
        private readonly List<CalculatorHistoryItem> _history = new List<CalculatorHistoryItem>();
        private string _expression = string.Empty;
        private string _result = string.Empty;
        private bool _isScientific;
        private bool _useDegrees = true;
        private bool _justEvaluated;

        /// <summary>
        /// Initializes a new instance of the <see cref="CalculatorViewModel"/> class.
        /// </summary>
        /// <param name="languageService">A language service.</param>
        /// <param name="settingsService">Settings service used to persist the history.</param>
        public CalculatorViewModel(ILanguageService languageService, ISettingsService settingsService = null)
        {
            _languageService = languageService;
            _settingsService = settingsService;

            InputCommand = new RelayCommand<string>(Input);
            BracketsCommand = new RelayCommand(Brackets);
            SignCommand = new RelayCommand(Sign);
            DeleteCommand = new RelayCommand(Delete);
            ClearCommand = new RelayCommand(Clear);
            CalculateCommand = new RelayCommand(Calculate);
            ToggleScientificCommand = new RelayCommand(() => IsScientific = !IsScientific);
            ToggleAngleCommand = new RelayCommand(() => UseDegrees = !UseDegrees);
            UseHistoryCommand = new RelayCommand<CalculatorHistoryItem>(UseHistory);
            ClearHistoryCommand = new RelayCommand(ClearHistory);

            LoadHistory();
        }

        /// <summary>Gets the command which appends a token to the expression.</summary>
        public ICommand InputCommand { get; }

        /// <summary>Gets the command which adds an opening or closing bracket.</summary>
        public ICommand BracketsCommand { get; }

        /// <summary>Gets the command which toggles the sign of the last number.</summary>
        public ICommand SignCommand { get; }

        /// <summary>Gets the command which deletes the last token.</summary>
        public ICommand DeleteCommand { get; }

        /// <summary>Gets the command which clears the expression.</summary>
        public ICommand ClearCommand { get; }

        /// <summary>Gets the command which evaluates the expression.</summary>
        public ICommand CalculateCommand { get; }

        /// <summary>Gets the command which toggles the scientific keypad.</summary>
        public ICommand ToggleScientificCommand { get; }

        /// <summary>Gets the command which toggles between degrees and radians.</summary>
        public ICommand ToggleAngleCommand { get; }

        /// <summary>Gets the command which loads a history entry into the expression.</summary>
        public ICommand UseHistoryCommand { get; }

        /// <summary>Gets the command which clears the history.</summary>
        public ICommand ClearHistoryCommand { get; }

        /// <summary>Gets the entered expression.</summary>
        public string Expression
        {
            get { return _expression; }
            private set { SetProperty(ref _expression, value); }
        }

        /// <summary>Gets the live preview of the result, or an empty string.</summary>
        public string Result
        {
            get { return _result; }
            private set { SetProperty(ref _result, value); }
        }

        /// <summary>Gets or sets a value indicating whether the scientific keypad is shown.</summary>
        public bool IsScientific
        {
            get { return _isScientific; }
            set { SetProperty(ref _isScientific, value); }
        }

        /// <summary>Gets or sets a value indicating whether trig functions use degrees.</summary>
        public bool UseDegrees
        {
            get { return _useDegrees; }
            set
            {
                if (SetProperty(ref _useDegrees, value))
                {
                    OnPropertyChanged(nameof(AngleLabel));
                    UpdatePreview();
                }
            }
        }

        /// <summary>Gets the label of the current angle unit.</summary>
        public string AngleLabel
        {
            get { return UseDegrees ? "DEG" : "RAD"; }
        }

        /// <summary>Gets the history, newest first.</summary>
        public IReadOnlyList<CalculatorHistoryItem> History
        {
            get { return _history; }
        }

        /// <summary>Gets a value indicating whether the history has entries.</summary>
        public bool HasHistory
        {
            get { return _history.Count > 0; }
        }

        private static bool IsOperatorToken(string token)
        {
            return (token.Length == 1) && (Array.IndexOf(Operators, token[0]) >= 0);
        }

        private static bool IsFunctionToken(string token)
        {
            return Functions.Contains(token);
        }

        private static bool IsPlainNumber(string text)
        {
            return (text.Length > 0) && text.All(c => char.IsDigit(c) || (c == '.'));
        }

        private static string Complete(string expression)
        {
            string result = expression.Trim();
            int open = result.Count(c => c == '(') - result.Count(c => c == ')');
            while (open > 0)
            {
                result += ")";
                open--;
            }

            if ((result.Length > 0) && IsOperatorToken(result.Substring(result.Length - 1)))
                result += "0";
            if ((result.Length > 0) && ("+×÷^".IndexOf(result[0]) >= 0))
                result = "0" + result;
            return result;
        }

        private void SetExpression(string value)
        {
            Expression = value;
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            string expression = _expression ?? string.Empty;
            if (string.IsNullOrWhiteSpace(expression) || IsPlainNumber(expression))
            {
                Result = string.Empty;
                return;
            }

            string value = CalculatorEngine.Evaluate(Complete(expression), UseDegrees);
            Result = value ?? string.Empty;
        }

        private void Input(string append)
        {
            if (string.IsNullOrEmpty(append))
                return;

            string input = _expression ?? string.Empty;

            if (_justEvaluated)
            {
                if (IsOperatorToken(append))
                {
                    // An operator continues calculating with the previous result.
                    _justEvaluated = false;
                }
                else
                {
                    input = string.Empty;
                    Result = string.Empty;
                    _justEvaluated = false;
                }
            }

            char last = (input.Length > 0) ? input[input.Length - 1] : '\0';

            // Functions always open a bracket, adding an implicit multiplication when needed.
            if (IsFunctionToken(append))
            {
                if ((input.Length > 0) && (char.IsDigit(last) || IsClosingValue(last)))
                    input += "×";
                SetExpression(input + append + "(");
                return;
            }

            if (append == ".")
            {
                if ((last == 'π') || (last == 'e'))
                    return;

                int lastBreak = -1;
                for (int i = input.Length - 1; i >= 0; i--)
                {
                    if (!(char.IsDigit(input[i]) || (input[i] == '.')))
                    {
                        lastBreak = i;
                        break;
                    }
                }
                string currentNumber = input.Substring(lastBreak + 1);
                if ((currentNumber.Length == 0) || currentNumber.Contains('.'))
                    return;
                SetExpression(input + append);
                return;
            }

            bool isDigit = (append.Length == 1) && char.IsDigit(append[0]);

            if (isDigit && (input.Length > 0) && IsClosingValue(last))
            {
                SetExpression(input + "×" + append);
                return;
            }

            if (IsOperatorToken(append) && (input.Length > 0) && IsOperatorToken(last.ToString()))
            {
                // Replace a trailing operator instead of stacking two of them.
                SetExpression(input.Substring(0, input.Length - 1) + append);
                return;
            }

            if (((append == "e") || (append == "π")) && (input.Length > 0) && (char.IsDigit(last) || IsClosingValue(last)))
            {
                SetExpression(input + "×" + append);
                return;
            }

            SetExpression(input + append);
        }

        private static bool IsClosingValue(char c)
        {
            return (c == ')') || (c == '!') || (c == '%') || (c == 'e') || (c == 'π');
        }

        private void Brackets()
        {
            string input = _expression ?? string.Empty;
            if (input.Length > 0)
            {
                char last = input[input.Length - 1];
                int open = input.Count(c => c == '(');
                int close = input.Count(c => c == ')');

                if ((open > close) && (char.IsDigit(last) || IsClosingValue(last)))
                {
                    SetExpression(input + ")");
                    return;
                }
                if (char.IsDigit(last) || IsClosingValue(last))
                {
                    SetExpression(input + "×(");
                    return;
                }
            }
            SetExpression(input + "(");
        }

        private void Sign()
        {
            string input = _expression ?? string.Empty;
            if (input.Length == 0)
            {
                SetExpression("(-");
                return;
            }

            char last = input[input.Length - 1];
            if (char.IsDigit(last))
            {
                int start = input.Length - 1;
                while ((start >= 0) && (char.IsDigit(input[start]) || (input[start] == '.')))
                    start--;
                start++;
                string number = input.Substring(start);

                bool alreadyNegative = (start >= 2) && (input.Substring(start - 2, 2) == "(-")
                    && ((start - 2 == 0) || !char.IsDigit(input[start - 3]));
                if (alreadyNegative)
                    SetExpression(input.Substring(0, start - 2) + number);
                else
                    SetExpression(input.Substring(0, start) + "(-" + number);
                return;
            }

            if ((last == '-') && (input.Length >= 2) && (input[input.Length - 2] == '('))
            {
                SetExpression(input.Substring(0, input.Length - 2));
                return;
            }

            SetExpression(input + "(-");
        }

        private void Delete()
        {
            string input = _expression ?? string.Empty;
            if (input.Length == 0)
                return;

            // Remove a whole function token instead of a single character.
            string[] tokens = { "asin(", "acos(", "atan(", "acot(", "sin(", "cos(", "tan(", "cot(", "log(", "exp(", "abs(", "sqrt(", "ln(", "√(" };
            foreach (string token in tokens)
            {
                if (input.EndsWith(token))
                {
                    SetExpression(input.Substring(0, input.Length - token.Length));
                    return;
                }
            }

            SetExpression(input.Substring(0, input.Length - 1));
        }

        private void Clear()
        {
            Expression = string.Empty;
            Result = string.Empty;
            _justEvaluated = false;
        }

        private void Calculate()
        {
            string expression = _expression ?? string.Empty;
            if (string.IsNullOrWhiteSpace(expression) || IsPlainNumber(expression))
                return;

            string completed = Complete(expression);
            string value = CalculatorEngine.Evaluate(completed, UseDegrees);
            if (value == null)
            {
                Result = _languageService["calc_error"];
                return;
            }

            AddHistory(completed, value);
            Expression = value;
            Result = string.Empty;
            _justEvaluated = true;
        }

        private void LoadHistory()
        {
            _history.Clear();
            if (_settingsService == null)
                return;

            try
            {
                SettingsModel settings = _settingsService.LoadSettingsOrDefault();
                if (settings.CalculatorHistory == null)
                    return;

                foreach (CalculatorHistoryEntryModel entry in settings.CalculatorHistory)
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.Expression))
                        continue;
                    _history.Add(new CalculatorHistoryItem(entry.Expression, entry.Result ?? string.Empty));
                }
            }
            catch (Exception)
            {
            }

            OnPropertyChanged(nameof(History));
            OnPropertyChanged(nameof(HasHistory));
        }

        private void PersistHistory()
        {
            if (_settingsService == null)
                return;

            try
            {
                SettingsModel settings = _settingsService.LoadSettingsOrDefault();
                settings.CalculatorHistory = _history
                    .Select(item => new CalculatorHistoryEntryModel
                    {
                        Expression = item.Expression,
                        Result = item.Result,
                    })
                    .ToList();
                _settingsService.TrySaveSettingsToLocalDevice(settings);
            }
            catch (Exception)
            {
            }
        }

        private void AddHistory(string expression, string result)
        {
            _history.Insert(0, new CalculatorHistoryItem(expression, result));
            if (_history.Count > MaxHistoryItems)
                _history.RemoveAt(_history.Count - 1);
            PersistHistory();
            OnPropertyChanged(nameof(History));
            OnPropertyChanged(nameof(HasHistory));
        }

        private void UseHistory(CalculatorHistoryItem item)
        {
            if (item == null)
                return;
            _justEvaluated = false;
            SetExpression(item.Expression);
        }

        private void ClearHistory()
        {
            _history.Clear();
            PersistHistory();
            OnPropertyChanged(nameof(History));
            OnPropertyChanged(nameof(HasHistory));
        }
    }

    /// <summary>
    /// A single entry of the calculator history.
    /// </summary>
    public class CalculatorHistoryItem
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CalculatorHistoryItem"/> class.
        /// </summary>
        /// <param name="expression">The evaluated expression.</param>
        /// <param name="result">The formatted result.</param>
        public CalculatorHistoryItem(string expression, string result)
        {
            Expression = expression;
            Result = result;
        }

        /// <summary>Gets the evaluated expression.</summary>
        public string Expression { get; }

        /// <summary>Gets the formatted result.</summary>
        public string Result { get; }
    }
}
