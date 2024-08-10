partial class Renderer {
  void DrawFlatRow(
    short row, short from, short upto, 
    byte[] texture, int colormapIdx, double height
  ) {
    double dist = height / (row - horizon) * projDist;
    var (columnDx, columnDy) = columnVectors[from];
    double stepX = +player.losDy * invDist * dist;
    double stepY = -player.losDx * invDist * dist;
    double flatX = player.x + columnDx * dist;
    double flatY = player.y + columnDy * dist;
    int rowStart = row * viewportWidth;
    var colormap = GetColormap(colormapIdx, dist);
    for (int col = from; col < upto; col++) {
      int idx = ((~(int)flatY & 0x3f) << 6) + ((int)flatX & 0x3f);
      flatX += stepX;
      flatY += stepY;
      pixels[rowStart + col] = colormap[texture[idx]];
    }
    InvokeCallback(~row);
  }  
  class FlatRenderer(Renderer renderer, bool isCeiling) {
    short[] linesFrom = new short[renderer.canvasHeight];
    bool isCeiling = isCeiling;
    ShortString texName;
    byte[] texture;
    int colormapIdx;
    double height;
    short upperFrom, upperUpto, lowerFrom, lowerUpto, columnFrom, columnUpto;
    BlockRenderer blockRenderer;
    Dictionary<(ShortString texName, double height, int colormapIdx), BlockRenderer> blockRenderers = [];
    public void Init(in Sector sector) {
      double surfaceHeight = isCeiling ? sector.ceilingHeight : sector.floorHeight;
      double newHeight = renderer.player.height - surfaceHeight;
      var newTexture = isCeiling ? sector.ceilingTexture : sector.floorTexture;
      if (texName == newTexture && height == newHeight && colormapIdx == sector.colormapIndex) return;
      AddColumn(0, 0, 0);
      texName = newTexture;
      texture = renderer.scene.flatTexs[texName];
      height = newHeight;
      colormapIdx = sector.colormapIndex;
      blockRenderer = null;
    }
    public void Flush() {
      AddColumn(0, 0, 0);
      foreach (var renderer in blockRenderers.Values) {
        renderer.RenderAll();
        renderer.Dispose();
      }
      blockRenderers.Clear();
      blockRenderer = null;
    }
    void DrawEdgeRows(short from, short upto) {
      for (short i = from; i < upto; i++) {
        renderer.DrawFlatRow(i, linesFrom[i], columnUpto, texture, colormapIdx, height);
      }
    }
    void DrawCoreRows(short from, short upto) {
      for (short i = from; i < upto; i++) {
        renderer.DrawFlatRow(i, columnFrom, columnUpto, texture, colormapIdx, height);
      }
    }
    public void AddColumn(short column, short from, short upto) {
      if (column != columnUpto || from == upto || 
        lowerUpto <= from  || upto <= upperFrom || 
        upto >= lowerUpto + 10 || from <= upperFrom - 10 ||
        upto <= lowerFrom - 10 || from >= upperUpto + 10
      ) {
        DrawEdgeRows(upperFrom, upperUpto);
        if (renderer.noblock || lowerFrom - upperUpto <= 10) {
          DrawCoreRows(upperUpto, lowerFrom);
        } else {
          if (blockRenderer == null) {
            var key = (texName, height, colormapIdx);
            if (!blockRenderers.TryGetValue(key, out blockRenderer)) {
              blockRenderer = renderer.acquireBlockRenderer(texture, height, colormapIdx);
              blockRenderers[key] = blockRenderer;
            }
          }
          blockRenderer.AddBlock(upperUpto, lowerFrom, columnFrom, columnUpto);
        }
        DrawEdgeRows(lowerFrom, lowerUpto);
        columnFrom = column;
        columnUpto = (short)(column + 1);
        upperFrom = upperUpto = from;
        lowerFrom = lowerUpto = upto;
        return;
      }
      if (from <= upperFrom) {
        for (short i = from; i < upperFrom; i++) {
          linesFrom[i] = column;
        }
        upperFrom = from;
      } else if (from <= upperUpto) {
        DrawEdgeRows(upperFrom, from);
        upperFrom = from;
      } else if (from <= lowerFrom) {
        DrawEdgeRows(upperFrom, upperUpto);
        DrawCoreRows(upperUpto, from);
        upperFrom = upperUpto = from;
      } else {
        DrawEdgeRows(upperFrom, upperUpto);
        DrawCoreRows(upperUpto, lowerFrom);
        DrawEdgeRows(lowerFrom, from);
        upperFrom = upperUpto = lowerFrom = from;
      }
      if (upto >= lowerUpto) {
        for (short i = lowerUpto; i < upto; i++) {
          linesFrom[i] = column;
        }
        lowerUpto = upto;
      } else if (upto >= lowerFrom) {
        DrawEdgeRows(upto, lowerUpto);
        lowerUpto = upto;
      } else if (upto >= upperUpto) {
        DrawCoreRows(upto, lowerFrom);
        DrawEdgeRows(lowerFrom, lowerUpto);
        lowerFrom = lowerUpto = upto;
      } else {
        DrawEdgeRows(upto, upperUpto);
        DrawCoreRows(upperUpto, lowerFrom);
        DrawEdgeRows(lowerFrom, lowerUpto);
        lowerFrom = lowerUpto = upperUpto = upto;
      }
      columnUpto = (short)(column + 1);
    }
  }
}
