class MapRenderer {
  Scene scene;
  Player player;
  public uint[] pixels;
  public short canvasWidth, canvasHeight;
  double mapLeft, mapBottom, mapRight, mapTop, mapWidth, mapHeight;
  double viewportX, viewportY, viewportWidth, viewportHeight, scaleX, scaleY;
  public MapRenderer(Scene scene, Player player, short width, short height) {
    this.scene = scene;
    this.player = player;
    canvasWidth = width;
    canvasHeight = height;
    pixels = new uint[canvasWidth * canvasHeight];
    mapLeft = scene.vertexes.MinBy(v => v.x).x;
    mapBottom = scene.vertexes.MinBy(v => v.y).y;
    mapRight =  scene.vertexes.MaxBy(v => v.x).x;
    mapTop = scene.vertexes.MaxBy(v => v.y).y;
    viewportWidth = canvasWidth * 0.95;
    viewportHeight = canvasHeight * 0.95;
    mapWidth = mapRight - mapLeft;
    mapHeight = mapTop - mapBottom;
    if (mapWidth / mapHeight > viewportWidth / viewportHeight) {
      viewportHeight = viewportWidth * mapHeight / mapWidth;
    } else {
      viewportWidth = viewportHeight * mapWidth / mapHeight;
    }
    viewportX = (canvasWidth - viewportWidth) / 2;
    viewportY = (canvasHeight - viewportHeight) / 2;
    scaleX = viewportWidth /  mapWidth;
    scaleY = viewportHeight / mapHeight;
  }  
  double MapWorldToCanvasX(double x) {
    return (x - mapLeft) * scaleX + viewportX;
  }
  double MapWorldToCanvasY(double y) {
    return canvasHeight - ((y - mapBottom) * scaleY + viewportY);
  }
  void DrawLine(double x1, double y1, double x2, double y2, uint color) {
    double steps = Math.Max(Math.Abs(x2 - x1), Math.Abs(y2 - y1));
    double dx = (x2 - x1) / steps;
    double dy = (y2 - y1) / steps;
    for (int i = 0; i < steps; i++) {
      pixels[(int)y1 * canvasWidth + (int)x1] = color;
      x1 += dx;
      y1 += dy;
    }
  }
  bool IsWithinBounds(double x, double y, int padding) {
    return padding <= x && x < canvasWidth  - padding && 
           padding <= y && y < canvasHeight - padding;
  }

  public void RenderMap() {
    Array.Clear(pixels);
    double xFrom = (int)Math.Ceiling(mapLeft  / 1024 - 3) * 1024;
    double xUpto = (int)Math.Ceiling(mapRight / 1024 + 3) * 1024;
    for (double x = xFrom; x < xUpto; x += 1024) {
      int col = (int)MapWorldToCanvasX(x);
      if (col < 0 || col >= canvasWidth) continue;
      int step = canvasWidth * 2;
      for (int i = col; i < pixels.Length; i += step) {
        pixels[i] = 0xffaaaaaa;
      }
    }
    double yFrom = (int)Math.Ceiling(mapBottom / 1024 - 3) * 1024;
    double yUpto = (int)Math.Ceiling(mapTop    / 1024 + 3) * 1024;
    for (double y = yFrom; y < yUpto; y += 1024) {
      int row = (int)MapWorldToCanvasY(y);
      if (row < 0 || row >= canvasHeight) continue;
      int rowFrom = row * canvasWidth;
      int rowUpto = rowFrom + canvasWidth;
      for (int i = rowFrom; i < rowUpto; i += 2) {
        pixels[i] = 0xffaaaaaa;
      }
    }
    for (int i = 0; i < scene.linedefs.Length; i++) {
      ref var linedef = ref scene.linedefs[i];
      ref var v1 = ref scene.vertexes[linedef.startVertexIdx];
      ref var v2 = ref scene.vertexes[linedef.endVertexIdx];
      uint color = linedef.backSidedefIdx == -1 ? 0xff8866ff : 0xffaaaaff;
      double x1 = MapWorldToCanvasX(v1.x);
      double y1 = MapWorldToCanvasY(v1.y);
      double x2 = MapWorldToCanvasX(v2.x);
      double y2 = MapWorldToCanvasY(v2.y);
      DrawLine(x1, y1, x2, y2, color);
    }
    double px0 = MapWorldToCanvasX(player.x);
    double py0 = MapWorldToCanvasY(player.y);
    int size = Math.Max(1, canvasWidth / 320);
    if (IsWithinBounds(px0, py0, size)) {
      int pCol = (int)px0, pRow = (int)py0;
      for (int row = pRow - size; row <= pRow + size; row++) {
        for (int col = pCol - size; col <= pCol + size; col++) {
          pixels[row * canvasWidth + col] = 0xffffffff;
        }
      }
      double px1 = px0 + 6 * size * player.losDx;
      double py1 = py0 - 6 * size * player.losDy;
      if (IsWithinBounds(px1, py1, 1)) {
        DrawLine(px0, py0, px1, py1, 0xffffffff);
      }
    }
  }
}
