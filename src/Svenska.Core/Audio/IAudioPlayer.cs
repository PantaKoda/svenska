namespace Svenska.Core.Audio;

/// <summary>
/// Plays one short clip at a time. The implementation is platform-specific and lives
/// outside Svenska.Core.
/// </summary>
public interface IAudioPlayer : IDisposable
{
    /// <summary>Starts playing the file and returns at once. Replaces a clip that is still playing.</summary>
    void Play(string path);

    void Stop();

    bool IsPlaying { get; }
}
