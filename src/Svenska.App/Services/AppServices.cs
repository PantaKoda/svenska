using Svenska.Core.Answers;
using Svenska.Core.Audio;
using Svenska.Core.Content;
using Svenska.Core.Progress;
using Svenska.Core.Saldo;

namespace Svenska.App.Services;

/// <summary>What the screens share: the loaded course, the word database and the audio player.</summary>
public sealed class AppServices : IDisposable
{
    private SaldoLexicon? _saldo;
    private IAudioPlayer? _audio;
    private bool _audioFailed;
    private readonly ProgressStore _progressStore;

    /// <summary>The learner's progress and review queue.</summary>
    public ProgressTracker Progress { get; }

    /// <summary>
    /// progress.db in the user's app-data folder (%APPDATA%\Svenska, ~/.config/Svenska).
    /// SVENSKA_PROGRESS_DB overrides the path, for trying things out without touching real progress.
    /// </summary>
    public static string ProgressPath =>
        Environment.GetEnvironmentVariable("SVENSKA_PROGRESS_DB") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Svenska", "progress.db");

    public string? ContentDir { get; }
    public Course Course { get; } = new();
    public int ContentErrors { get; }

    public AppServices()
    {
        ContentDir = ContentLoader.FindContentDir(AppContext.BaseDirectory);
        if (ContentDir != null)
        {
            var (course, issues) = ContentLoader.Load(ContentDir);
            Course = course;
            ContentErrors = issues.Count(i => i.Severity == Severity.Error);
        }
        _progressStore = new ProgressStore(ProgressPath);
        Progress = new ProgressTracker(Course, _progressStore, new LeitnerScheduler(), () => DateTime.UtcNow);
    }

    /// <summary>Null when data/saldo.db has not been built; answers are then checked without it.</summary>
    public ISaldoLexicon? Saldo
    {
        get
        {
            if (_saldo == null && ContentDir != null)
            {
                var path = Path.Combine(ContentDir, "..", "data", "saldo.db");
                if (File.Exists(path))
                    _saldo = new SaldoLexicon(path);
            }
            return _saldo;
        }
    }

    public AnswerChecker CreateChecker() => new(Saldo);

    private int _playRequest;

    /// <summary>Plays the audio of a Swedish line. False if there is no file or no sound device.</summary>
    public bool Play(string text, bool slow = false)
    {
        _playRequest++; // ends a sequence that is still playing
        return PlayFile(text, slow);
    }

    /// <summary>Plays several lines one after the other (what is heard before a dialogue reply).</summary>
    public async void PlayAll(IReadOnlyList<string> texts)
    {
        var request = ++_playRequest;
        foreach (var text in texts)
        {
            if (request != _playRequest || !PlayFile(text, slow: false))
                continue;
            while (request == _playRequest && _audio is { IsPlaying: true })
                await Task.Delay(60);
            await Task.Delay(250);
        }
    }

    public void StopAudio()
    {
        _playRequest++;
        _audio?.Stop();
    }

    private bool PlayFile(string text, bool slow)
    {
        if (ContentDir == null || _audioFailed)
            return false;
        var path = Path.Combine(ContentDir, "audio", AudioFiles.FileName(text, slow));
        if (!File.Exists(path) && slow)
            path = Path.Combine(ContentDir, "audio", AudioFiles.FileName(text, slow: false));
        if (!File.Exists(path))
            return false;
        try
        {
            _audio ??= new OpenAlAudioPlayer();
            _audio.Play(path);
            return true;
        }
        catch (Exception)
        {
            _audioFailed = true; // no sound device: the exercises still work, silently
            return false;
        }
    }

    /// <summary>Closes the files the app holds open, so that an update can replace them.</summary>
    public void ReleaseFiles()
    {
        _saldo?.Dispose();
        _saldo = null;
    }

    public void Dispose()
    {
        ReleaseFiles();
        _progressStore.Dispose();
        _audio?.Dispose();
    }
}
