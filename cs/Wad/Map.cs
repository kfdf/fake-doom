interface IMapComponent {
  static abstract (string lumpName, int recordSize) Info { get; }
  void Init(Lump lump);
}
interface IMapComponentExt {
  void InitExtended(Lump lump);
}
record struct Linedef : IMapComponent {
  public static (string, int) Info => ("LINEDEFS", 14);
  public int startVertexIdx, endVertexIdx, frontSidedefIdx, backSidedefIdx;
  public short flags, lineType, sectorTag;
  public bool upperUnpegged, lowerUnpegged;
  public void Init(Lump lump) {
    startVertexIdx = lump.ReadReference();
    endVertexIdx = lump.ReadReference();
    flags = lump.ReadShort();
    lineType = lump.ReadShort();
    sectorTag = lump.ReadShort();
    frontSidedefIdx = lump.ReadReference();
    backSidedefIdx = lump.ReadReference();
    upperUnpegged = (flags & 0b01000) != 0;
    lowerUnpegged = (flags & 0b10000) != 0;
  }
}
record struct Vertex : IMapComponent, IMapComponentExt {
  public static (string, int) Info => ("VERTEXES", 4);
  public double x, y;
  public void Init(Lump lump) {
    x = lump.ReadShort();
    y = lump.ReadShort();
  }
  public void InitExtended(Lump lump) {
    x = lump.ReadInt() / 65536.0;
    y = lump.ReadInt() / 65536.0;
  }
}
record struct Node : IMapComponent, IMapComponentExt {
  public static (string lumpName, int recordSize) Info => ("NODES", 28);
  public record struct ChildBox {
    public double top, bottom, left, right;
    public void Init(Lump lump) {
      top = lump.ReadShort();
      bottom = lump.ReadShort();
      left = lump.ReadShort();
      right = lump.ReadShort();
    }
  }
  public record struct ChildRef {
    public int childIdx;
    public bool isNode;
    public void Init(Lump lump) {
      childIdx = lump.ReadShort();
      isNode = childIdx >= 0;
      if (!isNode) childIdx &= 0x7fff;
    }
    public void InitExtended(Lump lump) {
      childIdx = lump.ReadInt();
      isNode = childIdx >= 0;
      if (!isNode) childIdx &= 0xfffffff;
    }
  }
  public double x, y, dx, dy; 
  public ChildBox leftBox, rightBox;
  public ChildRef leftRef, rightRef;
  void InitMain(Lump lump) {
    x = lump.ReadShort();
    y = lump.ReadShort();
    dx = lump.ReadShort();
    dy = lump.ReadShort();
    double len = Math.Sqrt(dx * dx + dy * dy);
    dx /= len;
    dy /= len;
    firstThingIdx = -1;
  }
  public void Init(Lump lump) {
    InitMain(lump);
    rightBox.Init(lump);
    leftBox.Init(lump);
    rightRef.Init(lump);
    leftRef.Init(lump);
  }
  public void InitExtended(Lump lump) {
    InitMain(lump);
    rightBox.Init(lump);
    leftBox.Init(lump);
    rightRef.InitExtended(lump);
    leftRef.InitExtended(lump);
  }
  public int firstThingIdx;
}
record struct Subsector: IMapComponent, IMapComponentExt {
  public static (string, int) Info => ("SSECTORS", 4);
  public int firstSegIdx, segmentCount;
  public void Init(Lump lump) {
    segmentCount = lump.ReadReference();
    firstSegIdx = lump.ReadReference();
    firstThingIdx = -1;
  }
  public void InitExtended(Lump lump) {
    segmentCount = lump.ReadInt();
    firstThingIdx = -1;
  }

  public int firstThingIdx;
}
record struct Segment: IMapComponent, IMapComponentExt {
  public static (string, int) Info => ("SEGS", 12);
  public int startVertexIdx, endVertexIdx, linedefIdx;
  public double offset;
  public bool isBackside;
  public void Init(Lump lump) {
    startVertexIdx = lump.ReadReference();
    endVertexIdx = lump.ReadReference();
    lump.offset += 2;
    linedefIdx = lump.ReadReference();
    isBackside = lump.ReadShort() != 0;
    offset = lump.ReadShort();
  }
  public void InitExtended(Lump lump) {
    startVertexIdx = lump.ReadInt();
    endVertexIdx = lump.ReadInt();
    linedefIdx = lump.ReadReference();
    isBackside = lump.ReadByte() != 0;
  }  
  public double frameCount;
  public double dx, dy, invLength;
  public int frontSidedefIdx, backSidedefIdx, frontSectorIdx, backSectorIdx;
}
record struct Thing: IMapComponent {
  public static (string, int) Info => ("THINGS", 10);
  public short angle, type, flags;
  public double x, y;
  public void Init(Lump lump) {
    x = lump.ReadShort();
    y = lump.ReadShort();
    angle = lump.ReadShort();
    type = lump.ReadShort();
    flags = lump.ReadShort();
  }
  public int sectorIdx, nextThingIdx;
  public (
    bool mirror, bool transparent, bool hanging, double dist,
    ShortString spriteName, double canvasLeft, double canvasRight, double scale
  ) cached;
  public long cacheFrameCount;
}
record struct Sector: IMapComponent {
  public static (string, int) Info => ("SECTORS", 26);
  public double floorHeight, ceilingHeight;
  public ShortString floorTexture, ceilingTexture;
  public short lightLevel, colormapIndex, type, tag;
  public void Init(Lump lump) {
    floorHeight = lump.ReadShort();
    ceilingHeight = lump.ReadShort();
    floorTexture = lump.ReadString().ToUppercase();
    ceilingTexture = lump.ReadString().ToUppercase();
    lightLevel = lump.ReadShort();
    type = lump.ReadShort();
    tag = lump.ReadShort();
  }
  public int adjSectorFrom, adjSectorUpto;
}
record struct Sidedef: IMapComponent {
  public static (string, int) Info => ("SIDEDEFS", 30);
  public double xOffset, yOffset;
  public ShortString upperTexture, middleTexture, lowerTexture;
  public int sectorIdx;
  public void Init(Lump lump) {
    xOffset = lump.ReadShort();
    yOffset = lump.ReadShort();
    upperTexture = lump.ReadString().ToUppercase();
    lowerTexture = lump.ReadString().ToUppercase();
    middleTexture = lump.ReadString().ToUppercase();
    sectorIdx = lump.ReadReference();
  }
}
