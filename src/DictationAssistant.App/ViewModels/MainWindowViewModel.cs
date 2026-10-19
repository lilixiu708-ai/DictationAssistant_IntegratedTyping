using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Media;
using Avalonia.Threading;
using System.Diagnostics;
using ALampy.XamlFontPicker.Avalonia;
using DictationAssistant.App.Services;
using DictationAssistant.App.Settings;
using DictationAssistant.App.Voice;
using DictationAssistant.App.Abstractions;

namespace DictationAssistant.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly EditorDocumentWordListSource _wordListSource;
    private readonly IDictationPlayer _dictationPlayer;
    private readonly AppSettingsStore _settingsStore;
    private readonly VoiceAggregator _aggregator;
    private readonly List<IDisposable> _disposables = [];
    private IVoice _currentVoice;
    private DispatcherTimer? _countdownTimer;

    public event Action<string>? AlertRequested;

    public IDictationPlayer DictationPlayer => _dictationPlayer;

    public MainWindowViewModel(
        EditorDocumentWordListSource wordListSource,
        IAudioPlayer audioPlayer,
        VoiceAggregator aggregator,
        IVoice initialVoice,
        AppSettingsStore settingsStore)
    {
        _wordListSource = wordListSource;
        _settingsStore = settingsStore;
        _aggregator = aggregator;
        _currentVoice = initialVoice;

        _dictationPlayer = new DictationPlayer(_currentVoice, wordListSource, audioPlayer);
        _dictationPlayer.ProgressChanged += (_, progress) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                CurrentLineIndex = progress.CurrentWordIndex;
                CurrentRepeat = progress.CurrentRepeat;

                ProgressText = progress.TotalWords <= 0
                    ? "0 / 0"
                    : $"{Math.Max(progress.CurrentWordIndex + 1, 0)} / {progress.TotalWords}";
                OnPropertyChanged(nameof(IsAutoRunning));
                OnPropertyChanged(nameof(IsAutoPaused));
                OnPropertyChanged(nameof(PauseOrResumeAutoText));
                OnPropertyChanged(nameof(SpeakStateText));
                UpdateCountdownTimer();
            });
        };

        _disposables.Add(_settingsStore.Observe(s => s.Dictation.IntervalExpression, v =>
        {
            OnPropertyChanged(nameof(IntervalExpression));
            ValidateAndApplyWaitingTime(v);
        }));
        _disposables.Add(_settingsStore.Observe(s => s.Dictation.TimesPerWord, v =>
        {
            OnPropertyChanged(nameof(TimesPerWord));
            _dictationPlayer.TimesPerWord = v;
        }));
        _disposables.Add(_settingsStore.Observe(s => s.Dictation.HighlightCurrentLine, _ =>
        {
            OnPropertyChanged(nameof(HighlightCurrentLine));
        }));
        _disposables.Add(_settingsStore.Observe(s => s.Dictation.AutoScrollToCurrentLine, _ =>
        {
            OnPropertyChanged(nameof(AutoScrollCurrentLine));
        }));
        _disposables.Add(_settingsStore.Observe(s => s.Dictation.Volume, v =>
        {
            OnPropertyChanged(nameof(Volume));
            _dictationPlayer.Volume = v;
        }));
        _disposables.Add(_settingsStore.Observe(s => s.Dictation.Rate, v =>
        {
            OnPropertyChanged(nameof(Rate));
            _dictationPlayer.Rate = v;
        }));

        _disposables.Add(_settingsStore.Observe(s => s.Preference.EditorFont, _ =>
        {
            OnPropertyChanged(nameof(EditorFontInfo));
        }));
        _disposables.Add(_settingsStore.Observe(s => s.Preference.ImprovedResourcePath, _ =>
        {
            OnPropertyChanged(nameof(ImprovedResourcePath));
            UpdateEffectiveVoice();
        }));
        _disposables.Add(_settingsStore.Observe(s => s.Preference.DefaultChineseVoiceId, _ =>
        {
            OnPropertyChanged(nameof(DefaultChineseVoiceId));
        }));
        _disposables.Add(_settingsStore.Observe(s => s.Preference.DefaultEnglishVoiceId, _ =>
        {
            OnPropertyChanged(nameof(DefaultEnglishVoiceId));
        }));

        ValidateAndApplyWaitingTime(_settingsStore.Value.Dictation.IntervalExpression);
        _dictationPlayer.TimesPerWord = _settingsStore.Value.Dictation.TimesPerWord;
        _dictationPlayer.Volume = _settingsStore.Value.Dictation.Volume;
        _dictationPlayer.Rate = _settingsStore.Value.Dictation.Rate;

        _ = LoadVoiceOptionsAsync();
        OnPropertyChanged(nameof(SpeakStateText));
    }

    public EditorDocumentWordListSource WordListSource => _wordListSource;

    public void Dispose()
    {
        if (_disposables is not null)
        {
            foreach (var d in _disposables)
            {
                d?.Dispose();
            }
            _disposables.Clear();
        }
    }

    public string IntervalExpression
    {
        get => _settingsStore.Value.Dictation.IntervalExpression;
        set => _settingsStore.Update(s => s with { Dictation = s.Dictation with { IntervalExpression = value } });
    }

    [ObservableProperty]
    private string _intervalValidationHint = string.Empty;

    public int TimesPerWord
    {
        get => _settingsStore.Value.Dictation.TimesPerWord;
        set => _settingsStore.Update(s => s with { Dictation = s.Dictation with { TimesPerWord = value } });
    }

    public bool HighlightCurrentLine
    {
        get => _settingsStore.Value.Dictation.HighlightCurrentLine;
        set => _settingsStore.Update(s => s with { Dictation = s.Dictation with { HighlightCurrentLine = value } });
    }

    public bool AutoScrollCurrentLine
    {
        get => _settingsStore.Value.Dictation.AutoScrollToCurrentLine;
        set => _settingsStore.Update(s => s with { Dictation = s.Dictation with { AutoScrollToCurrentLine = value } });
    }

    [ObservableProperty]
    private int _currentLineIndex = -1;

    [ObservableProperty]
    private int _currentRepeat;

    [ObservableProperty]
    private string _progressText = "0 / 0";

    [ObservableProperty]
    private bool _wordListVisible = true;

    public int Volume
    {
        get => _settingsStore.Value.Dictation.Volume;
        set => _settingsStore.Update(s => s with { Dictation = s.Dictation with { Volume = value } });
    }

    public int Rate
    {
        get => _settingsStore.Value.Dictation.Rate;
        set => _settingsStore.Update(s => s with { Dictation = s.Dictation with { Rate = value } });
    }

    [ObservableProperty]
    private double _mainWindowWidth = 980;

    [ObservableProperty]
    private double _mainWindowHeight = 680;

    public PickedFontInfo EditorFontInfo
    {
        get => _settingsStore.Value.Preference.EditorFont;
        set => _settingsStore.Update(s => s with { Preference = s.Preference with { EditorFont = value ?? new PickedFontInfo() } });
    }

    public string ImprovedResourcePath
    {
        get => _settingsStore.Value.Preference.ImprovedResourcePath;
        set => _settingsStore.Update(s => s with { Preference = s.Preference with { ImprovedResourcePath = value } });
    }

    public string DefaultChineseVoiceId
    {
        get => _settingsStore.Value.Preference.DefaultChineseVoiceId;
        set => _settingsStore.Update(s => s with { Preference = s.Preference with { DefaultChineseVoiceId = value } });
    }

    public string DefaultEnglishVoiceId
    {
        get => _settingsStore.Value.Preference.DefaultEnglishVoiceId;
        set => _settingsStore.Update(s => s with { Preference = s.Preference with { DefaultEnglishVoiceId = value } });
    }

    [ObservableProperty]
    private ObservableCollection<IVoiceFactory> _voiceOptions = [];

    [ObservableProperty]
    private IVoiceFactory? _selectedVoiceFactory;

    public bool IsAutoRunning => _dictationPlayer.AutoMode && !_dictationPlayer.IsPaused;

    public bool IsAutoPaused => _dictationPlayer.IsPaused;

    public string ShowOrHideWordListText => WordListVisible ? "隐藏词语列表(_W)" : "显示词语列表(_W)";

    public string PauseOrResumeAutoText => IsAutoPaused ? "恢复自动播报(_R)" : "暂停自动播报(_P)";

    public string SpeakStateText
    {
        get
        {
            if (IsAutoPaused)
            {
                return "自动播报已暂停";
            }

            var progress = _dictationPlayer.Progress;

            if (_dictationPlayer.IsSpeaking)
            {
                var speakingIndex = Math.Max(progress.CurrentWordIndex + 1, 1);
                return $"正在播报第{speakingIndex}个";
            }

            if (_dictationPlayer.AutoMode && progress.NextWordIndex.HasValue)
            {
                var nextIndex = progress.NextWordIndex.Value + 1;
                if (progress.NextSpeakTime.HasValue)
                {
                    var remaining = progress.NextSpeakTime.Value - DateTimeOffset.Now;
                    if (remaining.TotalSeconds > 0)
                    {
                        return $"即将播报第{nextIndex}个 ({remaining.TotalSeconds:F1}秒后)";
                    }
                }
                return $"即将播报第{nextIndex}个";
            }

            return "等待播报";
        }
    }

    [RelayCommand]
    private void ToggleWordList()
    {
        WordListVisible = !WordListVisible;
        OnPropertyChanged(nameof(ShowOrHideWordListText));
    }

    private void UpdateCountdownTimer()
    {
        var progress = _dictationPlayer.Progress;
        if (progress.NextSpeakTime.HasValue && _dictationPlayer.AutoMode && !_dictationPlayer.IsPaused)
        {
            if (_countdownTimer is null)
            {
                _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                _countdownTimer.Tick += (_, _) =>
                {
                    OnPropertyChanged(nameof(SpeakStateText));

                    var p = _dictationPlayer.Progress;
                    if (!p.NextSpeakTime.HasValue || !_dictationPlayer.AutoMode || _dictationPlayer.IsPaused)
                    {
                        StopCountdownTimer();
                    }
                };
            }
            _countdownTimer.Start();
        }
        else
        {
            StopCountdownTimer();
        }
    }

    private void StopCountdownTimer()
    {
        _countdownTimer?.Stop();
    }

    [RelayCommand]
    private void PauseOrResumeAuto()
    {
        if (IsAutoPaused)
        {
            ResumeAuto();
            return;
        }

        PauseAuto();
    }

    [RelayCommand]
    private void SpeakPrevious()
    {
        if (_dictationPlayer.Progress.CurrentWordIndex <= 0)
        {
            AlertRequested?.Invoke("已经是第1个了！");
            return;
        }
        _dictationPlayer.SpeakPrevious();
    }

    [RelayCommand]
    private void SpeakAgain()
    {
        if (_dictationPlayer.Progress.CurrentWordIndex < 0 || _dictationPlayer.Progress.CurrentWordIndex >= _wordListSource.Count)
        {
            AlertRequested?.Invoke("还没报过或已移除报过的词语！");
            return;
        }
        _dictationPlayer.SpeakAgain();
    }

    [RelayCommand]
    private void SpeakNext()
    {
        if (_wordListSource.Count == 0)
        {
            AlertRequested?.Invoke("请先添加词语！");
            return;
        }
        if (_dictationPlayer.Progress.CurrentWordIndex >= _wordListSource.Count - 1)
        {
            AlertRequested?.Invoke("已经播完了。");
            return;
        }
        _dictationPlayer.SpeakNext();
    }

    [RelayCommand]
    private void StartAuto()
    {
        _dictationPlayer.StartAuto(0);
    }

    [RelayCommand]
    private void PauseAuto()
    {
        _dictationPlayer.PauseAuto();
    }

    [RelayCommand]
    private void ResumeAuto()
    {
        _dictationPlayer.ResumeAuto();
    }

    [RelayCommand]
    private void Stop()
    {
        _dictationPlayer.Stop();
    }

    public void SpeakLine(int index)
    {
        _dictationPlayer.SpeakAt(index);
    }

    public void StartAutoFromLine(int index)
    {
        _dictationPlayer.StartAuto(index);
    }

    private async Task LoadVoiceOptionsAsync()
    {
        try
        {
            var factories = await _aggregator.GetAllFactoriesAsync(CancellationToken.None).ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                VoiceOptions.Clear();
                foreach (var factory in factories)
                {
                    VoiceOptions.Add(factory);
                }

                var targetVoiceId = _settingsStore.Value.Dictation.LastSelectedVoiceId;
                if (string.IsNullOrWhiteSpace(targetVoiceId))
                {
                    targetVoiceId = DefaultChineseVoiceId ?? DefaultEnglishVoiceId;
                }

                if (!string.IsNullOrWhiteSpace(targetVoiceId))
                {
                    SelectedVoiceFactory = VoiceOptions.FirstOrDefault(f => f.Info.Id == targetVoiceId || f.Info.DisplayName == targetVoiceId);
                }

                if (SelectedVoiceFactory is null && VoiceOptions.Count > 0)
                {
                    SelectedVoiceFactory = VoiceOptions[0];
                }
            });
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Failed to load voice factories: {ex}");
            await Dispatcher.UIThread.InvokeAsync(() => VoiceOptions.Clear());
        }
    }

    partial void OnSelectedVoiceFactoryChanged(IVoiceFactory? value)
    {
        if (value is not null)
        {
            _settingsStore.Update(s => s with { Dictation = s.Dictation with { LastSelectedVoiceId = value.Info.Id } });

            _currentVoice = value.Create();
            UpdateEffectiveVoice();
        }
    }

    private void UpdateEffectiveVoice()
    {
        IVoice effective = _currentVoice;
        if (!string.IsNullOrWhiteSpace(ImprovedResourcePath))
        {
            effective = new ImprovedVoice(effective, ImprovedResourcePath);
        }

        if (_dictationPlayer is DictationPlayer player)
        {
            player.Voice = effective;
        }
    }

    [RelayCommand]
    private void SwitchToChineseVoice()
    {
        var factory = VoiceOptions.FirstOrDefault(f => f.Info.Id == DefaultChineseVoiceId)
            ?? VoiceOptions.FirstOrDefault(f => f.Info.LocaleOrLanguage?.StartsWith("zh", StringComparison.OrdinalIgnoreCase) == true);
        if (factory is not null)
        {
            SelectedVoiceFactory = factory;
        }
        else
        {
            AlertRequested?.Invoke("未能找到中文引擎");
        }
    }

    [RelayCommand]
    private void SwitchToEnglishVoice()
    {
        var factory = VoiceOptions.FirstOrDefault(f => f.Info.Id == DefaultEnglishVoiceId)
            ?? VoiceOptions.FirstOrDefault(f => f.Info.LocaleOrLanguage?.StartsWith("en", StringComparison.OrdinalIgnoreCase) == true);
        if (factory is not null)
        {
            SelectedVoiceFactory = factory;
        }
        else
        {
            AlertRequested?.Invoke("未能找到英文引擎");
        }
    }

    public PreferenceSettings CreatePreferenceSnapshot() => _settingsStore.Value.Preference;

    public void ApplyPreferenceSettings(PreferenceSettings settings)
        => _settingsStore.Update(s => s with { Preference = settings });

    partial void OnWordListVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowOrHideWordListText));
    }

    private void ValidateAndApplyWaitingTime(string expression)
    {
        var result = WaitingTimeParser.Validate(expression);
        if (result.IsValid)
        {
            IntervalValidationHint = string.Empty;
            if (_dictationPlayer is DictationPlayer player)
            {
                player.WaitingTimeCalculator = result.Calculator;
            }
        }
        else
        {
            IntervalValidationHint = result.ErrorMessage;
        }
    }
}
