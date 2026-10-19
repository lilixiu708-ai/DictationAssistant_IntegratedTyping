using System.Diagnostics;

namespace DictationAssistant.App.Audio.Encoder;

public class ExternalAudioEncoder : IAudioEncoder
{
    private readonly Process _process;
    public PcmAudio RawAudio { get; }

    public ExternalAudioEncoder(PcmFormatInfo pcmFormatInfo, string path, string encoderFileName, string encoderArguments)
    {
        _process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = encoderFileName,
                Arguments = encoderArguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true
            }
        };

        _process.Start();
        WaveEncoder.WriteWaveHeader(_process.StandardInput.BaseStream, pcmFormatInfo, 0);
        RawAudio = new PcmAudio()
        {
            Data = _process.StandardInput.BaseStream,
            Format = pcmFormatInfo
        };
    }

    public async Task FinalizeAsync()
    {
        await _process.StandardInput.DisposeAsync();
        await _process.WaitForExitAsync();
        _process.Dispose();
    }
}
