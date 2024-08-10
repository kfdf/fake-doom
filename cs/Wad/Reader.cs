using System.IO;

class WadReader {
  enum LumpType { None, Patch, Sprite, Flat }
  FileStream file;
  DirectoryEntry[] directory;
  Dictionary<(ShortString, LumpType), int> directoryLookup = [];
  Dictionary<ShortString, TextureDef> textureDefLookup = [];
  Dictionary<ShortString, Picture> patchCache = new() { [default] = Picture.Empty };
  WadReader fallback;
  public WadReader(FileStream file, WadReader fallback = null) {
    this.fallback = fallback;
    this.file = file; {
      Span<byte> bytes = stackalloc byte[4];
      file.Seek(4, SeekOrigin.Begin);
      file.ReadExactly(bytes);
      var lumpCount = BitConverter.ToInt32(bytes);
      file.ReadExactly(bytes);
      var directoryOffset = BitConverter.ToInt32(bytes);
      using var dirLump = new Lump(file, directoryOffset, lumpCount * DirectoryEntry.Size);
      directory = new DirectoryEntry[lumpCount];
      var markers = new Dictionary<ShortString, (ShortString, ShortString, LumpType)> {
        ["S_START"] =  ("S_END",  default, LumpType.Sprite),
        ["SS_START"] = ("SS_END", "S_END", LumpType.Sprite),
        ["P_START"] =  ("P_END",  default, LumpType.Patch),
        ["PP_START"] = ("PP_END", "P_END", LumpType.Patch),
        ["F_START"] =  ("F_END",  default, LumpType.Flat),
        ["FF_START"] = ("FF_END", "F_END", LumpType.Flat),
      };
      var type = LumpType.None;
      ShortString endMarker = default, endMarker2 = default;
      for (int i = 0; i < directory.Length; i++) {
        var entry = directory[i] = new DirectoryEntry(dirLump);
        if (type == LumpType.None) {
          if (markers.TryGetValue(entry.name, out var value)) {
            (endMarker, endMarker2, type) = value;
          }
        } else if (entry.name == endMarker || entry.name == endMarker2) {
          type = LumpType.None;
        }
        directoryLookup[(entry.name, type)] = i;
      }
    } 
    (ShortString, LumpType)[] lumpNames = [
      ("TEXTURE1", LumpType.None), ("TEXTURE2", LumpType.None)
    ];
    if (lumpNames.Any(directoryLookup.ContainsKey)) {
      ShortString[] pnames; {
        var entry = directory[directoryLookup[("PNAMES", LumpType.None)]];
        using var lump = new Lump(file, entry);
        pnames = new ShortString[lump.ReadInt()];
        for (int i = 0; i < pnames.Length; i++) {
          pnames[i] = lump.ReadString().ToUppercase();
        } 
      } { 
        foreach (var lumpName in lumpNames) {
          if (!directoryLookup.TryGetValue(lumpName, out var entryIdx)) continue;
          using var lump = new Lump(file, directory[entryIdx]);
          int textureCount = lump.ReadInt();
          var offsets = new int[textureCount];
          for (int i = 0; i < textureCount; i++) {
            offsets[i] = lump.ReadInt();
          }
          Memory<PatchDef> freeSpace = new PatchDef[100];
          foreach (int offset in offsets) {
            lump.offset = offset;
            var name = lump.ReadString().ToUppercase();
            lump.offset += 4;
            short width = lump.ReadShort();
            short height = lump.ReadShort();
            lump.offset += 4;
            int patchCount = lump.ReadShort();
            if (patchCount > freeSpace.Length) {
              freeSpace = new PatchDef[Math.Max(patchCount, 100)];
            }
            var patches = freeSpace[..patchCount];
            freeSpace = freeSpace[patchCount..];
            textureDefLookup[name] = new TextureDef(width, height, patches);
            var span = patches.Span;
            for (int i = 0; i < patchCount; i++) {
              short xOffset = lump.ReadShort();
              short yOffset = lump.ReadShort();
              var patchName = pnames[lump.ReadShort()];
              lump.offset += 4;
              span[i] = new PatchDef(patchName, xOffset, yOffset);
            }
          }
        }
      }
    }
  }
  public (Vertex[], Subsector[], Segment[], Node[]) ReadExtended(ShortString mapName) {
    var (file, entry) = GetMapEntry<Node>(mapName, Node.Info.lumpName);
    using var lump = new Lump(file, entry);
    if (!"XNOD"u8.SequenceEqual(lump.ReadBytes(4))) {
      throw new Exception("Expected XNOD");
    }
    lump.offset += 4;
    return (read<Vertex>(lump), read<Subsector>(lump), read<Segment>(lump), read<Node>(lump));

    T[] read<T>(Lump lump) where T: IMapComponentExt<T> {
      var ret = new T[lump.ReadInt()];
      for (int i = 0; i < ret.Length; i++) {
        ret[i].InitExtended(lump);
      }
      return ret;
    }
  }
  public T[] ReadMap<T>(ShortString mapName) where T: IMapComponent<T> {
    var (file, entry) = GetMapEntry<T>(mapName, T.Info.lumpName);
    var ret = new T[entry.length / T.Info.recordSize];
    using var lump = new Lump(file, entry);
    for (int i = 0; i < ret.Length; i++) ret[i].Init(lump);
    return ret;
  }  
  public uint[] ReadPalette(int index) {
    var (file, entry) = GetEntry("PLAYPAL");
    var offset = entry.offset + index * 256 * 3;
    using var lump = new Lump(file, offset, 256 * 3);
    var ret = new uint[256];
    for (int i = 0; i < ret.Length; i++) {
      uint red = lump.ReadByte();
      uint green = lump.ReadByte();
      uint blue = lump.ReadByte();
      ret[i] = 0xff000000 | blue << 16 | green << 8 | red;
    }
    return ret;
  }
  public byte[][] ReadColormaps() {
    var (file, entry) = GetEntry("COLORMAP");
    using var lump = new Lump(file, entry);
    var ret = new byte[lump.Length / 256][];
    for (int i = 0; i < ret.Length; i++) {
      ret[i] = lump.ReadBytes(256);
    }
    return ret;
  }
  Picture ReadPatch(ShortString name) {
    if (patchCache.TryGetValue(name, out var ret)) return ret;
    var (file, entry) = GetEntry(name, LumpType.Patch);
    using var lump = new Lump(file, entry);
    return patchCache[name] = new Picture(lump);
  }
  public Picture ReadFloater(ShortString name) {
    var textureDef = GetTextureDefinition(name);
    var patchName = textureDef.patches.Span[0].name;
    return ReadPatch(patchName);
  }
  public Picture ReadSprite(ShortString name) {
    var (file, entry) = GetEntry(name, LumpType.Sprite);
    using var lump = new Lump(file, entry);
    return new Picture(lump);
  }
  public Texture ReadTexture(ShortString name) {
    var texdef = GetTextureDefinition(name);
    var data = new byte[texdef.width * texdef.height + 128];
    foreach (var pdef in texdef.patches.Span) {
      var patch = ReadPatch(pdef.name);
      int colFrom = Math.Max(0, +pdef.xOffset);
      int colUpto = Math.Min(texdef.width, pdef.xOffset + patch.width);
      for (int col = colFrom; col < colUpto; col++) {
        int destRowIdx = col * texdef.height;
        int patchCol = col - pdef.xOffset;
        foreach (var post in patch.EnumeratePosts(patchCol)) {
          var pixels = post.pixels.Span;
          int rowFrom = Math.Max(0, pdef.yOffset + post.from);
          int rowUpto = Math.Min(texdef.height, pdef.yOffset + post.upto);
          for (int row = rowFrom, i = 1; row < rowUpto; row++, i++) {
            data[destRowIdx + row] = pixels[i];
          }
        }
      }
    }
    return new Texture(texdef.width, texdef.height, data);
  }
  public byte[] ReadFlat(ShortString name) {
    var (file, entry) = GetEntry(name, LumpType.Flat);
    using var lump = new Lump(file, entry);
    return lump.ReadBytes(lump.Length);
  }
  public bool IsValidMap(ShortString name) {
    return directoryLookup.ContainsKey((name, LumpType.None)) ||
      fallback != null && fallback.IsValidMap(name);
  }
  (FileStream, DirectoryEntry) GetEntry(ShortString name, LumpType type = LumpType.None) {
    if (directoryLookup.TryGetValue((name, type), out var entryIdx) || 
      type == LumpType.Patch && (
        directoryLookup.TryGetValue((name, LumpType.None), out entryIdx) ||
        directoryLookup.TryGetValue((name, LumpType.Sprite), out entryIdx) 
      ) ||
      type == LumpType.Flat && (
        directoryLookup.TryGetValue((name, LumpType.None), out entryIdx)
      )
    ) {
      return (file, directory[entryIdx]);
    } else {
      return fallback.GetEntry(name, type);
    }
  }
  (FileStream, DirectoryEntry) GetMapEntry<T>(
    ShortString mapName, ShortString lumpName
  ) where T: IMapComponent<T> {
    if (directoryLookup.TryGetValue((mapName, LumpType.None), out var entryIndex)) {
      while (directory[++entryIndex].name != lumpName);
      return (file, directory[entryIndex]);
    } else {
      return fallback.GetMapEntry<T>(mapName, lumpName);
    }
  }
  TextureDef GetTextureDefinition(ShortString texName) {
    if (textureDefLookup.TryGetValue(texName, out var textureDef)) {
      return textureDef;
    } else if (fallback != null) {
      return fallback.GetTextureDefinition(texName);
    } else {
      Console.WriteLine("Texture not found: " + texName);
      return new TextureDef(128, 128, new PatchDef[1]);
    }
  }
}
