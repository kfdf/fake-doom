partial class Scene {
  public Vertex[] vertexes;
  public Linedef[] linedefs;
  public Sidedef[] sidedefs;
  public Node[] nodes;
  public Subsector[] subsectors;
  public Sector[] sectors;
  public Segment[] segments;
  public Thing[] things;
  public Dictionary<ShortString, Picture> floaters = [];
  public Dictionary<ShortString, Picture> sprites = [];
  public Dictionary<ShortString, byte[]> flatTexs = [];
  public Dictionary<ShortString, Texture> wallTexs = [];
  enum TextureLocation { Upper, Lower, Middle }
  record struct TextureReference(int index, TextureLocation location);
  HashSet<TextureReference> animatedWalls = [], animatedFlats = [];
  HashSet<int> scrollingWalls = [];
  public Texture skyTexture;
  public byte[][] colormaps;
  public byte[] spectreColormap;
  Dictionary<int, double> doorSectors = null;
  Dictionary<int, (short origIdx, short maxIdx)> dynamicLightSectors = [];
  (int sectorIdx1, int sectorIdx2, int sidedefIdx)[] adjSectors;
  public int frameCount;
  public static ShortString NO_TEXTURE = "-"; 
  public static ShortString SKY_TEXTURE = "F_SKY1";
  public static double[] Sins = new double[360 + 90];
  static Scene() {
    for (int ang = 0; ang < Sins.Length; ang++) {
      Sins[ang] = Math.Sin(ang * Math.PI / 180);
    }
  }
  public Scene(WadReader wad, ShortString mapName, ShortString skyTexture) {
    vertexes = wad.ReadMap<Vertex>(mapName);
    linedefs = wad.ReadMap<Linedef>(mapName);
    sidedefs = wad.ReadMap<Sidedef>(mapName);
    sectors = wad.ReadMap<Sector>(mapName);
    things = wad.ReadMap<Thing>(mapName);
    subsectors = wad.ReadMap<Subsector>(mapName);
    if (subsectors.Length != 0) {
      nodes = wad.ReadMap<Node>(mapName);
      segments = wad.ReadMap<Segment>(mapName);
    } else {
      (var extraVertexes, subsectors, segments, nodes) = wad.ReadExtended(mapName);
      int firstSegIdx = 0;
      for (int i = 0; i < subsectors.Length; i++) {
        subsectors[i].firstSegIdx = firstSegIdx;
        firstSegIdx += subsectors[i].segmentCount;
      }

      int origLength = vertexes.Length;
      Array.Resize(ref vertexes, vertexes.Length + extraVertexes.Length);
      extraVertexes.CopyTo(vertexes, origLength);

      for (int i = 0; i < segments.Length; i++) {
        ref var segment = ref segments[i];
        ref var linedef =  ref linedefs[segment.linedefIdx];
        var segmentStart = vertexes[segment.startVertexIdx];
        var lineStart = vertexes[segment.isBackside ? linedef.endVertexIdx : linedef.startVertexIdx];
        double dx = segmentStart.x - lineStart.x;
        double dy = segmentStart.y - lineStart.y;
        segment.offset = Math.Sqrt(dx * dx + dy * dy);
      }
    }
    adjSectors = new (int, int, int)[
      linedefs.Count(ld => ld.backSidedefIdx != -1) * 2
    ];
    if (adjSectors.Length > 0) {
      int idx = 0;
      for (int i = 0; i < linedefs.Length; i++) {
        ref var linedef = ref linedefs[i];
        if (linedef.backSidedefIdx == -1) continue;
        int frontSectorIdx = sidedefs[linedef.frontSidedefIdx].sectorIdx;
        int backSectorIdx = sidedefs[linedef.backSidedefIdx].sectorIdx;
        adjSectors[idx++] = new(frontSectorIdx, backSectorIdx, linedef.frontSidedefIdx);
        adjSectors[idx++] = new(backSectorIdx, frontSectorIdx, linedef.backSidedefIdx);
      }
      Array.Sort(adjSectors);
      int j = 0;
      foreach (var adjSector in adjSectors) {
        if (adjSector != adjSectors[j]) adjSectors[++j] = adjSector;
      }
      adjSectors = adjSectors[..(j + 1)];
      for (int i = 1; i < adjSectors.Length; i++) {
        if (adjSectors[i].sectorIdx1 == adjSectors[i - 1].sectorIdx1) continue;
        sectors[adjSectors[i - 1].sectorIdx1].adjSectorUpto = i;
        sectors[adjSectors[i].sectorIdx1].adjSectorFrom = i;
      }
      sectors[adjSectors[^1].sectorIdx1].adjSectorUpto = (int)adjSectors.Length;
    }
    for (int i = 0; i < segments.Length; i++) {
      ref var segment = ref segments[i];
      ref var linedef = ref linedefs[segment.linedefIdx];
      segment.frontSidedefIdx = segment.isBackside ? linedef.backSidedefIdx : linedef.frontSidedefIdx;
      segment.backSidedefIdx = segment.isBackside ? linedef.frontSidedefIdx : linedef.backSidedefIdx;
      segment.frontSectorIdx = sidedefs[segment.frontSidedefIdx].sectorIdx;
      segment.backSectorIdx = segment.backSidedefIdx == -1 ? 
        -1 : sidedefs[segment.backSidedefIdx].sectorIdx;
      ref var start = ref vertexes[segment.startVertexIdx];
      ref var end = ref vertexes[segment.endVertexIdx];
      double dx = end.x - start.x;
      double dy = end.y - start.y;
      double invLength = 1 / Math.Sqrt(dx * dx + dy * dy);
      segment.dx = dx * invLength;
      segment.dy = dy * invLength;
      segment.invLength = invLength;
    }

    Dictionary<short, double> radiiByThingType = [];
    for (int i = 0; i < things.Length; i++) {
      ref var thing = ref things[i];
      if ((thing.flags & 0b10100) != 0b00100) continue;
      if (!ThingType.Data.TryGetValue(thing.type, out var typeData)) continue;
      thing.sectorIdx = FindSectorIdx(thing.x, thing.y);
      if (!radiiByThingType.TryGetValue(thing.type, out double radius)) {
        radius = 0;
        foreach (var spritesForAngle in typeData.sprites) {
          foreach (var (spriteName, _) in spritesForAngle) {
            if (!sprites.TryGetValue(spriteName, out var sprite)) {
              sprites[spriteName] = sprite = wad.ReadSprite(spriteName);
            }
            radius = Math.Max(radius, sprite.width);
          }
        }
        radius /= 2;
      }
      int nodeIdx = nodes.Length - 1;
      while (true) {
        ref var node = ref nodes[nodeIdx];
        double dist = (thing.x - node.x) * node.dy - (thing.y - node.y) * node.dx;
        bool fullyInsideChildBox = Math.Abs(dist) >= radius;
        if (fullyInsideChildBox) {
          var childBox = dist > 0 ? node.rightBox : node.leftBox;
          fullyInsideChildBox = 
            thing.y <= childBox.top - radius &&
            thing.y >= childBox.bottom + radius &&
            thing.x <= childBox.right - radius &&
            thing.x >= childBox.left + radius;
        }
        if (!fullyInsideChildBox) {
          thing.nextThingIdx = node.firstThingIdx;
          node.firstThingIdx = i;
          break;
        }
        var childRef = dist > 0 ? node.rightRef : node.leftRef;
        if (!childRef.isNode) {
          ref var subsector = ref subsectors[childRef.childIdx];
          thing.nextThingIdx = subsector.firstThingIdx;
          subsector.firstThingIdx = i;
          break;
        }
        nodeIdx = childRef.childIdx;
      }
    } 
    for (int i = 0; i < linedefs.Length; i++) {
      ref var linedef = ref linedefs[i];
      if (linedef.lineType == 48) {
        scrollingWalls.Add(linedef.frontSidedefIdx);
      }
      int backSidedefIdx = linedef.backSidedefIdx;
      int frontSidedefIdx = linedef.frontSidedefIdx;
      if (backSidedefIdx == -1) {
        if (frontSidedefIdx == -1) continue;
        readWallTexture(frontSidedefIdx, TextureLocation.Middle);
      } else {
        readWallTexture(frontSidedefIdx, TextureLocation.Lower);
        readWallTexture(frontSidedefIdx, TextureLocation.Upper);
        readWallTexture(frontSidedefIdx, TextureLocation.Middle, true);
        readWallTexture(backSidedefIdx, TextureLocation.Lower);
        readWallTexture(backSidedefIdx, TextureLocation.Upper);
        readWallTexture(backSidedefIdx, TextureLocation.Middle, true);
      }
    }
    void readWallTexture(int sidedefIdx, TextureLocation location, bool isFloater = false) {
      var name = location switch {
        TextureLocation.Upper => sidedefs[sidedefIdx].upperTexture,
        TextureLocation.Middle => sidedefs[sidedefIdx].middleTexture,
        TextureLocation.Lower or _ => sidedefs[sidedefIdx].lowerTexture,
      };
      while (name != default && name != NO_TEXTURE) {
        if (AnimatedTextures.Walls.TryGetValue(name, out var nextName)) {
          animatedWalls.Add(new(sidedefIdx, location));
        }
        if (isFloater) {
          if (floaters.ContainsKey(name)) return;
          floaters[name] = wad.ReadFloater(name);
        } else {
          if (wallTexs.ContainsKey(name)) return;
          wallTexs[name] = wad.ReadTexture(name);
        }
        name = nextName;
      }
    }
    for (int i = 0; i < sectors.Length; i++) {
      ref var sector = ref sectors[i];
      readFlatTexture(i, TextureLocation.Upper);
      readFlatTexture(i, TextureLocation.Lower);
      sector.colormapIndex = (short)(~(sector.lightLevel >> 3) & 0x1f);
    }
    void readFlatTexture(int sectorIdx, TextureLocation location) {
      var name = location switch {
        TextureLocation.Upper => sectors[sectorIdx].ceilingTexture,
        TextureLocation.Lower or _ => sectors[sectorIdx].floorTexture,
      };
      while (name != default && name != NO_TEXTURE) {
        if (AnimatedTextures.Flats.TryGetValue(name, out var nextName)) {
          animatedFlats.Add(new(sectorIdx, location));
        }
        if (flatTexs.ContainsKey(name)) return;
        flatTexs[name] = wad.ReadFlat(name);
        name = nextName;
      }
    }

    Span<short> types = [1, 2, 3, 4, 8, 12, 13, 17];
    for (int i = 0; i < sectors.Length; i++) {
      ref var sector = ref sectors[i];
      if (!types.Contains(sector.type)) continue;
      short maxColormapIdx = 0;
      for (int j = sector.adjSectorFrom; j < sector.adjSectorUpto; j++) {
        var adjSectorIdx = adjSectors[j].sectorIdx2;
        short adjColormapIdx = sectors[adjSectorIdx].colormapIndex;
        maxColormapIdx = Math.Max(adjColormapIdx, maxColormapIdx);
      }
      dynamicLightSectors[i] = (sector.colormapIndex, maxColormapIdx);
    }

    this.skyTexture = wad.ReadTexture(skyTexture);
    colormaps = wad.ReadColormaps();
    spectreColormap = new byte[0x400];
    colormaps[4].CopyTo(spectreColormap, 0);
    colormaps[8].CopyTo(spectreColormap, 0x100);
    colormaps[12].CopyTo(spectreColormap, 0x200);
    colormaps[16].CopyTo(spectreColormap, 0x300);
  }
  public int FindSectorIdx(double x, double y) {
    int childIdx = nodes.Length - 1;
    while (true) {
      ref var node = ref nodes[childIdx];
      var rightSide = (x - node.x) * node.dy - (y - node.y) * node.dx >= 0;
      var childRef = rightSide ? node.rightRef : node.leftRef;
      childIdx = childRef.childIdx;
      if (!childRef.isNode) break;
    }
    int segmentIdx = subsectors[childIdx].firstSegIdx;
    return segments[segmentIdx].frontSectorIdx;
  }  
  public void Update(InputState input) {
    frameCount++; 
    if (doorSectors != null && !input.doorsOpen) {
      foreach (var (liftIdx, initialHeight) in doorSectors) {
        ref var sector = ref sectors[liftIdx];
        if (initialHeight != sector.floorHeight) {
          sector.floorHeight = initialHeight;
        } else {
          sector.ceilingHeight = initialHeight;
        }
      }
      doorSectors = null;
    } else if (doorSectors == null && input.doorsOpen) {
      doorSectors = [];
      for (int i = 0; i < sectors.Length; i++) {
        ref var sector = ref sectors[i];
        if (sector.adjSectorUpto == 0) continue;
        if (sector.floorHeight == sector.ceilingHeight) {
          double highestFloor = -100000, lowestCeiling = 100000;
          for (int j = sector.adjSectorFrom; j < sector.adjSectorUpto; j++) {
            int adjSectorIdx = adjSectors[j].sectorIdx2;
            highestFloor = Math.Max(highestFloor, sectors[adjSectorIdx].floorHeight);
            lowestCeiling = Math.Min(lowestCeiling, sectors[adjSectorIdx].ceilingHeight);
          }
          doorSectors[i] = sector.floorHeight;
          if (sector.floorHeight > (highestFloor + lowestCeiling) / 2) {
            sector.floorHeight = highestFloor;
          } else {
            sector.ceilingHeight = lowestCeiling;
          }
        } else {
          double adjFloorHeight = 0;
          int j = sector.adjSectorFrom - 1;
          while (++j < sector.adjSectorUpto) {
            var (_, adjSectorIdx, sidedefIdx) = adjSectors[j];
            if (sidedefs[sidedefIdx].lowerTexture != NO_TEXTURE) break;
            adjFloorHeight = sectors[adjSectorIdx].floorHeight;
            if (adjFloorHeight <= sector.floorHeight) break;
          }
          if (j != sector.adjSectorUpto) continue;
          doorSectors[i] = sector.floorHeight;
          sector.floorHeight = adjFloorHeight;
        }
      }
    }
    foreach (var (sectorIdx, (origColormapIdx, maxColormapIdx)) in dynamicLightSectors) {
      ref var sector = ref sectors[sectorIdx];
      if (sector.type == 1) {
        if (origColormapIdx < maxColormapIdx && (frameCount + sectorIdx & 0b1111110) == 0) {
          sector.colormapIndex = maxColormapIdx;
        } else {
          sector.colormapIndex = origColormapIdx;
        }
      } else if (sector.type == 8) {
        if (origColormapIdx < maxColormapIdx) {
          int range = maxColormapIdx - origColormapIdx;
          int zigzag = Math.Abs(frameCount % (range * 2) - range);
          sector.colormapIndex = (short)(origColormapIdx + zigzag);
        }
      } else if (sector.type == 17) {
        if (origColormapIdx < maxColormapIdx && (frameCount & 3) == 0) {
          int range = maxColormapIdx - origColormapIdx;
          int random = 4441 * ((frameCount >> 2) + sectorIdx) % 997 % range;
          sector.colormapIndex = (short)(origColormapIdx + random);
        }
      } else {
        int shiftedFrameCount = frameCount + (sector.type <= 4 ? sectorIdx : 0);
        int mask = sector.type == 3 || sector.type == 12 ? 0b111100 : 0b11100;
        if ((shiftedFrameCount & mask) != 0) {
          sector.colormapIndex = maxColormapIdx;
        } else {
          sector.colormapIndex = maxColormapIdx > origColormapIdx ? origColormapIdx : (short)0;
        }
      }
    }
    foreach (int sidedefIdx in scrollingWalls) {
      sidedefs[sidedefIdx].xOffset += 1;
    }
    if (frameCount % 12 == 0) {
      foreach (var (sectorIdx, location) in animatedFlats) {
        ref var sector = ref sectors[sectorIdx];
        if (location == TextureLocation.Upper) {
          sector.ceilingTexture = AnimatedTextures.Flats[sector.ceilingTexture];
        } else {
          sector.floorTexture = AnimatedTextures.Flats[sector.floorTexture];
        }
      }
      foreach (var (sidedefIdx, location) in animatedWalls) {
        ref var sidedef = ref sidedefs[sidedefIdx];
        if (location == TextureLocation.Upper) {
          sidedef.upperTexture = AnimatedTextures.Walls[sidedef.upperTexture];
        } else if (location == TextureLocation.Middle) {
          sidedef.middleTexture = AnimatedTextures.Walls[sidedef.middleTexture];
        } else {
          sidedef.lowerTexture = AnimatedTextures.Walls[sidedef.lowerTexture];
        }
      }
    }
  }
}
