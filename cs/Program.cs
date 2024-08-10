global using System;
global using System.Collections.Generic;
global using System.Linq;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Raylib_cs;

int doomVersion = 0;
if (args.Length > 0) {
  doomVersion = int.Parse(args[0]);
}
var file = File.OpenRead(Path.Join(AppContext.BaseDirectory, doomVersion switch {
  1 => "doom.wad",
  2 => "doom2.wad",
  _ => "doom1.wad",
}));

var wad = new WadReader(file);
foreach (string arg in args.Skip(1)) {
  var pwadFile = File.OpenRead(Path.Join(AppContext.BaseDirectory, arg));
  wad = new WadReader(pwadFile, wad);
}

string mapName = doomVersion == 2 ? "MAP01" : "E1M1";
string skyTexture = "SKY1";
var scene = new Scene(wad, mapName, skyTexture);

var normalPalette = wad.ReadPalette(0);
var overbrightPalette = normalPalette.AsSpan().ToArray();
for (int i = 0; i < overbrightPalette.Length; i++) {
  uint color = overbrightPalette[i];
  uint r = color & 0xff;
  uint g = color >> 8 & 0xff;
  uint b = color >> 16 & 0xff;
  r = (byte)Math.Min(0xff, (r + 30) * 1.5);
  g = (byte)Math.Min(0xff, (g + 30) * 1.5);
  b = (byte)Math.Min(0xff, (b + 30) * 1.5);
  overbrightPalette[i] =  0xff000000 | b << 16 | g << 8 | r;
}
var palette = overbrightPalette;

var input = new InputState();
var player = new Player(scene);
var renderers = new Renderers(scene, player, 320, 200, 1);

Raylib.InitWindow(renderers.width * 2, renderers.height * 2, "Fake Doom");
Raylib.SetWindowState(ConfigFlags.ResizableWindow | ConfigFlags.MaximizedWindow);
Raylib.SetTargetFPS(35);

var canvas = Raylib.LoadRenderTexture(renderers.width, renderers.height);
var pixels = new uint[renderers.width * renderers.height];
var (position, scale) = getPositionAndScale();
(Vector2, float) getPositionAndScale() {
  float windowWidth = Raylib.GetRenderWidth();
  float windowHeight = Raylib.GetRenderHeight();
  float canvasWidth = canvas.Texture.Width;
  float canvasHeight = canvas.Texture.Height;
  if (windowWidth / windowHeight > canvasWidth / canvasHeight) {
    float scale = windowHeight / canvasHeight;
    float offset = (windowWidth - scale * canvasWidth) / 2;
    return (new Vector2(offset, 0), scale);
  } else {
    float scale = windowWidth / canvasWidth;
    float offset = (windowHeight - scale * canvasHeight) / 2;
    return (new Vector2(0, offset), scale);
  }
}

bool showMap = false;
string newMapName = "";
string displayText = "";
var pipe1 = new BlockingCollection<object>();
var pipe2 = new BlockingCollection<object>();
var stopwatch = new Stopwatch();

while (!Raylib.WindowShouldClose()) {
  if (Raylib.IsKeyPressed(KeyboardKey.O)) {
    palette = palette == normalPalette ? overbrightPalette : normalPalette;
  }
  if (Raylib.IsKeyPressed(KeyboardKey.Tab)) {
    showMap = !showMap;
  }
  if (Raylib.IsKeyPressed(KeyboardKey.D)) {
    input.doorsOpen = !input.doorsOpen;
  }
  if (Raylib.IsKeyPressed(KeyboardKey.L)) {
    input.fullbright = !input.fullbright;
  }
  if (Raylib.IsKeyPressed(KeyboardKey.B)) {
    input.noblock = !input.noblock;
  }
  input.incResolution = Raylib.IsKeyPressed(KeyboardKey.PageUp);
  input.decResolution = Raylib.IsKeyPressed(KeyboardKey.PageDown);
  input.incThreads = Raylib.IsKeyPressed(KeyboardKey.Home);
  input.decThreads = Raylib.IsKeyPressed(KeyboardKey.End);
  input.forward = Raylib.IsKeyDown(KeyboardKey.Up) || Raylib.IsKeyDown(KeyboardKey.Kp8);
  input.back = Raylib.IsKeyDown(KeyboardKey.Down) || Raylib.IsKeyDown(KeyboardKey.Kp2);
  input.left = Raylib.IsKeyDown(KeyboardKey.Left) || Raylib.IsKeyDown(KeyboardKey.Kp4);
  input.right = Raylib.IsKeyDown(KeyboardKey.Right) || Raylib.IsKeyDown(KeyboardKey.Kp6);
  input.up = Raylib.IsKeyDown(KeyboardKey.Q);
  input.down = Raylib.IsKeyDown(KeyboardKey.A);
  input.zoomIn = Raylib.IsKeyDown(KeyboardKey.Z);
  input.zoomOut = Raylib.IsKeyDown(KeyboardKey.X);
  input.creep = Raylib.IsKeyDown(KeyboardKey.LeftShift);
  input.strafe = Raylib.IsKeyDown(KeyboardKey.LeftAlt);
  if (Raylib.IsKeyPressed(KeyboardKey.Space) && renderers.HoldingSpaceCallback == null) {
    Raylib.SetTargetFPS(renderers.width / 4);
    renderers.FillPixels(120);
    Array.Fill(pixels, palette[120]);
    renderers.HoldingSpaceCallback = drawSceneCallback;
  }
  while (true) {
    int key = Raylib.GetKeyPressed();
    if (key == 0) break;
    int keyNum = key - (int)KeyboardKey.Zero;
    if (keyNum < 0 || keyNum > 9) continue;
    if (doomVersion == 2) {
      if (newMapName == "") {
        newMapName = "MAP" + keyNum;
      } else {
        newMapName += keyNum;
      }
    } else if (doomVersion == 1) {
      if (newMapName == "") {
        newMapName = "E" + keyNum;
      } else {
        newMapName += "M" + keyNum;
      }      
    } else {
      newMapName = "E1M" + keyNum;
    }
  }
  if (newMapName.Length < mapName.Length) {
    scene.Update(input);
    player.Update(input);
    renderers.Update(input);
  } else if (newMapName == mapName || !wad.IsValidMap(newMapName)) {
    newMapName = "";
    scene.Update(input);
    player.Update(input);
    renderers.Update(input);
  } else {
    mapName = newMapName;
    newMapName = "";
    if (doomVersion == 2) {
      int map = int.Parse(mapName[3..]);
      skyTexture = map <= 11 ? "SKY1" : map <= 20 ? "SKY2" : "SKY3";
    } else if (doomVersion == 1) {
      skyTexture = "SKY" + mapName[1];
    }
    scene = new Scene(wad, mapName, skyTexture);
    player = new Player(scene);
    renderers = new Renderers(scene, player, renderers.width, renderers.height, renderers.threads);
  }
  if (renderers.width != canvas.Texture.Width) {
    Raylib.UnloadRenderTexture(canvas);
    canvas = Raylib.LoadRenderTexture(renderers.width, renderers.height);
    pixels = new uint[renderers.width * renderers.height];
    (position, scale) = getPositionAndScale();
  } else if (Raylib.IsWindowResized()) {
    (position, scale) = getPositionAndScale();
  }
  if (showMap) {
    renderers.RenderMap().CopyTo(pixels);
  } else {
    stopwatch.Start();
    var task = Task.Run(() => Parallel.For(1, renderers.threads, renderViewport));
    renderViewport(0);
    task.Wait();   
    stopwatch.Stop();
  }
  void renderViewport(int i) {
    var (data, viewportOffset, viewportWidth) = renderers.RenderScene(i);
    int dataIdx = 0;
    var data2 = MemoryMarshal.Cast<byte, ushort>(data.Span);
    var pixels2 = MemoryMarshal.Cast<uint, ulong>(pixels);
    for (int row = 0; row < renderers.height; row++) {
      int from = row * (renderers.width >> 1) + (viewportOffset >> 1);
      int upto = from + (viewportWidth >> 1);
      for (int idx = from; idx < upto; idx++) {
        ushort pair = data2[dataIdx++];
        pixels2[idx] = palette[pair & 0xff] | (ulong)palette[pair >> 8 & 0xff] << 32;
      }
    }
  }
  if (scene.frameCount % 10 == 0) {
    displayText = $"""
      {(newMapName.Length == 0 ? mapName : newMapName)}

      {renderers.width} x {renderers.height}

      Time: {100.0 * stopwatch.ElapsedTicks / Stopwatch.Frequency:N}

      Threads: {renderers.threads}

      Fov: {player.fov:N}


      Nodes
      
      {renderers.NodeCount}/{scene.nodes.Length + scene.subsectors.Length}

      
      Segments
      
      {renderers.SegmentCount}/{scene.segments.Length}
      

      Things
      
      {renderers.ThingCount}/{scene.things.Length}
      
      
      Sector: {player.sectorIdx}

      x: {player.x:N}
      
      y: {player.y:N}

      angle: {player.angle}
      
      {(input.noblock ? "blocks off" : "")}
      """.Replace("\r", "");
    stopwatch.Reset();
  }  
  drawPixels();
}
Raylib.UnloadRenderTexture(canvas);
Raylib.CloseWindow();

void drawSceneCallback(
  ReadOnlySpan<byte> data, int column, 
  int viewportOffset, int viewportWidth
) {
  if (column >= 0) {
    int dataIdx = column;
    int from = column + viewportOffset;
    for (int idx = from; idx < pixels.Length; idx += renderers.width) {
      pixels[idx] = palette[data[dataIdx]];
      dataIdx += viewportWidth;
    }
  } else {
    int dataIdx = ~column * viewportWidth;
    int from = ~column * renderers.width + viewportOffset;
    int upto = from + viewportWidth;
    for (int idx = from; idx < upto; idx++) {
      pixels[idx] = palette[data[dataIdx++]];
    }
  }
  if (viewportOffset != 0) {
    pipe1.Add(null);
    pipe2.Take();
    return;
  }
  for (int i = 1; i < renderers.threads; i++) pipe1.Take();
  drawPixels();
  if (!Raylib.IsKeyDown(KeyboardKey.Space)) {
    Raylib.SetTargetFPS(35);
    renderers.HoldingSpaceCallback = null;
  }
  for (int i = 1; i < renderers.threads; i++) pipe2.Add(null);
};

void drawPixels() {
  Raylib.UpdateTexture(canvas.Texture, pixels);
  Raylib.BeginDrawing();
  Raylib.ClearBackground(Color.DarkGray);
  Raylib.DrawTextureEx(canvas.Texture, position, 0, scale, Color.White);
  Raylib.DrawText(displayText, 3, 3, 28, Color.White);
  Raylib.EndDrawing();
}
class InputState {
  public bool forward, back, left, right, up, down, 
    zoomIn, zoomOut, strafe, creep, doorsOpen, fullbright, noblock, 
    incResolution, decResolution, incThreads, decThreads;
}
