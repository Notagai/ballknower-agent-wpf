using System.Media;

namespace Ballknower.Voice;

public static class SpeechEffects
{
    public static void PlayListening() => SystemSounds.Beep.Play();
    public static void PlaySuccess() => SystemSounds.Asterisk.Play();
    public static void PlayError() => SystemSounds.Exclamation.Play();
}
