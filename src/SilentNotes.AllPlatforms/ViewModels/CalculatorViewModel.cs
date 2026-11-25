// Copyright © 2025 aNote.
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Globalization;
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
        private bool _isConverterMode;
        private UnitConverterEngine.ConverterCategory _converterCategory = UnitConverterEngine.ConverterCategory.Physics;
        private string _physicsGroupId = "length";
        private string _fromUnitId = "m";
        private string _toUnitId = "km";
        private string _converterInput = "1";
        private string _converterOutput = string.Empty;
        private string _currencyFrom = "USD";
        private string _currencyTo = "EUR";
        private int _numberFromBase = 10;
        private int _numberToBase = 16;
        private string _networkInput = "192.168.1.0/24";
        private string _csTool = "base";
        private string _hexAsciiInput = string.Empty;
        private bool _isProgrammer;
        private bool _isDateMode;
        private string _baseOutput = string.Empty;
        private string _variableName = string.Empty;
        private string _variableValue = string.Empty;
        private string _dateFrom = string.Empty;
        private string _dateTo = string.Empty;
        private string _dateAmount = "1";
        private string _dateUnit = "d";
        private string _dateResult = string.Empty;
        private readonly Dictionary<string, double> _variables = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private readonly List<CalculatorVariableItem> _variableList = new List<CalculatorVariableItem>();
        private readonly List<CalculatorCustomCurrencyItem> _customCurrencyList = new List<CalculatorCustomCurrencyItem>();
        private readonly List<CalculatorCustomRateProfileItem> _customRateProfileList = new List<CalculatorCustomRateProfileItem>();
        private string _customCurrencyCode = string.Empty;
        private string _customCurrencyUsdRate = string.Empty;
        private string _customProfileName = string.Empty;
        private string _customProfileBaseLabel = string.Empty;
        private string _customProfileQuoteLabel = string.Empty;
        private string _customProfileRate = string.Empty;
        private string _selectedCustomRateProfileId = string.Empty;
        private string _customRateOutput = string.Empty;

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
            ToggleConverterCommand = new RelayCommand(() => IsConverterMode = !IsConverterMode);
            UseHistoryCommand = new RelayCommand<CalculatorHistoryItem>(UseHistory);
            ClearHistoryCommand = new RelayCommand(ClearHistory);
            SetConverterCategoryCommand = new RelayCommand<UnitConverterEngine.ConverterCategory>(SetConverterCategory);
            ToggleProgrammerCommand = new RelayCommand(() => IsProgrammer = !IsProgrammer);
            ToggleDateModeCommand = new RelayCommand(() => IsDateMode = !IsDateMode);
            SaveVariableCommand = new RelayCommand(SaveVariable);
            DeleteVariableCommand = new RelayCommand<CalculatorVariableItem>(DeleteVariable);
            UseVariableCommand = new RelayCommand<CalculatorVariableItem>(UseVariable);
            ShiftDateCommand = new RelayCommand(ShiftDate);
            SaveCustomCurrencyCommand = new RelayCommand(SaveCustomCurrency);
            DeleteCustomCurrencyCommand = new RelayCommand<CalculatorCustomCurrencyItem>(DeleteCustomCurrency);
            SaveCustomRateProfileCommand = new RelayCommand(SaveCustomRateProfile);
            DeleteCustomRateProfileCommand = new RelayCommand<CalculatorCustomRateProfileItem>(DeleteCustomRateProfile);

            LoadHistory();
            LoadVariables();
            LoadCustomRates();
            RecalculateConverter();
            ResetDateTools();
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

        /// <summary>Gets the command which toggles the unit converters panel.</summary>
        public ICommand ToggleConverterCommand { get; }

        /// <summary>Gets the command which selects a converter category.</summary>
        public ICommand SetConverterCategoryCommand { get; }

        /// <summary>Gets the command which loads a history entry into the expression.</summary>
        public ICommand UseHistoryCommand { get; }

        /// <summary>Gets the command which clears the history.</summary>
        public ICommand ClearHistoryCommand { get; }

        /// <summary>Gets the command which toggles the programmer keypad.</summary>
        public ICommand ToggleProgrammerCommand { get; }

        /// <summary>Gets the command which toggles the date interval mode.</summary>
        public ICommand ToggleDateModeCommand { get; }

        /// <summary>Gets the command which stores the entered named variable.</summary>
        public ICommand SaveVariableCommand { get; }

        /// <summary>Gets the command which removes a named variable.</summary>
        public ICommand DeleteVariableCommand { get; }

        /// <summary>Gets the command which inserts a variable name into the expression.</summary>
        public ICommand UseVariableCommand { get; }

        /// <summary>Gets the command which shifts the start date by the entered interval.</summary>
        public ICommand ShiftDateCommand { get; }

        /// <summary>Gets the command which stores a custom currency USD rate.</summary>
        public ICommand SaveCustomCurrencyCommand { get; }

        /// <summary>Gets the command which removes a custom currency rate.</summary>
        public ICommand DeleteCustomCurrencyCommand { get; }

        /// <summary>Gets the command which stores a custom rate profile.</summary>
        public ICommand SaveCustomRateProfileCommand { get; }

        /// <summary>Gets the command which removes a custom rate profile.</summary>
        public ICommand DeleteCustomRateProfileCommand { get; }

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

        /// <summary>Gets or sets a value indicating whether the converters panel is shown.</summary>
        public bool IsConverterMode
        {
            get { return _isConverterMode; }
            set
            {
                if (SetProperty(ref _isConverterMode, value) && value)
                    RecalculateConverter();
            }
        }

        public UnitConverterEngine.ConverterCategory ConverterCategory
        {
            get => _converterCategory;
            set
            {
                if (SetProperty(ref _converterCategory, value))
                    RecalculateConverter();
            }
        }

        public bool IsPhysicsConverter => _converterCategory == UnitConverterEngine.ConverterCategory.Physics;
        public bool IsCsConverter => _converterCategory == UnitConverterEngine.ConverterCategory.ComputerScience;
        public bool IsNetworkingConverter => _converterCategory == UnitConverterEngine.ConverterCategory.Networking;
        public bool IsCurrencyConverter => _converterCategory == UnitConverterEngine.ConverterCategory.Currency;

        /// <summary>Gets or sets a value indicating whether the programmer keypad is shown.</summary>
        public bool IsProgrammer
        {
            get { return _isProgrammer; }
            set
            {
                if (SetProperty(ref _isProgrammer, value))
                {
                    if (value)
                    {
                        IsDateMode = false;
                        IsConverterMode = false;
                    }
                    RefreshBaseOutput();
                }
            }
        }

        /// <summary>Gets or sets a value indicating whether the date interval mode is shown.</summary>
        public bool IsDateMode
        {
            get { return _isDateMode; }
            set
            {
                if (SetProperty(ref _isDateMode, value))
                {
                    if (value)
                    {
                        IsProgrammer = false;
                        IsConverterMode = false;
                        ResetDateTools();
                    }
                }
            }
        }

        /// <summary>Gets the representation of the current value in hex, dec, oct and bin.</summary>
        public string BaseOutput
        {
            get { return _baseOutput; }
            private set { SetProperty(ref _baseOutput, value); }
        }

        /// <summary>Gets the persistent named variables.</summary>
        public IReadOnlyList<CalculatorVariableItem> Variables
        {
            get { return _variableList; }
        }

        /// <summary>Gets a value indicating whether any named variables are defined.</summary>
        public bool HasVariables
        {
            get { return _variableList.Count > 0; }
        }

        public string VariableName
        {
            get { return _variableName; }
            set { SetProperty(ref _variableName, value); }
        }

        public string VariableValue
        {
            get { return _variableValue; }
            set { SetProperty(ref _variableValue, value); }
        }

        public string DateFrom
        {
            get { return _dateFrom; }
            set { if (SetProperty(ref _dateFrom, value)) UpdateDateResult(); }
        }

        public string DateTo
        {
            get { return _dateTo; }
            set { if (SetProperty(ref _dateTo, value)) UpdateDateResult(); }
        }

        public string DateAmount
        {
            get { return _dateAmount; }
            set { SetProperty(ref _dateAmount, value); }
        }

        public string DateUnit
        {
            get { return _dateUnit; }
            set { SetProperty(ref _dateUnit, value); }
        }

        public string DateResult
        {
            get { return _dateResult; }
            private set { SetProperty(ref _dateResult, value); }
        }

        /// <summary>Gets the units which can be used to shift a date.</summary>
        public IReadOnlyList<CalculatorDateUnit> DateUnits { get; } = new[]
        {
            new CalculatorDateUnit("d", "Days"),
            new CalculatorDateUnit("w", "Weeks"),
            new CalculatorDateUnit("mo", "Months"),
            new CalculatorDateUnit("y", "Years"),
            new CalculatorDateUnit("h", "Hours"),
            new CalculatorDateUnit("min", "Minutes"),
            new CalculatorDateUnit("s", "Seconds"),
        };

        public IReadOnlyList<UnitConverterEngine.UnitGroup> PhysicsGroups => UnitConverterEngine.PhysicsGroups;
        public IReadOnlyList<UnitConverterEngine.UnitOption> PhysicsUnits =>
            UnitConverterEngine.PhysicsGroups.FirstOrDefault(g => g.Id == _physicsGroupId)?.Units
            ?? Array.Empty<UnitConverterEngine.UnitOption>();

        public string PhysicsGroupId
        {
            get => _physicsGroupId;
            set
            {
                if (SetProperty(ref _physicsGroupId, value))
                {
                    var units = PhysicsUnits;
                    if (units.Count > 0)
                    {
                        _fromUnitId = units[0].Id;
                        _toUnitId = units.Count > 1 ? units[1].Id : units[0].Id;
                        OnPropertyChanged(nameof(FromUnitId), nameof(ToUnitId), nameof(PhysicsUnits));
                    }
                    RecalculateConverter();
                }
            }
        }

        public string FromUnitId
        {
            get => _fromUnitId;
            set { if (SetProperty(ref _fromUnitId, value)) RecalculateConverter(); }
        }

        public string ToUnitId
        {
            get => _toUnitId;
            set { if (SetProperty(ref _toUnitId, value)) RecalculateConverter(); }
        }

        public string ConverterInput
        {
            get => _converterInput;
            set { if (SetProperty(ref _converterInput, value)) RecalculateConverter(); }
        }

        public string ConverterOutput
        {
            get => _converterOutput;
            private set => SetProperty(ref _converterOutput, value);
        }

        public IReadOnlyList<string> CurrencyCodes =>
            UnitConverterEngine.GetCurrencyCodes(BuildCustomCurrencyRatePairs());

        public IReadOnlyList<CalculatorCustomCurrencyItem> CustomCurrencyRates => _customCurrencyList;

        public bool HasCustomCurrencyRates => _customCurrencyList.Count > 0;

        public IReadOnlyList<CalculatorCustomRateProfileItem> CustomRateProfiles => _customRateProfileList;

        public bool HasCustomRateProfiles => _customRateProfileList.Count > 0;

        public string CustomCurrencyCode
        {
            get => _customCurrencyCode;
            set => SetProperty(ref _customCurrencyCode, value);
        }

        public string CustomCurrencyUsdRate
        {
            get => _customCurrencyUsdRate;
            set => SetProperty(ref _customCurrencyUsdRate, value);
        }

        public string CustomProfileName
        {
            get => _customProfileName;
            set => SetProperty(ref _customProfileName, value);
        }

        public string CustomProfileBaseLabel
        {
            get => _customProfileBaseLabel;
            set => SetProperty(ref _customProfileBaseLabel, value);
        }

        public string CustomProfileQuoteLabel
        {
            get => _customProfileQuoteLabel;
            set => SetProperty(ref _customProfileQuoteLabel, value);
        }

        public string CustomProfileRate
        {
            get => _customProfileRate;
            set => SetProperty(ref _customProfileRate, value);
        }

        public string SelectedCustomRateProfileId
        {
            get => _selectedCustomRateProfileId;
            set
            {
                if (SetProperty(ref _selectedCustomRateProfileId, value))
                    RecalculateConverter();
            }
        }

        public string CustomRateOutput
        {
            get => _customRateOutput;
            private set => SetProperty(ref _customRateOutput, value);
        }

        public string CurrencyFrom
        {
            get => _currencyFrom;
            set { if (SetProperty(ref _currencyFrom, value)) RecalculateConverter(); }
        }

        public string CurrencyTo
        {
            get => _currencyTo;
            set { if (SetProperty(ref _currencyTo, value)) RecalculateConverter(); }
        }

        public int NumberFromBase
        {
            get => _numberFromBase;
            set { if (SetProperty(ref _numberFromBase, value)) RecalculateConverter(); }
        }

        public int NumberToBase
        {
            get => _numberToBase;
            set { if (SetProperty(ref _numberToBase, value)) RecalculateConverter(); }
        }

        public string NetworkInput
        {
            get => _networkInput;
            set { if (SetProperty(ref _networkInput, value)) RecalculateConverter(); }
        }

        public string CsTool
        {
            get => _csTool;
            set { if (SetProperty(ref _csTool, value)) RecalculateConverter(); }
        }

        public string HexAsciiInput
        {
            get => _hexAsciiInput;
            set { if (SetProperty(ref _hexAsciiInput, value)) RecalculateConverter(); }
        }

        private void SetConverterCategory(UnitConverterEngine.ConverterCategory category)
        {
            ConverterCategory = category;
            OnPropertyChanged(nameof(IsPhysicsConverter), nameof(IsCsConverter), nameof(IsNetworkingConverter), nameof(IsCurrencyConverter));
        }

        public void RecalculateConverter()
        {
            try
            {
                switch (_converterCategory)
                {
                    case UnitConverterEngine.ConverterCategory.Physics:
                        ConverterOutput = ConvertPhysics();
                        break;
                    case UnitConverterEngine.ConverterCategory.Currency:
                        if (!double.TryParse(_converterInput, NumberStyles.Float, CultureInfo.InvariantCulture, out double money)
                            && !double.TryParse(_converterInput, NumberStyles.Float, CultureInfo.CurrentCulture, out money))
                        {
                            ConverterOutput = string.Empty;
                            break;
                        }
                        ConverterOutput = UnitConverterEngine.ConvertCurrency(
                            money,
                            _currencyFrom,
                            _currencyTo,
                            BuildCustomCurrencyRatePairs());
                        break;
                    case UnitConverterEngine.ConverterCategory.ComputerScience:
                        ConverterOutput = ConvertComputerScience();
                        break;
                    case UnitConverterEngine.ConverterCategory.Networking:
                        ConverterOutput = UnitConverterEngine.AnalyzeCidr(_networkInput);
                        break;
                    default:
                        ConverterOutput = string.Empty;
                        break;
                }

                RecalculateCustomRateProfile();
            }
            catch
            {
                ConverterOutput = string.Empty;
                CustomRateOutput = string.Empty;
            }
        }

        private void RecalculateCustomRateProfile()
        {
            CustomRateOutput = string.Empty;
            if (string.IsNullOrWhiteSpace(_selectedCustomRateProfileId))
                return;

            CalculatorCustomRateProfileItem profile = _customRateProfileList
                .FirstOrDefault(item => string.Equals(item.Id, _selectedCustomRateProfileId, StringComparison.Ordinal));
            if (profile == null || profile.Rate <= 0)
                return;

            if (!double.TryParse(_converterInput, NumberStyles.Float, CultureInfo.InvariantCulture, out double amount)
                && !double.TryParse(_converterInput, NumberStyles.Float, CultureInfo.CurrentCulture, out amount))
            {
                return;
            }

            CustomRateOutput = UnitConverterEngine.ConvertCustomRate(amount, profile.Rate);
        }

        private IEnumerable<KeyValuePair<string, double>> BuildCustomCurrencyRatePairs()
        {
            return _customCurrencyList.Select(item => new KeyValuePair<string, double>(item.Code, item.UsdRate));
        }

        private string ConvertPhysics()
        {
            var group = UnitConverterEngine.PhysicsGroups.FirstOrDefault(g => g.Id == _physicsGroupId);
            if (group == null)
                return string.Empty;
            if (!double.TryParse(_converterInput, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                && !double.TryParse(_converterInput, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
            {
                return string.Empty;
            }

            if (group.IsTemperature)
                return UnitConverterEngine.ConvertTemperature(value, _fromUnitId, _toUnitId);

            var from = group.Units.FirstOrDefault(u => u.Id == _fromUnitId);
            var to = group.Units.FirstOrDefault(u => u.Id == _toUnitId);
            return UnitConverterEngine.ConvertLinear(value, from, to);
        }

        private string ConvertComputerScience()
        {
            if (_csTool == "datasize")
            {
                var group = UnitConverterEngine.ComputerScienceGroups[0];
                if (!double.TryParse(_converterInput, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                    && !double.TryParse(_converterInput, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
                {
                    return string.Empty;
                }
                var from = group.Units.FirstOrDefault(u => u.Id == _fromUnitId) ?? group.Units[0];
                var to = group.Units.FirstOrDefault(u => u.Id == _toUnitId) ?? group.Units[1];
                return UnitConverterEngine.ConvertLinear(value, from, to);
            }

            if (_csTool == "asciihex")
                return UnitConverterEngine.AsciiToHex(_hexAsciiInput);
            if (_csTool == "hexascii")
                return UnitConverterEngine.HexToAscii(_hexAsciiInput);

            return UnitConverterEngine.ConvertNumberBase(_converterInput, _numberFromBase, _numberToBase);
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
                RefreshBaseOutput();
                return;
            }

            string value = CalculatorEngine.Evaluate(Complete(expression), UseDegrees, _variables);
            Result = value ?? string.Empty;
            RefreshBaseOutput();
        }

        private void RefreshBaseOutput()
        {
            string expression = _expression ?? string.Empty;
            double numericValue;
            if (IsPlainNumber(expression) &&
                double.TryParse(expression, NumberStyles.Float, CultureInfo.InvariantCulture, out numericValue))
            {
                BaseOutput = CalculatorEngine.FormatAllBases(numericValue);
                return;
            }

            string evaluated = CalculatorEngine.Evaluate(Complete(expression), UseDegrees, _variables);
            if (!string.IsNullOrWhiteSpace(evaluated) &&
                double.TryParse(evaluated, NumberStyles.Float, CultureInfo.InvariantCulture, out numericValue))
            {
                BaseOutput = CalculatorEngine.FormatAllBases(numericValue);
                return;
            }

            BaseOutput = string.Empty;
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

            if (CalculatorEngine.TryEvaluateAssignment(expression, UseDegrees, _variables, out _, out string assignedValue))
            {
                AddHistory(expression, assignedValue);
                Expression = assignedValue;
                Result = string.Empty;
                _justEvaluated = true;
                PersistVariables();
                NotifyVariablesChanged();
                RefreshBaseOutput();
                return;
            }

            string completed = Complete(expression);
            string value = CalculatorEngine.Evaluate(completed, UseDegrees, _variables);
            if (value == null)
            {
                Result = _languageService["calc_error"];
                return;
            }

            AddHistory(completed, value);
            Expression = value;
            Result = string.Empty;
            _justEvaluated = true;
            RefreshBaseOutput();
        }

        private void LoadVariables()
        {
            _variables.Clear();
            _variableList.Clear();
            if (_settingsService == null)
                return;

            try
            {
                SettingsModel settings = _settingsService.LoadSettingsOrDefault();
                if (settings.CalculatorVariables == null)
                    return;

                foreach (CalculatorVariableEntryModel entry in settings.CalculatorVariables)
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.Name))
                        continue;
                    string name = entry.Name.Trim().ToLowerInvariant();
                    _variables[name] = entry.Value;
                    _variableList.Add(new CalculatorVariableItem(name, entry.Value));
                }
                _variableList.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));
            }
            catch (Exception)
            {
            }
        }

        private void PersistVariables()
        {
            if (_settingsService == null)
                return;

            try
            {
                SettingsModel settings = _settingsService.LoadSettingsOrDefault();
                settings.CalculatorVariables = _variableList
                    .Select(item => new CalculatorVariableEntryModel { Name = item.Name, Value = item.Value })
                    .ToList();
                _settingsService.TrySaveSettingsToLocalDevice(settings);
            }
            catch (Exception)
            {
            }
        }

        private void NotifyVariablesChanged()
        {
            OnPropertyChanged(nameof(Variables), nameof(HasVariables));
        }

        private void SaveVariable()
        {
            string name = VariableName?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(name) || !char.IsLetter(name[0]) ||
                !name.All(c => char.IsLetterOrDigit(c) || c == '_'))
            {
                return;
            }

            string value = CalculatorEngine.Evaluate(VariableValue ?? string.Empty, UseDegrees, _variables);
            if (value == null || !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double numeric))
                return;

            _variables[name] = numeric;
            int index = _variableList.FindIndex(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
                _variableList[index] = new CalculatorVariableItem(name, numeric);
            else
                _variableList.Add(new CalculatorVariableItem(name, numeric));
            _variableList.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));

            VariableName = string.Empty;
            VariableValue = string.Empty;
            PersistVariables();
            NotifyVariablesChanged();
            RefreshBaseOutput();
        }

        private void DeleteVariable(CalculatorVariableItem item)
        {
            if (item == null)
                return;
            _variables.Remove(item.Name);
            _variableList.RemoveAll(entry => string.Equals(entry.Name, item.Name, StringComparison.OrdinalIgnoreCase));
            PersistVariables();
            NotifyVariablesChanged();
        }

        private void UseVariable(CalculatorVariableItem item)
        {
            if (item == null)
                return;
            Input(item.Name);
        }

        private void LoadCustomRates()
        {
            _customCurrencyList.Clear();
            _customRateProfileList.Clear();
            if (_settingsService == null)
            {
                NotifyCustomRatesChanged();
                return;
            }

            try
            {
                SettingsModel settings = _settingsService.LoadSettingsOrDefault();
                if (settings.CustomCurrencyRates != null)
                {
                    foreach (CurrencyRateEntryModel entry in settings.CustomCurrencyRates)
                    {
                        if (entry == null || string.IsNullOrWhiteSpace(entry.Code) || entry.UsdRate <= 0)
                            continue;
                        string code = entry.Code.Trim().ToUpperInvariant();
                        _customCurrencyList.Add(new CalculatorCustomCurrencyItem(code, entry.UsdRate));
                    }
                }

                if (settings.CustomRateProfiles != null)
                {
                    foreach (CustomRateProfileEntryModel entry in settings.CustomRateProfiles)
                    {
                        if (entry == null || string.IsNullOrWhiteSpace(entry.Id))
                            continue;
                        _customRateProfileList.Add(new CalculatorCustomRateProfileItem(
                            entry.Id,
                            entry.Name,
                            entry.BaseLabel,
                            entry.QuoteLabel,
                            entry.Rate));
                    }
                }

                _customCurrencyList.Sort((left, right) => string.CompareOrdinal(left.Code, right.Code));
                _customRateProfileList.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));
            }
            catch (Exception)
            {
            }

            NotifyCustomRatesChanged();
        }

        private void PersistCustomRates()
        {
            if (_settingsService == null)
                return;

            try
            {
                SettingsModel settings = _settingsService.LoadSettingsOrDefault();
                settings.CustomCurrencyRates = _customCurrencyList
                    .Select(item => new CurrencyRateEntryModel { Code = item.Code, UsdRate = item.UsdRate })
                    .ToList();
                settings.CustomRateProfiles = _customRateProfileList
                    .Select(item => new CustomRateProfileEntryModel
                    {
                        Id = item.Id,
                        Name = item.Name,
                        BaseLabel = item.BaseLabel,
                        QuoteLabel = item.QuoteLabel,
                        Rate = item.Rate,
                    })
                    .ToList();
                _settingsService.TrySaveSettingsToLocalDevice(settings);
            }
            catch (Exception)
            {
            }
        }

        private void NotifyCustomRatesChanged()
        {
            OnPropertyChanged(
                nameof(CustomCurrencyRates),
                nameof(HasCustomCurrencyRates),
                nameof(CustomRateProfiles),
                nameof(HasCustomRateProfiles),
                nameof(CurrencyCodes));
        }

        private void SaveCustomCurrency()
        {
            string code = CustomCurrencyCode?.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(code) || code.Length > 12)
                return;

            if (!double.TryParse(CustomCurrencyUsdRate, NumberStyles.Float, CultureInfo.InvariantCulture, out double usdRate)
                && !double.TryParse(CustomCurrencyUsdRate, NumberStyles.Float, CultureInfo.CurrentCulture, out usdRate))
            {
                return;
            }

            if (usdRate <= 0)
                return;

            int index = _customCurrencyList.FindIndex(item => string.Equals(item.Code, code, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
                _customCurrencyList[index] = new CalculatorCustomCurrencyItem(code, usdRate);
            else
                _customCurrencyList.Add(new CalculatorCustomCurrencyItem(code, usdRate));
            _customCurrencyList.Sort((left, right) => string.CompareOrdinal(left.Code, right.Code));

            CustomCurrencyCode = string.Empty;
            CustomCurrencyUsdRate = string.Empty;
            PersistCustomRates();
            NotifyCustomRatesChanged();
            RecalculateConverter();
        }

        private void DeleteCustomCurrency(CalculatorCustomCurrencyItem item)
        {
            if (item == null)
                return;
            _customCurrencyList.RemoveAll(entry => string.Equals(entry.Code, item.Code, StringComparison.OrdinalIgnoreCase));
            PersistCustomRates();
            NotifyCustomRatesChanged();
            RecalculateConverter();
        }

        private void SaveCustomRateProfile()
        {
            string name = CustomProfileName?.Trim();
            if (string.IsNullOrWhiteSpace(name))
                return;

            if (!double.TryParse(CustomProfileRate, NumberStyles.Float, CultureInfo.InvariantCulture, out double rate)
                && !double.TryParse(CustomProfileRate, NumberStyles.Float, CultureInfo.CurrentCulture, out rate))
            {
                return;
            }

            if (rate <= 0)
                return;

            string id = Guid.NewGuid().ToString("N");
            string baseLabel = string.IsNullOrWhiteSpace(CustomProfileBaseLabel) ? "Base" : CustomProfileBaseLabel.Trim();
            string quoteLabel = string.IsNullOrWhiteSpace(CustomProfileQuoteLabel) ? "Quote" : CustomProfileQuoteLabel.Trim();
            _customRateProfileList.Add(new CalculatorCustomRateProfileItem(id, name, baseLabel, quoteLabel, rate));
            _customRateProfileList.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));

            CustomProfileName = string.Empty;
            CustomProfileBaseLabel = string.Empty;
            CustomProfileQuoteLabel = string.Empty;
            CustomProfileRate = string.Empty;
            PersistCustomRates();
            NotifyCustomRatesChanged();
            RecalculateConverter();
        }

        private void DeleteCustomRateProfile(CalculatorCustomRateProfileItem item)
        {
            if (item == null)
                return;
            _customRateProfileList.RemoveAll(entry => string.Equals(entry.Id, item.Id, StringComparison.Ordinal));
            if (string.Equals(_selectedCustomRateProfileId, item.Id, StringComparison.Ordinal))
                SelectedCustomRateProfileId = string.Empty;
            PersistCustomRates();
            NotifyCustomRatesChanged();
            RecalculateConverter();
        }

        private void ResetDateTools()
        {
            if (string.IsNullOrEmpty(_dateFrom))
                _dateFrom = DateIntervalEngine.ToInputValue(DateTime.Now);
            if (string.IsNullOrEmpty(_dateTo))
                _dateTo = DateIntervalEngine.ToInputValue(DateTime.Now.AddDays(1));
            OnPropertyChanged(nameof(DateFrom), nameof(DateTo), nameof(DateAmount), nameof(DateUnit), nameof(DateUnits));
            UpdateDateResult();
        }

        private void UpdateDateResult()
        {
            if (!DateIntervalEngine.TryParseDate(_dateFrom, out DateTime from) ||
                !DateIntervalEngine.TryParseDate(_dateTo, out DateTime to))
            {
                DateResult = string.Empty;
                return;
            }

            DateResult = DateIntervalEngine.Describe(from, to);
        }

        private void ShiftDate()
        {
            if (!DateIntervalEngine.TryParseDate(_dateFrom, out DateTime from) ||
                !double.TryParse(_dateAmount, NumberStyles.Float, CultureInfo.InvariantCulture, out double amount))
            {
                DateResult = string.Empty;
                return;
            }

            DateTime? shifted = DateIntervalEngine.Shift(from, amount, _dateUnit);
            if (shifted == null)
            {
                DateResult = string.Empty;
                return;
            }

            _dateTo = DateIntervalEngine.ToInputValue(shifted.Value);
            OnPropertyChanged(nameof(DateTo));
            UpdateDateResult();
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

    /// <summary>
    /// A named variable of the programmer calculator.
    /// </summary>
    public class CalculatorVariableItem
    {
        public CalculatorVariableItem(string name, double value)
        {
            Name = name;
            Value = value;
        }

        public string Name { get; }

        public double Value { get; }

        public string DisplayText
        {
            get { return Name + " = " + Value.ToString("0.##########", CultureInfo.InvariantCulture); }
        }
    }

    /// <summary>
    /// A unit which can be used to shift a date in the calculator.
    /// </summary>
    public class CalculatorDateUnit
    {
        public CalculatorDateUnit(string id, string label)
        {
            Id = id;
            Label = label;
        }

        public string Id { get; }

        public string Label { get; }
    }

    /// <summary>
    /// A custom currency rate stored for offline calculator conversion.
    /// </summary>
    public class CalculatorCustomCurrencyItem
    {
        public CalculatorCustomCurrencyItem(string code, double usdRate)
        {
            Code = code;
            UsdRate = usdRate;
        }

        public string Code { get; }

        public double UsdRate { get; }

        public string DisplayText =>
            Code + " = " + UsdRate.ToString("0.########", CultureInfo.InvariantCulture) + " USD";
    }

    /// <summary>
    /// A named custom rate profile (e.g. kWh to currency).
    /// </summary>
    public class CalculatorCustomRateProfileItem
    {
        public CalculatorCustomRateProfileItem(string id, string name, string baseLabel, string quoteLabel, double rate)
        {
            Id = id;
            Name = name;
            BaseLabel = baseLabel;
            QuoteLabel = quoteLabel;
            Rate = rate;
        }

        public string Id { get; }

        public string Name { get; }

        public string BaseLabel { get; }

        public string QuoteLabel { get; }

        public double Rate { get; }

        public string DisplayText =>
            Name + ": 1 " + BaseLabel + " = " + Rate.ToString("0.########", CultureInfo.InvariantCulture) + " " + QuoteLabel;
    }
}
