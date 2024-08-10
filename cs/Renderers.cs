class Renderers {
  Scene scene;
  Player player;
  public short width, height, threads;
  Renderer[] renderers;
  MapRenderer mapRenderer;

  public Renderer.HoldingSpaceCallback HoldingSpaceCallback {
    get => renderers[0].holdingSpaceCallback;
    set {
      foreach (var renderer in renderers) {
        renderer.holdingSpaceCallback = value;
      }
    }
  }
  public int NodeCount => renderers.Sum(r => r.counts.nodes);
  public int ThingCount => renderers.Sum(r => r.counts.things);
  public int SegmentCount => renderers.Sum(r => r.counts.segments);
  
  public Renderers(Scene scene, Player player, short width, short height, short threads) {
    this.scene = scene;
    this.player = player;
    this.width = width;
    this.height = height;
    this.threads = threads;
    CreateRenderers();
  }
  void CreateRenderers() {
    mapRenderer = new MapRenderer(scene, player, width, height);
    renderers = new Renderer[threads];
    short viewportSize = (short)(width / threads);
    short viewportOffset = 0;
    for (int i = 0; i < threads; i++) {
      renderers[i] = new Renderer(scene, player, width, height, viewportOffset, viewportSize);
      viewportOffset += viewportSize;
    }
  }
  public void FillPixels(byte color) {
    foreach (var renderer in renderers) {
      Array.Fill(renderer.pixels, color);
    }
  }
  public void Update(InputState input) {
    if (input.incResolution && width < 1280) {
      width *= 2;
      height *= 2;
    }
    if (input.decResolution && width > 40 && 0.5 * width / threads % 2 == 0) {
      width /= 2;
      height /= 2;
    }
    if (input.decThreads && threads > 1) {
      threads /= 2;
    }
    if (input.incThreads && threads < 16 && 0.5 * width / threads % 2 == 0) {
      threads *= 2;
    }
    if (threads != renderers.Length || width != renderers[0].canvasWidth) {
      CreateRenderers();
    }
    foreach (var renderer in renderers) {
      renderer.Update(input);
    }
  }
  public (ReadOnlyMemory<byte> pixels, short offset, short width) RenderScene(int i) {
    renderers[i].RenderScene();
    var memory = renderers[i].pixels.AsMemory();
    var offset = renderers[i].viewportOffset;
    var width = renderers[i].viewportWidth;
    return (memory, offset, width);
  }
  public ReadOnlySpan<uint> RenderMap() {
    mapRenderer.RenderMap();
    return mapRenderer.pixels.AsSpan();
  }
}
