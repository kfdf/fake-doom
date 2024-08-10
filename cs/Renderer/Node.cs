partial class Renderer {
  record struct EdgesProj(short col1, short col2, double dist1, double dist2);
  EdgesProj ProjectLineSegmentEdges(double x1, double y1, double x2, double y2) {
    double dist1 = (x1 - player.x) * player.losDx + (y1 - player.y) * player.losDy;
    double dist2 = (x2 - player.x) * player.losDx + (y2 - player.y) * player.losDy;
    if (dist1 <= 0 && dist2 <= 0) return default;
    double proj1, proj2;
    if (dist1 <= 0) {
      double side = (player.x - x1) * (y2 - y1) - (player.y - y1) * (x2 - x1);
      if (side == 0) return default;
      proj1 = side > 0 ? double.NegativeInfinity : double.PositiveInfinity;
    } else {
      proj1 = (x1 - player.x) * player.losDy - (y1 - player.y) * player.losDx;
      proj1 *= projDist / dist1;
    }
    if (dist2 <= 0) {
      double side = (player.x - x2) * (y1 - y2) - (player.y - y2) * (x1 - x2);
      if (side == 0) return default;
      proj2 = side > 0 ? double.NegativeInfinity : double.PositiveInfinity;
    } else {
      proj2 = (x2 - player.x) * player.losDy - (y2 - player.y) * player.losDx;
      proj2 *= projDist / dist2;
    }
    short col1 = (short)Math.Clamp(Math.Ceiling(proj1 - viewportOffset + midline), 0, viewportWidth);
    short col2 = (short)Math.Clamp(Math.Ceiling(proj2 - viewportOffset + midline), 0, viewportWidth);
    return new EdgesProj(col1, col2, dist1, dist2);
  }

  bool IsNodeChildVisible(in Node.ChildBox box) {
    (double x, double y) left, right;
    if (player.x > box.right) {
      if (player.y > box.top) {
        left = (box.right,  box.bottom);
        right = (box.left, box.top);
      } else if (player.y < box.bottom) {
        left = (box.left, box.bottom);
        right = (box.right, box.top);
      } else {
        left = (box.right, box.bottom);
        right = (box.right, box.top);
      }
    } else if (player.x < box.left) {
      if (player.y > box.top) {
        left = (box.right, box.top);
        right = (box.left, box.bottom);
      } else if (player.y < box.bottom) {
        left = (box.left, box.top);
        right = (box.right, box.bottom);
      } else {
        left = (box.left, box.top);
        right = (box.left, box.bottom);
      }
    } else {
      if (player.y > box.top) {
        left = (box.right, box.top);
        right = (box.left, box.top);
      } else if (player.y < box.bottom) {
        left = (box.left, box.bottom);
        right = (box.right, box.bottom);
      } else {
        return true;
      }
    }
    var proj = ProjectLineSegmentEdges(left.x, left.y, right.x, right.y);
    return proj.col1 != proj.col2 && !AreColumnsFull(proj.col1, proj.col2);
  }
  void RenderChildIfVisible(in Node.ChildBox box, in Node.ChildRef child) {
    if (!IsNodeChildVisible(box)) return;
    if (child.isNode) {
      RenderNode(scene.nodes[child.childIdx]);
    } else {
      counts.nodes++;
      ref var subsector = ref scene.subsectors[child.childIdx];
      AppendThingsToRenderList(subsector.firstThingIdx);

      int segmentIdx = subsector.firstSegIdx;
      for (int i = 0; i < subsector.segmentCount; i++) {
        ref var segment = ref scene.segments[segmentIdx++];
        ref var start = ref scene.vertexes[segment.startVertexIdx];
        ref var end = ref scene.vertexes[segment.endVertexIdx];
        var proj = ProjectLineSegmentEdges(start.x, start.y, end.x, end.y);
        if (proj.col1 >= proj.col2 || AreColumnsFull(proj.col1, proj.col2)) continue;
        if (segment.backSectorIdx == -1) {
          RenderWall(segment, proj);
        } else {
          if (scene.sidedefs[segment.frontSidedefIdx].middleTexture != Scene.NO_TEXTURE) {
            var renderer = acquireFloaterRenderer(segment, proj);
            double dist = renderer.MoveToNextColumn();
            if (dist == -1) {
              renderer.Dispose();
            } else {
              floatersToRender.Add(((int)dist, floaterRenderers.Count));
              floaterRenderers.Add(renderer);
            }
          }
          RenderPortal(segment, proj);
        }
      }
    }
  }
  void RenderNode(in Node node) {
    counts.nodes++;
    AppendThingsToRenderList(node.firstThingIdx);
    bool onRightSide = (player.x - node.x) * node.dy - (player.y - node.y) * node.dx >= 0;
    if (onRightSide) {
      RenderChildIfVisible(node.rightBox, node.rightRef);
      RenderChildIfVisible(node.leftBox, node.leftRef);
    } else {
      RenderChildIfVisible(node.leftBox, node.leftRef);
      RenderChildIfVisible(node.rightBox, node.rightRef);
    }
  }
}
