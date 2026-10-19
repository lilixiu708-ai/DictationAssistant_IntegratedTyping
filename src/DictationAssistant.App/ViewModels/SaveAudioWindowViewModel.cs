using CommunityToolkit.Mvvm.ComponentModel;
using DictationAssistant.App.Abstractions;
using DictationAssistant.App.Lyric;
using DictationAssistant.App.Audio;
using DictationAssistant.App.Audio.Encoder;
using DictationAssistant.App.Services;

namespace DictationAssistant.App.ViewModels;

public partial class SaveAudioWindowViewModel : ObservableObject
{
    private IDictationPlayer? _dictationPlayer;

    public event Action<string>? AlertRequested;

    public IReadOnlyList<string> ChannelOptions { get; } = ["Mono", "Stereo"];

    public IReadOnlyList<string> SampleFormatOptions { get; } = ["Unsigned 8bit", "Signed 16bit"];

    public IReadOnlyList<string> FrequencyOptions { get; } =
    [
        "6000",
        "7333",
        "8000",
        "11025",
        "16000",
        "22050",
        "24000",
        "32000",
        "44100",
        "48000"
    ];

    public IReadOnlyList<string> LyricModeOptions { get; } = ["Dismiss", "Lrc File"];

    public IReadOnlyList<IAudioEncoderFactory> EncoderOptions { get; }

    [ObservableProperty]
    private IAudioEncoderFactory _selectedEncoder;

    [ObservableProperty]
    private string _channel = "Stereo";

    [ObservableProperty]
    private string _sampleFormat = "Signed 16bit";

    [ObservableProperty]
    private string _frequency = "44100";

    [ObservableProperty]
    private string _lyricMode = "Lrc File";

    [ObservableProperty]
    private string _targetPath = "";

    [ObservableProperty]
    private bool _isExporting;

    [ObservableProperty]
    private double _exportProgress;

    private CancellationTokenSource? _exportCts;

    public SaveAudioWindowViewModel()
    {
        var encoders = AudioEncoderProviders.GetAvailable();

        EncoderOptions = encoders;
        _selectedEncoder = encoders[0];
    }

    public void SetDictationPlayer(IDictationPlayer player)
    {
        _dictationPlayer = player;
    }

    public async Task<bool> ExportAsync()
    {
        if (_dictationPlayer is null)
        {
            AlertRequested?.Invoke("错误：未初始化播放器");
            return false;
        }

        if (string.IsNullOrWhiteSpace(TargetPath))
        {
            AlertRequested?.Invoke("请指定输出路径");
            return false;
        }

        // Snapshot all parameters from UI thread before going async
        var words = _dictationPlayer.WordListSource.GetWords();
        var voice = _dictationPlayer.Voice;
        var waitingTimeCalculator = _dictationPlayer.WaitingTimeCalculator;
        var timesPerWord = _dictationPlayer.TimesPerWord;
        var rate = _dictationPlayer.Rate;
        var sampleRate = int.TryParse(Frequency, out var sr) ? sr : 44100;
        var channels = Channel switch
        {
            "Mono" => 1,
            "Stereo" => 2,
            _ => throw new InvalidOperationException("Invalid channel option")
        };
        var sampleFormat = SampleFormat switch
        {
            "Unsigned 8bit" => PcmSampleFormat.U8,
            "Signed 16bit" => PcmSampleFormat.S16LE,
            _ => throw new InvalidOperationException("Invalid sample format option")
        };
        var targetFormat = new PcmFormatInfo(sampleRate, channels, sampleFormat);
        var selectedEncoder = SelectedEncoder;
        var targetPath = TargetPath;
        var lyricMode = LyricMode;

        IsExporting = true;
        ExportProgress = 0;
        _exportCts = new CancellationTokenSource();
        var ct = _exportCts.Token;

        var progress = new Progress<double>(p => ExportProgress = p);

        try
        {
            await Task.Run(async () =>
            {
                ILyricWriter? lyricWriter = null;
                if (lyricMode == "Lrc File")
                {
                    lyricWriter = new LyricWriter();
                }

                var encoder = selectedEncoder.CreateEncoder(targetFormat, targetPath);
                try
                {
                    using var pcmWriter = new PcmWriter(encoder.RawAudio, leaveOpen: true);

                    var exporter = new AudioExporter(voice, words, waitingTimeCalculator);
                    await exporter.ExportAsync(
                        pcmWriter, lyricWriter,
                        timesPerWord, rate,
                        progress, ct).ConfigureAwait(false);
                }
                finally
                {
                    await encoder.FinalizeAsync().ConfigureAwait(false);
                }

                if (lyricWriter is not null)
                {
                    var lrcPath = Path.ChangeExtension(targetPath, "lrc");
                    using var lyricFileStream = File.Open(lrcPath, FileMode.Create);
                    lyricWriter.SaveTo(lyricFileStream);
                }
            }, ct).ConfigureAwait(false);

            IsExporting = false;
            AlertRequested?.Invoke("导出完成");
            return true;
        }
        catch (OperationCanceledException)
        {
            IsExporting = false;
            AlertRequested?.Invoke("导出已取消");
            return false;
        }
        catch (Exception ex)
        {
            IsExporting = false;
            AlertRequested?.Invoke($"导出失败: {ex.Message}");
            return false;
        }
        finally
        {
            _exportCts?.Dispose();
            _exportCts = null;
        }
    }

    public void CancelExport()
    {
        _exportCts?.Cancel();
    }

    partial void OnSelectedEncoderChanged(IAudioEncoderFactory value)
    {
        if (!string.IsNullOrWhiteSpace(TargetPath))
        {
            TargetPath = Path.ChangeExtension(TargetPath, value.Info.Extension) ?? TargetPath;
        }
    }
}
