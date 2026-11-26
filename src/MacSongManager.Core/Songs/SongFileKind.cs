namespace MacSongManager.Core.Songs;

/// <summary>
/// How a file name relates to Rocksmith 2014 on Mac.
/// Mac packages end in _m.psarc. A disabled package keeps the same stem and
/// inserts .disabled before the extension. Packages ending in _p.psarc are
/// the PC layout and the Mac game will not load them.
/// </summary>
public enum SongFileKind
{
    Unmanaged = 0,
    MacEnabled,
    MacDisabled,
    PcFormat
}
