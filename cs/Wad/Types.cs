using System.Buffers;
using System.IO;

readonly record struct ShortString(ulong value) {
  public readonly ShortString ToUppercase() {
    ulong value = 0;
    for (int i = 0; i < 64; i += 8) {
      value |= (ulong)char.ToUpperInvariant((char)(byte)(this.value >> i)) << i;
    }
    var ret = new ShortString(value);
    return ret;
  }
  public static implicit operator ShortString(string str) {
    ulong value = 0;
    int offset = 0;
    foreach (char ch in str) {
      value |= (ulong)(byte)ch << offset;
      offset += 8;
    }
    return new ShortString(value);
  }
  public override readonly string ToString() {
    Span<char> chars = stackalloc char[8];
    ulong value = this.value;
    int idx = 0;
    while (idx < chars.Length && value != 0) {
      chars[idx++] = (char)(byte)value;
      value >>= 8;
    }
    return new String(chars[..idx]);
  }  
}

readonly record struct DirectoryEntry {
  public const int Size = 16;
  public readonly int offset, length;
  public readonly ShortString name;
  public DirectoryEntry(Lump lump) {
    offset = lump.ReadInt();
    length = lump.ReadInt();
    name = lump.ReadString().ToUppercase();
  }
}

class Lump : IDisposable {
  byte[] data;
  public int offset;
  readonly int length;
  public int Length => length - offset;
  public Lump(FileStream file, DirectoryEntry entry) :
    this(file, entry.offset, entry.length) { }
  public Lump(FileStream file, int offset, int length) {
    file.Seek(offset, SeekOrigin.Begin);
    data = ArrayPool<byte>.Shared.Rent(length);
    file.ReadExactly(data.AsSpan(..length));
    this.offset = 0;
    this.length = length;
  }
  public int ReadInt() {
    var ret = BitConverter.ToInt32(data, offset);
    offset += 4;
    return ret;
  }
  public short ReadShort() {
    var ret = BitConverter.ToInt16(data, offset);
    offset += 2;
    return ret;
  }  
  public int ReadReference() {
    var ret = BitConverter.ToUInt16(data, offset);
    offset += 2;
    return ret == 0xffff ? -1 : ret;
  }
  public byte ReadByte() {
    return data[offset++];
  }  
  public ShortString ReadString() {
    var str = BitConverter.ToUInt64(data, offset);
    offset += 8;
    for (int i = 0; i < 64; i += 8) {
      if ((str >>> i & 0xff) != 0) continue;
      str &= (1UL << i) - 1;
      break;
    }
    return new ShortString(str);
  }
  public byte[] ReadBytes(int count) {
    return data[offset..(offset += count)];
  }
  public void Dispose() {
    if (data != null) {
      ArrayPool<byte>.Shared.Return(data);
    }
  }
}

record struct TextureDef(short width, short height, Memory<PatchDef> patches);
record struct PatchDef(ShortString name, short xOffset, short yOffset);
record Texture(short width, short height, byte[] data);

class Picture {
  public short width, height, leftOffset, topOffset;
  int[] columnOffsets;
  byte[] data;
  public static Picture Empty = new Picture {
    width = 128,
    height = 128,
    columnOffsets = new int[128],
    data = [0xff],
  };
  Picture() {}
  public Picture(Lump lump) {
    int lumpSize = lump.Length;
    width = lump.ReadShort();
    height = lump.ReadShort();
    leftOffset = lump.ReadShort();
    topOffset = lump.ReadShort();
    columnOffsets = new int[width];
    for (int i = 0; i < columnOffsets.Length; i++) {
      columnOffsets[i] = lump.ReadInt();
    }
    int headerSize = lumpSize - lump.Length;
    for (int i = 0; i < columnOffsets.Length; i++) {
      columnOffsets[i] -= headerSize;
    }
    data = lump.ReadBytes(lump.Length); 
  }
  public ColumnEnumerator EnumeratePosts(int column) {
    return new ColumnEnumerator(columnOffsets[column], data);
  } 
  public readonly record struct Post(byte from, byte upto, Memory<byte> pixels);
  public record struct ColumnEnumerator(int offset, byte[] data) {
    public ColumnEnumerator GetEnumerator() => this;
    public Post Current { get; private set; }
    public bool MoveNext() {
      byte from = data[offset];
      if (from == 0xff) return false;
      byte size = data[offset + 1];
      byte upto = (byte)(from + size);
      var pixels = data.AsMemory(offset + 2, size + 2);
      offset += size + 4;
      Current = new Post(from, upto, pixels);
      return true;
    }
  }
}
