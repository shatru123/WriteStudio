using System.Text;

namespace WriteStudio.Audio;

/// <summary>
/// A resilient 16-bit PCM WAV (RIFF) writer that updates header chunk sizes progressively.
/// </summary>
public class WavFileWriter : IDisposable, IAsyncDisposable
{
    private readonly FileStream _fileStream;
    private readonly BinaryWriter _writer;
    private readonly int _sampleRate;
    private readonly short _channels;
    private readonly short _bitsPerSample;
    private uint _dataBytesWritten = 0;
    private bool _isDisposed = false;
    private readonly object _lock = new();

    public string FilePath { get; }
    public int SampleRate => _sampleRate;
    public short Channels => _channels;
    public uint DataBytesWritten => _dataBytesWritten;
    public TimeSpan RecordedDuration => TimeSpan.FromSeconds((double)_dataBytesWritten / (_sampleRate * _channels * (_bitsPerSample / 8)));

    public WavFileWriter(string filePath, int sampleRate = 48000, short channels = 2, short bitsPerSample = 16)
    {
        FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        _sampleRate = sampleRate;
        _channels = channels;
        _bitsPerSample = bitsPerSample;

        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _fileStream = new FileStream(filePath, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
        _writer = new BinaryWriter(_fileStream, Encoding.UTF8);

        WriteHeaderPlaceholder();
    }

    private void WriteHeaderPlaceholder()
    {
        lock (_lock)
        {
            _writer.Seek(0, SeekOrigin.Begin);
            
            // RIFF chunk descriptor
            _writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            _writer.Write((uint)0); // Placeholder for RIFF chunk size
            _writer.Write(Encoding.ASCII.GetBytes("WAVE"));

            // "fmt " sub-chunk
            _writer.Write(Encoding.ASCII.GetBytes("fmt "));
            _writer.Write((uint)16); // SubChunk1Size for PCM = 16
            _writer.Write((short)1);  // AudioFormat 1 = PCM
            _writer.Write(_channels);
            _writer.Write(_sampleRate);
            
            int byteRate = _sampleRate * _channels * (_bitsPerSample / 8);
            _writer.Write(byteRate);
            
            short blockAlign = (short)(_channels * (_bitsPerSample / 8));
            _writer.Write(blockAlign);
            _writer.Write(_bitsPerSample);

            // "data" sub-chunk
            _writer.Write(Encoding.ASCII.GetBytes("data"));
            _writer.Write((uint)0); // Placeholder for SubChunk2Size
        }
    }

    public void WriteSampleData(byte[] buffer, int offset, int count)
    {
        if (_isDisposed || count <= 0) return;

        lock (_lock)
        {
            _writer.Write(buffer, offset, count);
            _dataBytesWritten += (uint)count;
        }
    }

    public void FlushAndFinalizeHeader()
    {
        lock (_lock)
        {
            if (_isDisposed) return;

            long currentPos = _fileStream.Position;

            // Update RIFF total size: 36 + SubChunk2Size
            _writer.Seek(4, SeekOrigin.Begin);
            _writer.Write((uint)(36 + _dataBytesWritten));

            // Update data chunk size
            _writer.Seek(40, SeekOrigin.Begin);
            _writer.Write(_dataBytesWritten);

            _writer.Seek((int)currentPos, SeekOrigin.Begin);
            _writer.Flush();
            _fileStream.Flush();
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        lock (_lock)
        {
            if (_isDisposed) return;
            FlushAndFinalizeHeader();
            _writer.Dispose();
            _fileStream.Dispose();
            _isDisposed = true;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        FlushAndFinalizeHeader();
        await _fileStream.FlushAsync();
        Dispose();
    }
}
