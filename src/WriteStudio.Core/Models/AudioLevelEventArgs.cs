namespace WriteStudio.Core.Models;

/// <summary>
/// Real-time audio input level metrics for VU meters.
/// </summary>
public class AudioLevelEventArgs : EventArgs
{
    /// <summary>
    /// Peak amplitude normalized between 0.0f (silence) and 1.0f (clipping).
    /// </summary>
    public float Peak { get; }

    /// <summary>
    /// Root Mean Square amplitude normalized between 0.0f and 1.0f.
    /// </summary>
    public float Rms { get; }

    /// <summary>
    /// Decibel level, typically -60 dB to 0 dB.
    /// </summary>
    public float Decibels { get; }

    public AudioLevelEventArgs(float peak, float rms)
    {
        Peak = Math.Clamp(peak, 0.0f, 1.0f);
        Rms = Math.Clamp(rms, 0.0f, 1.0f);
        Decibels = rms > 0.00001f ? (float)(20.0 * Math.Log10(rms)) : -60.0f;
    }
}
