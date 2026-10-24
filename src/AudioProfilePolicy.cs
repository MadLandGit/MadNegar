namespace MadNegar;

internal readonly record struct AudioProfilePreset(
    string Id, int MicrophoneBoostDb, bool AutoLevelQuietSpeech, bool ReduceLowFrequencyRumble);

/// <summary>
/// Small, transparent microphone presets. These are conservative starting points,
/// not noise cancellation; Custom preserves the user's current controls.
/// </summary>
internal static class AudioProfilePolicy
{
    public const string Custom = "custom";
    public const string QuietRoom = "quiet-room";
    public const string NoisyRoom = "noisy-room";

    public static string Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        QuietRoom => QuietRoom,
        NoisyRoom => NoisyRoom,
        _ => Custom
    };

    public static bool TryGetPreset(string? profile, out AudioProfilePreset preset)
    {
        switch (Normalize(profile))
        {
            case QuietRoom:
                preset = new AudioProfilePreset(QuietRoom, 6, true, false);
                return true;
            case NoisyRoom:
                // Do not boost background noise. Auto-level is still clamped by
                // the audio processor; the high-pass only removes low-frequency rumble.
                preset = new AudioProfilePreset(NoisyRoom, 0, true, true);
                return true;
            default:
                preset = default;
                return false;
        }
    }


}
