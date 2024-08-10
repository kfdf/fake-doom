partial class Renderer {
  Stack<FloaterRenderer> floaterPool = [];
  FloaterRenderer acquireFloaterRenderer(in Segment segment, in EdgesProj edgesProj) {
    var ret = floaterPool.Count == 0 ? new(this) : floaterPool.Pop();
    ret.Init(segment, edgesProj);
    return ret;
  }
  class FloaterRenderer(Renderer renderer) : IDisposable {
    Scene scene = renderer.scene;
    Player player = renderer.player;
    Picture floater;
    double leftDist, rightDist;
    double segmentDx, segmentDy, segmentOffset, segmentInvLength;
    double vertexX, vertexY;
    int colormapIndex;
    double wallBaseHeight, floorHeight, ceilingHeight;
    double intersecDist, intersecOffset;
    short column, columnUpto, columnStep;
    bool unpegged;
    public int minDist;
    double playerDist, playerProj;

    public void Init(in Segment segment, in EdgesProj proj) {
      leftDist = proj.dist1;
      rightDist = proj.dist2;
      if (leftDist > rightDist) {
        minDist = (int)Math.Max(1, rightDist - 0.00001);
        column = (short)(proj.col1 - 1);
        columnUpto = (short)(proj.col2 - 1);
        columnStep = 1;
      } else {
        minDist = (int)Math.Max(1, leftDist - 0.00001);
        column = proj.col2;
        columnUpto = proj.col1;
        columnStep = -1;
      }
      (segmentDx, segmentDy) = (segment.dx, segment.dy);
      (segmentOffset, segmentInvLength) = (segment.offset, segment.invLength);
      ref var vertex = ref scene.vertexes[segment.startVertexIdx];
      (vertexX, vertexY) = (vertex.x, vertex.y);
      unpegged = scene.linedefs[segment.linedefIdx].lowerUnpegged;
      ref var sector = ref scene.sectors[segment.frontSectorIdx];
      colormapIndex = sector.colormapIndex;
      ref var backSector = ref scene.sectors[segment.backSectorIdx];

      ref var sidedef = ref scene.sidedefs[segment.frontSidedefIdx];
      segmentOffset += sidedef.xOffset;
      floater = scene.floaters[sidedef.middleTexture];
      floorHeight = Math.Max(sector.floorHeight, backSector.floorHeight);
      ceilingHeight = Math.Min(sector.ceilingHeight, backSector.ceilingHeight);
      wallBaseHeight = unpegged ? floorHeight : ceilingHeight - floater.height + sidedef.yOffset;
      playerDist = (player.x - vertex.x) * segment.dy - (player.y - vertex.y) * segment.dx;
      playerProj = (player.x - vertex.x) * segment.dx + (player.y - vertex.y) * segment.dy;
    }
    public void Dispose() {
      floater = null;
      renderer.floaterPool.Push(this);
    }
    public double MoveToNextColumn() {
      if (column == columnUpto) return intersecDist = 0;
      column += columnStep;
      var columnVec = renderer.columnVectors[column];
      double columnDx = player.x + columnVec.x - vertexX;
      double columnDy = player.y + columnVec.y - vertexY;
      double columnDist = columnDx * segmentDy - columnDy * segmentDx;
      double columnProj = columnDx * segmentDx + columnDy * segmentDy;
      double ratio = (columnProj - playerProj) / (columnDist - playerDist);
      intersecOffset = columnProj - columnDist * ratio;
      intersecDist = leftDist + (rightDist - leftDist) * intersecOffset * segmentInvLength;
      return intersecDist;
    }
    public double RenderUntil(double minDist) {
      var (pixels, viewportSize) = (renderer.pixels, renderer.viewportWidth);
      while (intersecDist >= minDist) {
        double scale = renderer.projDist / intersecDist;
        double colCanvasBottom = renderer.horizon + (player.height - wallBaseHeight) * scale;
        double colCanvasTop = colCanvasBottom - floater.height * scale;
        int floaterX = (int)(segmentOffset + intersecOffset) % floater.width;
        if (floaterX < 0) floaterX += floater.width;
        var colormap = renderer.GetColormap(colormapIndex, intersecDist);
        var (rangeFrom, rangeUpto) = renderer.ZBufferPopUntil(column, intersecDist);
        double rangeBeg = renderer.horizon + (player.height - ceilingHeight) * scale;
        double rangeEnd = renderer.horizon + (player.height - floorHeight) * scale;
        rangeFrom = (short)Math.Clamp(Math.Ceiling(rangeBeg), rangeFrom, rangeUpto);
        rangeUpto = (short)Math.Clamp(Math.Ceiling(rangeEnd), rangeFrom, rangeUpto);
        double floaterToCanvas = (colCanvasBottom - colCanvasTop) / floater.height;
        double canvasToFloater = 1 / floaterToCanvas;
        bool rendereredSomething = false;
        foreach (var post in floater.EnumeratePosts(floaterX)) {
          double postBeg = post.from * floaterToCanvas + colCanvasTop;
          double postEnd = post.upto * floaterToCanvas + colCanvasTop;
          short postFrom = (short)Math.Clamp(Math.Ceiling(postBeg), rangeFrom, rangeUpto);
          short postUpto = (short)Math.Clamp(Math.Ceiling(postEnd), rangeFrom, rangeUpto);
          if (postFrom >= postUpto) continue;
          rendereredSomething = true;
          var postPixels = post.pixels.Span;
          double postIdx = (postFrom - colCanvasTop) * canvasToFloater - post.from + 1;
          int idxFrom = postFrom * viewportSize + column;
          int idxUpto = postUpto * viewportSize;
          for (int idx = idxFrom; idx < idxUpto; idx += viewportSize) {
            pixels[idx] = colormap[postPixels[(int)postIdx]];
            postIdx += canvasToFloater;
          }
        }
        if (rendereredSomething) renderer.InvokeCallback(column);
        MoveToNextColumn();
      }
      return intersecDist;
    }
  }  
}
