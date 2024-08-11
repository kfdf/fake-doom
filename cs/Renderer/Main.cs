partial class Renderer {
  Scene scene;
  Player player;
  FlatRenderer floorRenderer, ceilingRenderer;
  List<(int dist, int thingIdx)> thingsToRender = [];
  List<FloaterRenderer> floaterRenderers = [];
  List<(int dist, int floaterIdx)> floatersToRender = [];
  public byte[] pixels;
  public short canvasWidth, canvasHeight;
  public short viewportOffset, viewportEnd, viewportWidth;
  double projDist, invDist, midline, invSkyTextureWidth;
  public double horizon;
  ulong[] mask;
  (float dist, short from, short upto)[] zbuffer;
  byte[] zbufferLengths;
  public (short from, short upto)[] clippingRanges;
  (double x, double y)[] columnVectors;
  public (int nodes, int segments, int things, int hiddenNodes) counts;
  public delegate void HoldingSpaceCallback(
    ReadOnlySpan<byte> data, int column, int viewportOffset, int viewportWidth
  );
  public HoldingSpaceCallback holdingSpaceCallback = null;
  public bool noblock, fullbright;
  public Renderer(
    Scene scene, Player player, short canvasWidth, 
    short canvasHeight, short viewportOffset, short viewportSize
  ) {
    this.scene = scene;
    this.player = player;
    this.canvasWidth = canvasWidth;
    this.canvasHeight = canvasHeight;
    this.viewportOffset = viewportOffset;
    this.viewportWidth = viewportSize;
    this.viewportEnd = (short)(viewportOffset + viewportSize);
    pixels = new byte[viewportSize * canvasHeight];
    projDist = canvasWidth / player.fov;
    invDist = player.fov / canvasWidth;
    horizon = canvasHeight * 0.5 - 0.5;
    midline = canvasWidth * 0.5 - 0.5;
    invSkyTextureWidth = 256.0 / scene.skyTexture.width;
    mask = new ulong[(viewportSize + 63) / 64];
    zbuffer = new (float, short, short)[viewportSize * 32];
    zbufferLengths = new byte[viewportSize];
    for (int col = 0; col < viewportSize; col++) {
      zbuffer[col << 5] = (0, 0, canvasHeight);
    }
    clippingRanges = new (short, short)[viewportSize];
    columnVectors = new (double, double)[viewportSize];
    floorRenderer = new(this, false);
    ceilingRenderer = new(this, true);
  }
  public void ZBufferPush(
    short column, short from, short upto, double dist, bool upperClip, bool lowerClip
  ) {
    byte length = zbufferLengths[column];
    if (length >= 31) {
      if (length == 32) return;
      upto = from;
    }
    int offset = (column << 5) + length;
    var (_, prevFrom, prevUpto) = zbuffer[offset - 1];
    if ((!upperClip || prevFrom == from) && (!lowerClip || prevUpto == upto)) return;
    zbuffer[offset] = ((float)dist, from, upto);
    zbufferLengths[column] = ++length;
  }
  public (short from, short upto) ZBufferPopUntil(short column, double distance) {
    int offset = (column << 5) - 1;
    byte length = zbufferLengths[column];
    while (true) {
      var (dist, from, upto) = zbuffer[offset + length];
      if (dist < distance) return (from, upto);
      zbufferLengths[column] = --length;
    }
  }
  public void MarkColumnAsFull(int col) {
    mask[col >> 6] |= 1UL << (col & 0x3f);
  }
  public bool IsColumnFull(int col) {
    return (mask[col >> 6] & 1UL << (col & 0x3f)) != 0;
  }
  public bool AreColumnsFull(int from, int upto) {
    int arrFrom = from >> 6;
    int arrUpto = --upto >> 6;
    from = from & 0x3f;
    upto = 63 - (upto & 0x3f);
    if (arrFrom == arrUpto) {
      return (~mask[arrFrom] >>> from << from << upto) == 0;
    } 
    if ((~mask[arrFrom++] >>> from) != 0) return false;
    if ((~mask[arrUpto--] << upto) != 0) return false;
    for (int i = arrFrom; i <= arrUpto; i++) {
      if (~mask[i] != 0) return false;
    }
    return true;
  }
  public void MarkColumnsAsFull(int from, int upto) {
    int arrFrom = from >> 6;
    int arrUpto = --upto >> 6;
    from = from & 0x3f;
    upto = 63 - (upto & 0x3f);
    if (arrFrom == arrUpto) {
      mask[arrFrom] |= ~0UL >>> from << from << upto >>> upto;
    } else {
      mask[arrFrom++] |= ~0UL >>> from << from;
      mask[arrUpto--] |= ~0UL << upto >>> upto;
      for (int i = arrFrom; i <= arrUpto; i++) mask[i] = ~0UL;
    }
  }    
  public void Reset() {
    Array.Fill(zbufferLengths, (byte)1);
    Array.Clear(mask);
    Array.Fill(clippingRanges, ((short)0, canvasHeight));
    for (short column = 0; column < viewportWidth; column++) {
      double offset = (column + viewportOffset - midline) * invDist;
      double dx = player.losDx + player.losDy * offset;
      double dy = player.losDy - player.losDx * offset;
      columnVectors[column] = (dx, dy);
    }
  }
  public void Update(InputState input) {
    noblock = input.noblock;
    fullbright = input.fullbright;
    projDist = canvasWidth / player.fov;
    invDist = player.fov / canvasWidth;
    horizon = canvasHeight * (0.5 + player.vertAngle / player.fov) - 0.5;
  }
  public byte[] GetColormap(int colormapIdx, double dist) {
    if (fullbright) return scene.colormaps[0];
    colormapIdx += Math.Min(((int)dist >> 4) - 10, 0);
    return scene.colormaps[Math.Max(colormapIdx, 0)];
  }
  void InvokeCallback(int column) {
    holdingSpaceCallback?.Invoke(pixels, column, viewportOffset, viewportWidth);
  }
  public void RenderScene() {
    counts = default;
    Reset();
    RenderNode(scene.nodes[^1]);
    floorRenderer.Flush();
    ceilingRenderer.Flush();
    RenderThingsAndFloaters();
    while (holdingSpaceCallback != null) {
      InvokeCallback(0);
    }
  }
}
