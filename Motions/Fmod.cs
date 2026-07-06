using System;
using System.IO;
using FMODUnity;

namespace Motions;

public static class FMODAudioUtil
{
    public static FMOD.Channel PlaySound(byte[] audioData)
        => PlaySound(audioData, 0f, 1f);

    public static FMOD.Channel PlaySound(byte[] audioData, float clipIn)
        => PlaySound(audioData, clipIn, 1f);

    /// <summary>
    /// Plays audio data, seeking to <paramref name="clipIn"/> seconds into the file
    /// (respecting the leading cut you set in Unity Timeline).
    /// </summary>
    public static FMOD.Channel PlaySound(byte[] audioData, float clipIn, float volume = 1f)
    {
        if (audioData == null || audioData.Length == 0)
            return default;

        string ext = DetectAudioExtension(audioData);
        string tempPath = Path.Combine(Path.GetTempPath(), $"motions_{Guid.NewGuid()}{ext}");

        File.WriteAllBytes(tempPath, audioData);

        var channel = PlaySoundInternal(tempPath, FMOD.MODE.DEFAULT | FMOD.MODE._2D, volume);

        // Seek to clipIn offset (leading-cut support)
        if (clipIn > 0f && channel.hasHandle())
        {
            uint posMs = (uint)(clipIn * 1000f);
            channel.setPosition(posMs, FMOD.TIMEUNIT.MS);
        }

        // Cleanup after the file is definitely no longer needed by FMOD
        System.Threading.Tasks.Task.Run(async () =>
        {
            await System.Threading.Tasks.Task.Delay(10000);
            try { File.Delete(tempPath); } catch { }
        });

        return channel;
    }

    private static string DetectAudioExtension(byte[] data)
    {
        if (data.Length < 4) return ".dat";

        // WAV = "RIFF"
        if (data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F')
            return ".wav";

        // OGG = "OggS"
        if (data[0] == 'O' && data[1] == 'g' && data[2] == 'g' && data[3] == 'S')
            return ".ogg";

        return ".dat";
    }

    private static FMOD.Channel PlaySoundInternal(string filePath, FMOD.MODE mode, float volume = 1f)
    {
        try
        {
            if (!RuntimeManager.IsInitialized) return default;
            var coreSystem = RuntimeManager.CoreSystem;
            if (!coreSystem.hasHandle()) return default;

            FMOD.Sound sound;
            // Use the 3-argument overload (string, MODE, out Sound) to bypass exinfo ABI issues
            var result = coreSystem.createSound(filePath, mode, out sound);

            if (result == FMOD.RESULT.OK)
            {
                coreSystem.getMasterChannelGroup(out var group);
                coreSystem.playSound(sound, group, false, out var channel);
                channel.setVolume(volume);
                // Note: We don't delete the temp file immediately as FMOD might need it for streaming
                return channel;
            }

            Logger.LogError($"createSound failed for {filePath}: {result}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Exception in PlaySoundInternal: {ex}");
        }

        return default;
    }

    public static byte[] GetWavBytes(float[] samples, int channels, int sampleRate)
    {
        using var stream = new System.IO.MemoryStream();
        using var writer = new System.IO.BinaryWriter(stream);

        int byteCount = samples.Length * 2; // 16-bit

        // RIFF header
        writer.Write("RIFF".ToCharArray());
        writer.Write(36 + byteCount);
        writer.Write("WAVE".ToCharArray());

        // fmt chunk
        writer.Write("fmt ".ToCharArray());
        writer.Write(16);
        writer.Write((short)1); // PCM
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * 2); // byte rate
        writer.Write((short)(channels * 2));     // block align
        writer.Write((short)16);                // bits per sample

        // data chunk
        writer.Write("data".ToCharArray());
        writer.Write(byteCount);

        for (int i = 0; i < samples.Length; i++)
        {
            short sample = (short)Math.Clamp(samples[i] * 32767f, short.MinValue, short.MaxValue);
            writer.Write(sample);
        }

        return stream.ToArray();
    }
}
