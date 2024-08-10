using System;

partial class Renderer {
  void RenderThingsAndFloaters() {
    counts.things = thingsToRender.Count;
    floatersToRender.Sort();
    thingsToRender.Sort();
    while (true) {
      var (thingDist, thingIdx) = thingsToRender.Count > 0 ? thingsToRender[^1] : default;
      var (floaterDist, floaterIdx) = floatersToRender.Count > 0 ? floatersToRender[^1] : default;
      if (thingDist == 0 && floaterDist == 0) break;
      if (thingDist > floaterDist) {
        thingsToRender.RemoveAt(thingsToRender.Count - 1);
        DrawThing(thingIdx);
      } else {
        var renderer = floaterRenderers[floaterIdx];
        int targetDist = Math.Max(thingDist, renderer.minDist);
        int idx = floatersToRender.Count - 2;
        for (; idx >= 0; idx--) {
          var (closerDist, closerIdx) = floatersToRender[idx];
          if (closerDist <= targetDist) break;
          if (closerIdx < floaterIdx) continue;
          targetDist = closerDist;
          break;
        }          
        int newFloaterDist = (int)renderer.RenderUntil(targetDist);
        if (newFloaterDist > 1) {
          while (idx >= 0 && floatersToRender[idx].dist > newFloaterDist) idx--;
          idx++;
          int i = floatersToRender.Count - 1;
          while (i > idx) floatersToRender[i] = floatersToRender[--i];
          floatersToRender[idx] = (newFloaterDist, floaterIdx);
        } else {
          floatersToRender.RemoveAt(floatersToRender.Count - 1);
          renderer.Dispose();
        }
      }
    }
    floaterRenderers.Clear();
  }
  void AppendThingsToRenderList(int thingIdx) {
    while (thingIdx != -1) {
      ref var thing = ref scene.things[thingIdx];
      double spriteCanvasLeft, spriteCanvasRight;
      double dist;
      if (thing.cached.frameCount == scene.frameCount) {
        spriteCanvasLeft = thing.cached.canvasLeft;
        spriteCanvasRight = thing.cached.canvasRight;
        dist = thing.cached.dist;
      } else {
        dist = (thing.x - player.x) * player.losDx + (thing.y - player.y) * player.losDy;
        if (dist < 1) {
          thingIdx = thing.nextThingIdx;
          continue;
        }
        double proj = (thing.x - player.x) * player.losDy - (thing.y - player.y) * player.losDx;
        double scale = projDist / dist;
        int spriteAngle = 0;
        var thingInfo = ThingType.Data[thing.type];
        if (thingInfo.multiangle) {
          int angle = thing.angle - 22;
          if (angle < 0) angle += 360;
          double thingDx = Scene.Sins[angle + 90];
          double thingDy = Scene.Sins[angle];
          double thingProj = (player.x - thing.x) * thingDx + (player.y - thing.y) * thingDy;
          double thingDist = (player.x - thing.x) * thingDy - (player.y - thing.y) * thingDx;
          bool quar = Math.Abs(thingProj) >= Math.Abs(thingDist);
          spriteAngle = thingProj >= 0 ? (quar ? 0 : 1) : (quar ? 3 : 2);
          if (thingDist >= 0) spriteAngle = 7 - spriteAngle;
        }
        int frameIdx = (scene.frameCount + thingIdx & 0x1f) * thingInfo.sprites.Length >> 5;
        var (spriteName, mirror) = thingInfo.sprites[frameIdx][spriteAngle];
        var sprite = scene.sprites[spriteName];
        spriteCanvasLeft = (proj - sprite.leftOffset) * scale + midline;
        spriteCanvasRight = spriteCanvasLeft + sprite.width * scale;

        thing.cached = (
          scene.frameCount, mirror, thingInfo.transparent, thingInfo.hanging, 
          dist, spriteName, spriteCanvasLeft, spriteCanvasRight, scale
        );
      }
      if (viewportOffset < spriteCanvasRight && spriteCanvasLeft <= viewportEnd - 1) {
        thingsToRender.Add(((int)dist, thingIdx));
      }
      thingIdx = thing.nextThingIdx;
    }
  }  

  void DrawThing(int thingIdx) {
    ref var thing = ref scene.things[thingIdx];
    var (_, mirror, transparent, hanging, dist, spriteName, canvasLeft, canvasRight, scale) = thing.cached;
    
    var sprite = scene.sprites[spriteName];
    ref var sector = ref scene.sectors[thing.sectorIdx];
    double spriteWorldBottom = hanging ? sector.ceilingHeight - sprite.height : sector.floorHeight;
    double spriteWorldTop = spriteWorldBottom + sprite.height;
    double spriteCanvasTop = horizon - (spriteWorldTop - player.height) * scale;
    double spriteCanvasBottom = horizon - (spriteWorldBottom - player.height) * scale;
    if (spriteCanvasBottom <= 0 || spriteCanvasTop > canvasHeight - 1) return;
    short spriteTopRow = (short)Math.Ceiling(spriteCanvasTop);

    canvasLeft -= viewportOffset;
    canvasRight -= viewportOffset;
    short columnFrom = (short)Math.Clamp(Math.Ceiling(canvasLeft), 0, viewportWidth);
    short columnUpto = (short)Math.Clamp(Math.Ceiling(canvasRight), 0, viewportWidth);

    var colormap = transparent ? scene.spectreColormap : GetColormap(sector.colormapIndex, dist);
    double colCanvasToSprite = sprite.width / (canvasRight - canvasLeft);
    double rowSpriteToCanvas = (spriteCanvasBottom - spriteCanvasTop) / sprite.height;
    double rowCanvasToSprite = 1 / rowSpriteToCanvas;
    for (short column = columnFrom; column < columnUpto; column++) {
      var (rangeFrom, rangeUpto) = ZBufferPopUntil(column, dist);
      if (spriteTopRow >= rangeUpto) continue;
      int spriteCol = (short)((column - canvasLeft) * colCanvasToSprite);
      if (mirror) spriteCol = sprite.width - 1 - spriteCol;
      bool rendereredSomething = false;
      foreach (var post in sprite.EnumeratePosts(spriteCol)) {
        double postBeg = post.from * rowSpriteToCanvas + spriteCanvasTop;
        double postEnd = post.upto * rowSpriteToCanvas + spriteCanvasTop;
        short postFrom = (short)Math.Clamp(Math.Ceiling(postBeg), rangeFrom, rangeUpto);
        short postUpto = (short)Math.Clamp(Math.Ceiling(postEnd), rangeFrom, rangeUpto);
        if (postFrom >= postUpto) continue;
        rendereredSomething = true;
        int idxFrom = postFrom * viewportWidth + column;
        int idxUpto = postUpto * viewportWidth;
        var postPixels = post.pixels.Span;
        double postIdx = (postFrom - spriteCanvasTop) * rowCanvasToSprite - post.from + 1;
        if (transparent) {
          for (var idx = idxFrom; idx < idxUpto; idx += viewportWidth) {
            int colormapIdx = ((postPixels[(int)postIdx] + scene.frameCount) & 0b11000) << 5;
            pixels[idx] = colormap[pixels[idx] | colormapIdx];
            postIdx += rowCanvasToSprite;
          }
        } else {
          for (var idx = idxFrom; idx < idxUpto; idx += viewportWidth) {
            var color = colormap[postPixels[(int)postIdx]];
            pixels[idx] = color;
            postIdx += rowCanvasToSprite;
          }
        }
      }
      if (rendereredSomething) InvokeCallback(column);
    }
  }
}
