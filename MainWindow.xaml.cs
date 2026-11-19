using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Navigation;
using Microsoft.Win32;

namespace LiveTranscriber;

public partial class MainWindow : Window
{
    private const int MinContextSeconds = 5;
    private const int MaxContextSeconds = 1800;
    private const int MaxTransparency = 80;
    private const int MaxContextLength = 1_000_000;
    private const int CharsPerToken = 3;
    private const int ManyTokens = 100_000;
    private const int StyleIndex = -16;
    private const int MaximizeBox = 0x00010000;
    private const int SystemCommand = 0x0112;
    private const int MaximizeCommand = 0xF030;
    private const double FullMinWidth = 920;
    private const double FullMinHeight = 480;
    private const double CompactMinWidth = 520;
    private const double CompactMinHeight = 200;
    private const double LeftColumnMinWidth = 260;
    private const double SplitterWidth = 14;

    private static readonly Color PanelColor = Color.FromRgb(0x0F, 0x1A, 0x22);
    private static readonly Color FieldColor = Color.FromRgb(0x16, 0x24, 0x2D);

    private readonly Assistant assistant;
    private readonly Settings settings;
    private readonly string contextPath;
    private readonly string preferencesPath;
    private readonly UserPreferences preferences;
    private readonly DoubleAnimation thinkingPulse = new(1, 0.3, TimeSpan.FromMilliseconds(650))
    {
        AutoReverse = true,
        RepeatBehavior = RepeatBehavior.Forever,
        EasingFunction = new SineEase()
    };

    private GlobalHotkey? hotkey;
    private string lastAnswer = string.Empty;
    private bool asking;
    private bool keySaved;
    private Size fullSize;
    private GridLength leftColumnWidth;

    public MainWindow(Assistant assistant, Settings settings)
    {
        InitializeComponent();

        this.assistant = assistant;
        this.settings = settings;
        contextPath = Path.Combine(settings.DataFolder, "context.txt");
        preferencesPath = Path.Combine(settings.DataFolder, "preferences.json");
        preferences = UserPreferences.Load(preferencesPath);
        CaptureProtection.Install(preferences.HideFromCapture);

        if (File.Exists(contextPath))
        {
            ContextBox.Text = File.ReadAllText(contextPath);
        }

        assistant.TranscriptAdded += line => Dispatcher.InvokeAsync(() => Append(TranscriptBox, line));
        assistant.ErrorOccurred += message => Dispatcher.InvokeAsync(() => SetStatus(message, isError: true));

        ApplyContextSeconds(preferences.ContextSeconds);
        TransparencySlider.Value = preferences.Transparency;
        ApplyTransparency(preferences.Transparency);
        TransparencySlider.ValueChanged += (_, e) => ApplyTransparency((int)Math.Round(e.NewValue));
        ApplyPin();

        PromptBox.Text = preferences.CustomPrompt ?? settings.SystemPrompt;
        assistant.SystemPrompt = PromptBox.Text;
        BuildLanguageChips();

        fullSize = new Size(Width, Height);
        leftColumnWidth = LeftColumn.Width;
        AnswersOnlyBox.IsChecked = preferences.AnswersOnly;

        if (preferences.AnswersOnly)
        {
            ApplyAnswersOnly(true);
            Width = preferences.CompactWidth;
            Height = preferences.CompactHeight;
        }

        if (TrySetHotkey(preferences.HotkeyModifiers, preferences.HotkeyKey))
        {
            SetStatus($"Cue слухає. Натисни {hotkey!.Description} у будь-якому вікні, щоб отримати підказку", isError: false);
        }

        keySaved = ApiKeyStore.Load(settings.DataFolder) is not null;
        UpdateApiKeyStatus();
        UpdateState();

        if (!assistant.HasApiKey)
        {
            Loaded += (_, _) => RequireKey();
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var handle = new WindowInteropHelper(this).Handle;
        SetWindowLong(handle, StyleIndex, GetWindowLong(handle, StyleIndex) & ~MaximizeBox);
        HwndSource.FromHwnd(handle).AddHook(BlockMaximize);

        if (!ApplyCaptureProtection())
        {
            SetStatus("Windows не дозволила сховати вікно від демонстрації екрана", isError: true);
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr window, int index, int value);

    private static IntPtr BlockMaximize(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == SystemCommand && (wParam.ToInt64() & 0xFFF0) == MaximizeCommand)
        {
            handled = true;
        }

        return IntPtr.Zero;
    }

    private Brush Res(string key) => (Brush)FindResource(key);

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnCloseWindow(object sender, RoutedEventArgs e) => Close();

    private void OnTogglePin(object sender, RoutedEventArgs e)
    {
        preferences.AlwaysOnTop = !preferences.AlwaysOnTop;
        ApplyPin();
        SavePreferences();
        SetStatus(preferences.AlwaysOnTop ? "Cue тепер завжди поверх інших вікон" : "Cue більше не закріплений поверх вікон", isError: false);
    }

    private void OnToggleSettings(object sender, RoutedEventArgs e) => SetSettingsOpen(SettingsPanel.Visibility != Visibility.Visible);

    private void OnCloseSettings(object sender, RoutedEventArgs e) => SetSettingsOpen(false);

    private void SetSettingsOpen(bool open)
    {
        if (!open && SettingsPanel.Visibility == Visibility.Visible)
        {
            CommitPrompt();
        }

        SettingsPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        ContentGrid.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
        QuestionBar.Visibility = open || preferences.AnswersOnly ? Visibility.Collapsed : Visibility.Visible;

        if (open)
        {
            SettingsButton.BorderBrush = Res("CueBrush");
        }
        else
        {
            SettingsButton.ClearValue(BorderBrushProperty);
        }
    }

    private void OnAnswersOnlyChanged(object sender, RoutedEventArgs e)
    {
        var compact = AnswersOnlyBox.IsChecked == true;

        if (compact == preferences.AnswersOnly)
        {
            return;
        }

        if (compact)
        {
            fullSize = new Size(ActualWidth, ActualHeight);
        }
        else
        {
            RememberCompactSize();
        }

        preferences.AnswersOnly = compact;
        ApplyAnswersOnly(compact);
        Width = compact ? preferences.CompactWidth : fullSize.Width;
        Height = compact ? preferences.CompactHeight : fullSize.Height;
        SavePreferences();
    }

    private void ApplyAnswersOnly(bool compact)
    {
        var visibility = compact ? Visibility.Collapsed : Visibility.Visible;

        if (compact)
        {
            SetSettingsOpen(false);
        }

        foreach (var element in new UIElement[] { TitleText, StateText, TransparencyLabel, ControlsBar, LeftPanel, ColumnSplitter, AnswersHeader, QuestionBar })
        {
            element.Visibility = visibility;
        }

        if (compact)
        {
            leftColumnWidth = LeftColumn.Width;
            LeftColumn.MinWidth = 0;
            LeftColumn.Width = new GridLength(0);
            SplitterColumn.Width = new GridLength(0);
            MinWidth = CompactMinWidth;
            MinHeight = CompactMinHeight;
        }
        else
        {
            LeftColumn.Width = leftColumnWidth;
            LeftColumn.MinWidth = LeftColumnMinWidth;
            SplitterColumn.Width = new GridLength(SplitterWidth);
            MinWidth = FullMinWidth;
            MinHeight = FullMinHeight;
        }

        UpdateHotkeyText();
    }

    private void RememberCompactSize()
    {
        preferences.CompactWidth = ActualWidth;
        preferences.CompactHeight = ActualHeight;
    }

    private void ApplyPin()
    {
        Topmost = preferences.AlwaysOnTop;
        PinButton.Content = preferences.AlwaysOnTop ? "" : "";
        PinButton.ToolTip = preferences.AlwaysOnTop
            ? "Cue закріплений поверх усіх вікон. Клацни, щоб відкріпити"
            : "Закріпити Cue поверх усіх вікон, щоб він не ховався за дзвінком";

        if (preferences.AlwaysOnTop)
        {
            PinButton.Foreground = Res("CueBrush");
        }
        else
        {
            PinButton.ClearValue(ForegroundProperty);
        }
    }

    private void ApplyTransparency(int percent)
    {
        var value = Math.Clamp(percent, 0, MaxTransparency);
        var alpha = (byte)Math.Round((100 - value) * 255 / 100.0);

        Resources["PanelBrush"] = new SolidColorBrush(Color.FromArgb(alpha, PanelColor.R, PanelColor.G, PanelColor.B));
        Resources["FieldBrush"] = new SolidColorBrush(Color.FromArgb(alpha, FieldColor.R, FieldColor.G, FieldColor.B));
        TransparencyText.Text = $"{value}%";
        preferences.Transparency = value;
    }

    private async Task AskAsync(string? question)
    {
        if (asking)
        {
            return;
        }

        if (!assistant.HasApiKey)
        {
            RequireKey();
            return;
        }

        asking = true;
        AskButton.IsEnabled = false;
        UpdateState();
        SetStatus("Готую підказку…", isError: false);

        try
        {
            var answer = await assistant.AskAsync(ContextBox.Text, question);

            if (answer is null)
            {
                SetStatus("Поки нічого нового не прозвучало. Дочекайся питання або напиши його внизу", isError: false);
                return;
            }

            lastAnswer = answer;
            var title = question is null ? $"{DateTime.Now:HH:mm}" : $"{DateTime.Now:HH:mm}   «{question}»";
            Append(AnswersBox, $"{title}{Environment.NewLine}{answer.Trim()}{Environment.NewLine}");
            SetStatus("Підказка готова", isError: false);
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message, isError: true);
        }
        finally
        {
            asking = false;
            AskButton.IsEnabled = true;
            UpdateState();
        }
    }

    private async void OnAsk(object sender, RoutedEventArgs e) => await AskAsync(null);

    private async void OnSend(object sender, RoutedEventArgs e) => await SendQuestionAsync();

    private async void OnQuestionKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await SendQuestionAsync();
        }
    }

    private async Task SendQuestionAsync()
    {
        var question = QuestionBox.Text.Trim();

        if (question.Length == 0 || asking)
        {
            return;
        }

        QuestionBox.Clear();
        await AskAsync(question);
    }

    private bool TrySetHotkey(ModifierKeys modifiers, Key key)
    {
        if (hotkey is not null && hotkey.Modifiers == modifiers && hotkey.Key == key)
        {
            UpdateHotkeyText();
            return true;
        }

        try
        {
            var created = new GlobalHotkey(modifiers, key);
            created.Pressed += () => Dispatcher.InvokeAsync(() => _ = AskAsync(null));

            hotkey?.Dispose();
            hotkey = created;
            preferences.HotkeyModifiers = modifiers;
            preferences.HotkeyKey = key;
            SavePreferences();
            return true;
        }
        catch (InvalidOperationException exception)
        {
            SetStatus($"{exception.Message}. Обери іншу в налаштуваннях", isError: true);
            return false;
        }
        finally
        {
            UpdateHotkeyText();
        }
    }

    private void OnHotkeyFocus(object sender, KeyboardFocusChangedEventArgs e) => HotkeyBox.Text = "Натисни комбінацію…";

    private void OnHotkeyBlur(object sender, KeyboardFocusChangedEventArgs e) => UpdateHotkeyText();

    private void OnHotkeyKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;

        if (key == Key.Tab && modifiers == ModifierKeys.None)
        {
            return;
        }

        e.Handled = true;

        if (IsModifierKey(key))
        {
            return;
        }

        if (key == Key.Escape)
        {
            Keyboard.ClearFocus();
            return;
        }

        if (modifiers == ModifierKeys.None && key is not (>= Key.F1 and <= Key.F24))
        {
            SetStatus("Додай Ctrl, Alt або Shift. Без них підходять лише F1–F24", isError: true);
            return;
        }

        if (TrySetHotkey(modifiers, key))
        {
            SetStatus($"Тепер підказка за {hotkey!.Description}", isError: false);
        }

        Keyboard.ClearFocus();
    }

    private void OnSecondsInput(object sender, TextCompositionEventArgs e) => e.Handled = !e.Text.All(char.IsDigit);

    private void OnSecondsKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Keyboard.ClearFocus();
        }
    }

    private void OnSecondsBlur(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!int.TryParse(SecondsBox.Text, out var seconds))
        {
            SecondsBox.Text = preferences.ContextSeconds.ToString();
            return;
        }

        if (seconds != preferences.ContextSeconds)
        {
            ApplyContextSeconds(seconds);
            SetStatus($"Для підказки Cue братиме останні {preferences.ContextSeconds} с розмови", isError: false);
        }
        else
        {
            SecondsBox.Text = seconds.ToString();
        }
    }

    private void ApplyContextSeconds(int seconds)
    {
        var value = Math.Clamp(seconds, MinContextSeconds, MaxContextSeconds);

        assistant.ContextWindow = TimeSpan.FromSeconds(value);
        preferences.ContextSeconds = value;
        SecondsBox.Text = value.ToString();
        SavePreferences();
    }

    private void OnPause(object sender, RoutedEventArgs e)
    {
        assistant.Paused = !assistant.Paused;
        UpdateState();
        SetStatus(assistant.Paused ? "Пауза: Cue нічого не слухає" : "Cue знову слухає", isError: false);
    }

    private void OnToggleCapture(object sender, RoutedEventArgs e)
    {
        preferences.HideFromCapture = !preferences.HideFromCapture;
        SavePreferences();

        if (!ApplyCaptureProtection())
        {
            SetStatus("Windows не дозволила змінити видимість вікна в демонстрації екрана", isError: true);
        }
        else
        {
            SetStatus(preferences.HideFromCapture
                ? "Cue невидимий: у демонстрації екрана й на записі його немає"
                : "Увага: Cue видно в демонстрації екрана", isError: !preferences.HideFromCapture);
        }
    }

    private bool ApplyCaptureProtection()
    {
        var hidden = preferences.HideFromCapture;

        CaptureIcon.Text = hidden ? "" : "";
        CaptureText.Text = hidden ? "Невидимий у демонстрації" : "Видно в демонстрації";
        CaptureIcon.Foreground = hidden ? Res("CueBrush") : Res("AlertBrush");
        CaptureButton.ToolTip = hidden
            ? "Ти бачиш Cue, а в Zoom, Teams, Meet, Discord, OBS і на скріншотах його немає. Клацни, щоб показати"
            : "Cue зараз потрапляє в демонстрацію екрана. Клацни, щоб сховати";
        CaptureIconButton.Content = CaptureIcon.Text;
        CaptureIconButton.Foreground = CaptureIcon.Foreground;
        CaptureIconButton.ToolTip = CaptureButton.ToolTip;

        if (hidden)
        {
            CaptureText.ClearValue(TextBlock.ForegroundProperty);
        }
        else
        {
            CaptureText.Foreground = Res("AlertBrush");
        }

        return CaptureProtection.Apply(this, hidden);
    }

    private void BuildLanguageChips()
    {
        var conversation = preferences.ConversationLanguage ?? settings.Language ?? Languages.Auto;

        AddChip(ConversationLanguages, Languages.Auto, "Авто", conversation, OnConversationLanguageChecked);
        AddChip(AnswerLanguages, Languages.SameAsConversation, "Як розмова", preferences.AnswerLanguage, OnAnswerLanguageChecked);

        foreach (var language in Languages.All)
        {
            AddChip(ConversationLanguages, language.Code, language.Label, conversation, OnConversationLanguageChecked);
            AddChip(AnswerLanguages, language.Code, language.Label, preferences.AnswerLanguage, OnAnswerLanguageChecked);
        }

        ApplyConversationLanguage(conversation);
        ApplyAnswerLanguage(preferences.AnswerLanguage);
    }

    private void AddChip(WrapPanel panel, string code, string label, string selected, RoutedEventHandler onChecked)
    {
        var chip = new RadioButton
        {
            Content = label,
            Tag = code,
            GroupName = panel.Name,
            Style = (Style)FindResource("Chip"),
            IsChecked = code == selected
        };

        chip.Checked += onChecked;
        panel.Children.Add(chip);
    }

    private void OnConversationLanguageChecked(object sender, RoutedEventArgs e)
    {
        var code = (string)((RadioButton)sender).Tag;

        preferences.ConversationLanguage = code;
        ApplyConversationLanguage(code);
        SavePreferences();
        SetStatus($"Мова розмови: {LanguageLabel(code)}", isError: false);
    }

    private void OnAnswerLanguageChecked(object sender, RoutedEventArgs e)
    {
        var code = (string)((RadioButton)sender).Tag;

        preferences.AnswerLanguage = code;
        ApplyAnswerLanguage(code);
        SavePreferences();
        SetStatus($"Мова підказок: {LanguageLabel(code)}", isError: false);
    }

    private void ApplyConversationLanguage(string code)
    {
        assistant.Language = code == Languages.Auto ? null : code;
        UpdateState();
    }

    private void ApplyAnswerLanguage(string code) => assistant.AnswerLanguage = Languages.Find(code)?.EnglishName;

    private static string LanguageLabel(string code) => code switch
    {
        Languages.Auto => "визначається автоматично",
        Languages.SameAsConversation => "як розмова",
        _ => Languages.Find(code)?.Label ?? code
    };

    private void OnPromptBlur(object sender, KeyboardFocusChangedEventArgs e) => CommitPrompt();

    private void OnResetPrompt(object sender, RoutedEventArgs e)
    {
        PromptBox.Text = settings.SystemPrompt;

        if (!CommitPrompt())
        {
            SetStatus("Зараз і так стандартна інструкція", isError: false);
        }
    }

    private bool CommitPrompt()
    {
        var text = PromptBox.Text.Trim();
        var custom = text.Length == 0 || text == settings.SystemPrompt.Trim() ? null : text;

        if (text.Length == 0)
        {
            PromptBox.Text = settings.SystemPrompt;
        }

        if (custom == preferences.CustomPrompt)
        {
            return false;
        }

        preferences.CustomPrompt = custom;
        assistant.SystemPrompt = custom ?? settings.SystemPrompt;
        SavePreferences();
        SetStatus(custom is null ? "Повернуто стандартну інструкцію" : "Інструкцію збережено. Наступні підказки будуть за нею", isError: false);
        return true;
    }

    private void RequireKey()
    {
        if (preferences.AnswersOnly)
        {
            SetStatus("Потрібен ключ OpenAI: зніми галочку «Лише підказки» й відкрий налаштування", isError: true);
            return;
        }

        SetSettingsOpen(true);
        ApiKeyBox.Focus();
        SetStatus("Щоб Cue запрацював, встав свій ключ OpenAI в налаштуваннях", isError: true);
    }

    private void OnApiKeyChanged(object sender, RoutedEventArgs e) =>
        ApiKeyHint.Visibility = ApiKeyBox.Password.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    private async void OnApiKeyKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await SaveApiKeyAsync();
        }
    }

    private async void OnSaveApiKey(object sender, RoutedEventArgs e) => await SaveApiKeyAsync();

    private async Task SaveApiKeyAsync()
    {
        var key = ApiKeyBox.Password.Trim();

        if (key.Length == 0)
        {
            SetStatus("Спершу встав ключ у поле", isError: true);
            ApiKeyBox.Focus();
            return;
        }

        SaveKeyButton.IsEnabled = false;
        SetStatus("Перевіряю ключ…", isError: false);

        try
        {
            HttpStatusCode? status;

            try
            {
                status = await assistant.CheckKeyAsync(key);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                status = null;
            }

            if (status == HttpStatusCode.Unauthorized)
            {
                SetStatus("OpenAI не прийняв цей ключ. Перевір, чи скопійовано його повністю", isError: true);
                return;
            }

            ApiKeyStore.Save(settings.DataFolder, key);
            assistant.ApiKey = key;
            keySaved = true;
            ApiKeyBox.Clear();
            UpdateApiKeyStatus();
            UpdateState();

            if (status == HttpStatusCode.OK)
            {
                SetStatus("Ключ працює і збережений. Cue готовий підказувати", isError: false);
            }
            else if (status is null)
            {
                SetStatus("Ключ збережено, але перевірити його зараз не вдалося: немає зв'язку з OpenAI", isError: true);
            }
            else
            {
                SetStatus($"Ключ збережено, але OpenAI відповів кодом {(int)status}. Якщо підказки не працюватимуть, перевір права ключа", isError: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException)
        {
            SetStatus($"Не вдалося зберегти ключ: {exception.Message}", isError: true);
        }
        finally
        {
            SaveKeyButton.IsEnabled = true;
        }
    }

    private void OnDeleteApiKey(object sender, RoutedEventArgs e)
    {
        try
        {
            ApiKeyStore.Delete(settings.DataFolder);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SetStatus($"Не вдалося видалити ключ: {exception.Message}", isError: true);
            return;
        }

        keySaved = false;
        assistant.ApiKey = settings.EnvironmentApiKey ?? string.Empty;
        UpdateApiKeyStatus();
        UpdateState();
        SetStatus(assistant.HasApiKey
            ? "Ключ видалено. Зараз Cue бере ключ зі змінної середовища OPENAI_API_KEY"
            : "Ключ видалено з цього комп'ютера. Без ключа Cue не працює", isError: !assistant.HasApiKey);
    }

    private void UpdateApiKeyStatus()
    {
        ApiKeyStatus.Text = keySaved ? $"Збережено: {ApiKeyStore.Mask(assistant.ApiKey)}"
            : assistant.HasApiKey ? $"Зі змінної OPENAI_API_KEY: {ApiKeyStore.Mask(assistant.ApiKey)}"
            : "Ключа ще немає";
        ApiKeyStatus.Foreground = assistant.HasApiKey ? Res("MistBrush") : Res("AlertBrush");
        DeleteKeyButton.Visibility = keySaved ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnOpenLink(object sender, RequestNavigateEventArgs e)
    {
        e.Handled = true;

        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Win32Exception exception)
        {
            SetStatus($"Не вдалося відкрити браузер: {exception.Message}. Адреса: {e.Uri.AbsoluteUri}", isError: true);
        }
    }

    private void OnClearTranscript(object sender, RoutedEventArgs e)
    {
        assistant.ClearTranscript();
        TranscriptBox.Clear();
        SetStatus("Розмову очищено", isError: false);
    }

    private void OnClearContext(object sender, RoutedEventArgs e)
    {
        ContextBox.Clear();
        SetStatus("Матеріали очищено", isError: false);
    }

    private void OnClearAnswers(object sender, RoutedEventArgs e)
    {
        assistant.ClearConversation();
        AnswersBox.Clear();
        lastAnswer = string.Empty;
        SetStatus("Почали з чистого аркуша: Cue забув попередні підказки", isError: false);
    }

    private void OnCopyLast(object sender, RoutedEventArgs e)
    {
        if (lastAnswer.Length == 0)
        {
            SetStatus("Поки немає підказки, яку можна скопіювати", isError: false);
            return;
        }

        try
        {
            Clipboard.SetText(lastAnswer);
            SetStatus("Підказку скопійовано", isError: false);
        }
        catch (Exception exception)
        {
            SetStatus($"Не вдалося скопіювати: {exception.Message}", isError: true);
        }
    }

    private async void OnAddFile(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Filter = "Текстові файли|*.txt;*.md;*.cs;*.json;*.xml;*.csv;*.log;*.py;*.js;*.ts;*.html;*.css;*.sql|Усі файли|*.*"
        };

        if (dialog.ShowDialog(this) == true)
        {
            await AddToContextAsync(dialog.FileNames);
        }
    }

    private async void OnAddFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Multiselect = true, Title = "Папка проєкту" };

        if (dialog.ShowDialog(this) == true)
        {
            await AddToContextAsync(dialog.FolderNames);
        }
    }

    private void OnContextChanged(object sender, TextChangedEventArgs e)
    {
        var tokens = ContextBox.Text.Length / CharsPerToken;

        ContextSizeText.Text = ContextBox.Text.Length == 0 ? string.Empty
            : tokens < 1000 ? "≈ менше 1 тис. токенів"
            : $"≈ {tokens / 1000} тис. токенів";
        ContextSizeText.Foreground = tokens > ManyTokens ? Res("AlertBrush") : Res("MistBrush");
    }

    private void OnContextDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private async void OnContextDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            e.Handled = true;
            await AddToContextAsync(paths);
        }
    }

    private async Task AddToContextAsync(IReadOnlyCollection<string> paths)
    {
        SetStatus("Читаю файли…", isError: false);

        ContextCollection result;
        var budget = MaxContextLength - ContextBox.Text.Length;

        try
        {
            result = await Task.Run(() => ContextCollector.Collect(paths, budget));
        }
        catch (Exception exception)
        {
            SetStatus($"Не вдалося прочитати: {exception.Message}", isError: true);
            return;
        }

        if (result.Files.Count > 0)
        {
            var text = new StringBuilder();

            foreach (var file in result.Files)
            {
                if (ContextBox.Text.Length > 0 || text.Length > 0)
                {
                    text.Append(Environment.NewLine).Append(Environment.NewLine);
                }

                text.Append($"=== {file.Name} ==={Environment.NewLine}{file.Text.TrimEnd()}");
            }

            ContextBox.AppendText(text.ToString());
            ContextBox.ScrollToEnd();
        }

        var skipped = result.TooLarge.Concat(result.Unreadable).ToList();
        var added = result.Files.Count == 1 ? $"Додано {result.Files[0].Name}" : $"Додано файлів: {result.Files.Count}";

        if (result.LimitReached)
        {
            SetStatus($"{added}. Далі зупинився: матеріалів уже забагато, додай лише потрібні папки", isError: true);
        }
        else if (skipped.Count > 0)
        {
            SetStatus($"{added}. Пропущено завеликі або недоступні: {string.Join(", ", skipped.Take(5))}{(skipped.Count > 5 ? $" і ще {skipped.Count - 5}" : string.Empty)}", isError: true);
        }
        else if (result.Files.Count == 0)
        {
            SetStatus("Не знайшов файлів з кодом чи текстом", isError: true);
        }
        else if (ContextBox.Text.Length / CharsPerToken > ManyTokens)
        {
            SetStatus($"{added}. Матеріалів забагато: підказки будуть повільніші й дорожчі", isError: true);
        }
        else
        {
            SetStatus(added, isError: false);
        }
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        Directory.CreateDirectory(settings.DataFolder);
        File.WriteAllText(contextPath, ContextBox.Text);
        CommitPrompt();

        if (preferences.AnswersOnly)
        {
            RememberCompactSize();
        }

        SavePreferences();
        hotkey?.Dispose();
        assistant.Dispose();
    }

    private void SavePreferences()
    {
        try
        {
            preferences.Save(preferencesPath);
        }
        catch (IOException exception)
        {
            SetStatus($"Не вдалося зберегти налаштування: {exception.Message}", isError: true);
        }
    }

    private void UpdateHotkeyText()
    {
        var description = hotkey?.Description;

        AskHotkeyText.Text = description ?? string.Empty;
        AskHotkeyChip.Visibility = description is null ? Visibility.Collapsed : Visibility.Visible;
        AnswersHint.Text = description is null
            ? "Коли співрозмовник поставить питання, натисни «Підказати», і тут з'явиться коротка відповідь."
            : $"Коли співрозмовник поставить питання, натисни {description}, і тут з'явиться коротка відповідь.";

        if (!preferences.AnswersOnly)
        {
            AnswersHint.Text += " Або спитай своїми словами внизу.";
        }

        if (!HotkeyBox.IsKeyboardFocused)
        {
            HotkeyBox.Text = description ?? "не задано";
        }
    }

    private void UpdateState()
    {
        var paused = assistant.Paused;
        var details = $"Слухаю звук комп'ютера ({assistant.Format}), мікрофон не записую.{Environment.NewLine}" +
                      $"Розпізнавання: {settings.TranscribeModel}, мова {LanguageLabel(assistant.Language ?? Languages.Auto)}. Підказки: {settings.ChatModel}.";

        PauseIcon.Text = paused ? "" : "";
        PauseText.Text = paused ? "Слухати" : "Пауза";

        if (!assistant.HasApiKey)
        {
            LogoLight.Fill = Res("AlertBrush");
            StateText.Text = "потрібен ключ OpenAI";
            Logo.ToolTip = "Без ключа OpenAI Cue не може розпізнавати розмову й підказувати. Додай його в налаштуваннях";
        }
        else if (asking)
        {
            LogoLight.Fill = Res("StandbyBrush");
            StateText.Text = "готую підказку…";
            Logo.ToolTip = "Cue готує підказку";
        }
        else if (paused)
        {
            LogoLight.Fill = Res("MistBrush");
            StateText.Text = "на паузі";
            Logo.ToolTip = "Пауза: Cue нічого не слухає";
        }
        else
        {
            LogoLight.Fill = Res("CueBrush");
            StateText.Text = "слухаю";
            Logo.ToolTip = details;
        }

        LogoLight.BeginAnimation(OpacityProperty, asking && SystemParameters.ClientAreaAnimation ? thinkingPulse : null);
    }

    private void SetStatus(string text, bool isError)
    {
        StatusText.Text = $"{DateTime.Now:HH:mm}   {text}";
        StatusText.Foreground = isError ? Res("AlertBrush") : Res("MistBrush");
    }

    private static bool IsModifierKey(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

    private static void Append(TextBox box, string text)
    {
        box.AppendText(box.Text.Length > 0 ? Environment.NewLine + text : text);
        box.ScrollToEnd();
    }
}
