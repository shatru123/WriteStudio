using WriteStudio.Core.Models;

namespace WriteStudio.Audio;

public static class AudioLevelCalculator
{
    /// <summary>
    /// Computes Peak, RMS, and Decibels from a 16-bit PCM byte array.
    /// </summary>
    public static AudioLevelEventArgs ComputeLevels(byte[] buffer, int offset, int count)
    {
        if (buffer == null || count < 2)
        {
            return new AudioLevelEventArgs(0.0f, 0.0f);
        }

        int sampleCount = count / 2;
        float maxSample = 0.0f;
        double sumSquares = 0.0;

        for (int i = 0; i < sampleCount; i++)
        {
            int index = offset + (i * 2);
            short sample = (short)(buffer[index] | (buffer[index + 1] << 8));
            float normalized = Math.Abs(sample / 32768.0f);

            if (normalized > maxSample)
            {
                maxSample = normalized;
            }

            sumSquares += normalized * normalized;
        }

        float rms = (float)Math.Sqrt(sumSquares / sampleCount);
        return new AudioLevelEventArgs(maxSample, rms);
    }
}
