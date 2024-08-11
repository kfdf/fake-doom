partial class Renderer {
  void DrawSkyColumn(short column, short from, short upto) {
    int idx = from * viewportWidth + column;
    double offset = -player.angle * (1 / 90.0) + 0.5 + 
      (column + viewportOffset - midline) * invDist * 0.5;
    offset *= invSkyTextureWidth;
    short skyColumn = (short)((offset - Math.Floor(offset)) * scene.skyTexture.width);
    int skyPixelsColStart = skyColumn * scene.skyTexture.height;
    short skyFrom = (short)Math.Ceiling(horizon - canvasHeight / player.fov);
    short skyFromClamped = Math.Clamp(skyFrom, from, upto);
    var skyPixels = scene.skyTexture.data;
    for (int row = from; row < skyFromClamped; row++) {
      pixels[idx] = skyPixels[skyPixelsColStart];
      idx += viewportWidth;
    }
    double skyIdxStep = 100.0 / canvasHeight * player.fov;
    for (int row = skyFromClamped; row < upto; row++) {
      int skyColIdx = (int)((row - skyFrom) * skyIdxStep) & 0x7f;
      pixels[idx] = skyPixels[skyPixelsColStart + skyColIdx];
      idx += viewportWidth;
    }
  }  
  void DrawWallColumn(
    short column, short from, short upto,
    Texture texture, byte[] colormap, double dist,
    double textureWorldTop, double textureX
  ) {
    int texCol = (int)textureX % texture.width;
    if (texCol < 0) texCol += texture.width;
    double texWorldStep = dist * invDist;
    double texWorldRow = (horizon - from) * texWorldStep + player.height;
    int textureColStart = texture.height * texCol;
    var wallPixels = texture.data;
    int idxFrom = from * viewportWidth + column;
    int idxUpto = upto * viewportWidth;
    for (int idx = idxFrom; idx < idxUpto; idx += viewportWidth) {
      int texRow = (int)(textureWorldTop - texWorldRow) & 0x7f;
      texWorldRow -= texWorldStep;
      pixels[idx] = colormap[wallPixels[textureColStart + texRow]];
    }               
  }  
  ref struct SegmentProjection {
    Renderer renderer;
    Player player;
    ref readonly Segment segment;
    ref Vertex start;
    short col, upto;
    double playerDist, playerProj;
    public SegmentProjection(in Segment segment, short from, short upto, Renderer renderer) {
      this.renderer = renderer;
      this.player = renderer.player;
      this.segment = ref segment;
      start = ref renderer.scene.vertexes[segment.startVertexIdx];
      col = (short)(from - 1);
      this.upto = upto;
      double playerDx = player.x - start.x;
      double playerDy = player.y - start.y;
      playerDist = playerDx * segment.dy - playerDy * segment.dx;
      playerProj = playerDx * segment.dx + playerDy * segment.dy;
    }
    public (short column, double distance) Current { get; private set; }
    public SegmentProjection GetEnumerator() => this;

    public bool MoveNext() {
      do { if (++col >= upto) return false; } 
      while (renderer.IsColumnFull(col));
      var columnVec = renderer.columnVectors[col];
      double columnDx = player.x - start.x + columnVec.x;
      double columnDy = player.y - start.y + columnVec.y;
      double columnDist = columnDx * segment.dy - columnDy * segment.dx;
      double columnProj = columnDx * segment.dx + columnDy * segment.dy;
      double ratio = (columnProj - playerProj) / (columnDist - playerDist);
      Current = (col, columnProj - columnDist * ratio);
      return true;
    }
  }
  void RenderWall(in Segment segment, in EdgesProj proj) {
    counts.segments++;
    var (leftEdge, rightEdge, leftDist, rightDist) = proj;
    ref var sidedef = ref scene.sidedefs[segment.frontSidedefIdx];
    ref var sector = ref scene.sectors[segment.frontSectorIdx];
    double texWorldTop = 0;
    if (scene.wallTexs.TryGetValue(sidedef.middleTexture, out var wallTexture)) {
      texWorldTop = scene.linedefs[segment.linedefIdx].lowerUnpegged ?
        sector.floorHeight + wallTexture.height + sidedef.yOffset :
        sector.ceilingHeight + sidedef.yOffset;
    }
    floorRenderer.Init(sector);
    ceilingRenderer.Init(sector);
    foreach (var (column, interOffset) in new SegmentProjection(segment, leftEdge, rightEdge, this)) {
      double dist = leftDist + (rightDist - leftDist) * interOffset * segment.invLength;
      double scale = projDist / dist;
      var colormap = GetColormap(sector.colormapIndex, dist);
      var (rangeFrom, rangeUpto) = clippingRanges[column];
      double wallBeg = horizon - scale * (sector.ceilingHeight - player.height);
      double wallEnd = horizon - scale * (sector.floorHeight - player.height);
      short wallFrom = (short)Math.Clamp(Math.Ceiling(wallBeg), rangeFrom, rangeUpto);
      short wallUpto = (short)Math.Clamp(Math.Ceiling(wallEnd), rangeFrom, rangeUpto);
      bool renderedSomething = false;
      if (rangeFrom < wallFrom) {
        if (sector.ceilingTexture == Scene.SKY_TEXTURE) {
          DrawSkyColumn(column, rangeFrom, wallUpto);
          renderedSomething = true;
        } else {
          ceilingRenderer.AddColumn(column, rangeFrom, wallFrom);
        }
      }
      if (wallUpto < rangeUpto) {
        if (sector.floorTexture == Scene.SKY_TEXTURE) {
          DrawSkyColumn(column, wallUpto, rangeUpto);
          renderedSomething = true;
        } else {
          floorRenderer.AddColumn(column, wallUpto, rangeUpto);
        }
      }
      if (wallFrom < wallUpto && wallTexture != null) {
        double textureX = segment.offset + interOffset + sidedef.xOffset;
        DrawWallColumn(column, wallFrom, wallUpto, wallTexture, colormap, dist, texWorldTop, textureX);
        renderedSomething = true;
      }
      ZBufferPush(column, 0, 0, dist, true, true);
      if (renderedSomething) InvokeCallback(column);
    }
    MarkColumnsAsFull(leftEdge, rightEdge);
  }

  void RenderPortal(in Segment segment, in EdgesProj edgesProj) {
    var (leftEdge, rightEdge, leftDist, rightDist) = edgesProj;
    ref var sidedef = ref scene.sidedefs[segment.frontSidedefIdx];
    ref var linedef = ref scene.linedefs[segment.linedefIdx];
    ref var frontSector = ref scene.sectors[segment.frontSectorIdx];
    ref var backSector = ref scene.sectors[segment.backSectorIdx];
    bool diffCeilings = 
      frontSector.ceilingHeight != backSector.ceilingHeight ||
      frontSector.colormapIndex != backSector.colormapIndex ||
      frontSector.ceilingTexture != backSector.ceilingTexture ||
      backSector.ceilingHeight == backSector.floorHeight;
    bool diffFloors = 
      frontSector.floorHeight != backSector.floorHeight ||
      frontSector.colormapIndex != backSector.colormapIndex ||
      frontSector.floorTexture != backSector.floorTexture ||
      backSector.ceilingHeight == backSector.floorHeight;
    bool upperWallReal = 
      frontSector.ceilingTexture != Scene.SKY_TEXTURE ||
      backSector.ceilingTexture != Scene.SKY_TEXTURE;
    bool isCeilingVisible =   frontSector.ceilingHeight > player.height && diffCeilings && upperWallReal;
    bool isUpperWallVisible = frontSector.ceilingHeight > backSector.ceilingHeight && upperWallReal;
    bool isLowerWallVisible = frontSector.floorHeight   < backSector.floorHeight;
    bool isFloorVisible =     frontSector.floorHeight   < player.height && diffFloors;
    if (!(isUpperWallVisible || isCeilingVisible || isLowerWallVisible || isFloorVisible)) return;
    counts.segments++;

    Texture upperWallTexture = null, lowerWallTexture = null;
    double upperTexWorldTop = 0, lowerTexWorldTop = 0;
    if (isUpperWallVisible && (isUpperWallVisible = 
      scene.wallTexs.TryGetValue(sidedef.upperTexture, out upperWallTexture)
    )) {
      upperTexWorldTop = linedef.upperUnpegged ? 
        frontSector.ceilingHeight + sidedef.yOffset: 
        backSector.ceilingHeight + upperWallTexture.height + sidedef.yOffset;
    }
    if (isLowerWallVisible && (
      isLowerWallVisible = scene.wallTexs.TryGetValue(sidedef.lowerTexture, out lowerWallTexture)
    )) {
      lowerTexWorldTop = linedef.lowerUnpegged ? 
        frontSector.ceilingHeight + sidedef.yOffset : 
        backSector.floorHeight + sidedef.yOffset;
    }
    if (isCeilingVisible && frontSector.ceilingTexture != Scene.SKY_TEXTURE) {
      ceilingRenderer.Init(frontSector);
    }
    if (isFloorVisible && frontSector.floorTexture != Scene.SKY_TEXTURE) {
      floorRenderer.Init(frontSector);
    }
    bool upperClip = upperWallReal && (
      backSector.ceilingHeight < player.height ||
      backSector.ceilingHeight > frontSector.ceilingHeight);
    bool lowerClip = 
      backSector.floorHeight > player.height ||
      backSector.floorHeight < frontSector.floorHeight;
    bool addClippingWindow = lowerClip || upperClip;

    foreach (var (column, interOffset) in new SegmentProjection(segment, leftEdge, rightEdge, this)) {
      double dist = leftDist + (rightDist - leftDist) * interOffset * segment.invLength;
      double scale = projDist / dist;
      var colormap = GetColormap(frontSector.colormapIndex, dist);
      var (rangeFrom, rangeUpto) = clippingRanges[column];
      bool rendereredSomething = false;
      if (isCeilingVisible) {
        double ceilingEnd = horizon - scale* (frontSector.ceilingHeight - player.height);
        short ceilingUpto = (short)Math.Clamp(Math.Ceiling(ceilingEnd), rangeFrom, rangeUpto);
        if (rangeFrom < ceilingUpto) {
          if (frontSector.ceilingTexture == Scene.SKY_TEXTURE) {
            DrawSkyColumn(column, rangeFrom, ceilingUpto);
            rendereredSomething = true;
          } else {
            ceilingRenderer.AddColumn(column, rangeFrom, ceilingUpto);
          }
          rangeFrom = ceilingUpto;
        }
      }
      if (isFloorVisible) {
        double floorBeg = horizon - scale * (frontSector.floorHeight - player.height);
        short floorFrom = (short)Math.Clamp(Math.Ceiling(floorBeg), rangeFrom, rangeUpto);
        if (floorFrom < rangeUpto) {
          if (frontSector.floorTexture == Scene.SKY_TEXTURE) {
            DrawSkyColumn(column, floorFrom, rangeUpto);
            rendereredSomething = true;
          } else {
            floorRenderer.AddColumn(column, floorFrom, rangeUpto);
          }
          rangeUpto = floorFrom;
        }
      }
      if (isUpperWallVisible) {
        double wallEnd = horizon - scale * (backSector.ceilingHeight - player.height);
        short wallUpto = (short)Math.Clamp(Math.Ceiling(wallEnd), rangeFrom, rangeUpto);
        if (rangeFrom < wallUpto) {
          double textureX = segment.offset + interOffset + sidedef.xOffset;
          DrawWallColumn(column, rangeFrom, wallUpto, upperWallTexture, colormap, dist, upperTexWorldTop, textureX);
          rendereredSomething = true;
          rangeFrom = wallUpto;
        }              
      }
      if (isLowerWallVisible) {
        double wallBeg = horizon - scale * (backSector.floorHeight - player.height);
        short wallFrom = (short)Math.Clamp(Math.Ceiling(wallBeg), rangeFrom, rangeUpto);
        if (wallFrom < rangeUpto) {
          double textureX = segment.offset + interOffset + sidedef.xOffset;
          DrawWallColumn(column, wallFrom, rangeUpto, lowerWallTexture, colormap, dist, lowerTexWorldTop, textureX);
          rendereredSomething = true;
          rangeUpto = wallFrom;
        }              
      }
      if (rangeFrom == rangeUpto) {
        MarkColumnAsFull(column);
      } else {
        clippingRanges[column] = (rangeFrom, rangeUpto);
      }
      if (addClippingWindow) {
        ZBufferPush(column, rangeFrom, rangeUpto, dist, upperClip, lowerClip);
      }
      if (rendereredSomething) InvokeCallback(column);
    }
  }
}
