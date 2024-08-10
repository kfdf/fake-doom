partial class Renderer {
  Stack<BlockRenderer> blockPool = [];
  BlockRenderer acquireBlockRenderer(byte[] texture, double height, int colormapIdx) {
    var ret = blockPool.Count == 0 ? new BlockRenderer(this) : blockPool.Pop();
    ret.init(texture, height, colormapIdx);
    return ret;
  }
  class BlockRenderer(Renderer renderer) : IDisposable {
    record struct Block(short top, short bottom, short left, short right, short prev, short next);
    Block[] blocks = new Block[4];
    int blockCount, colormapIdx;
    byte[] texture;
    double height;
    public void init(byte[] texture, double height, int colormapIdx) {
      this.blocks[0] = new(0, 0, -1, 0, 0, 0);
      this.blockCount = 1;
      this.texture = texture;
      this.colormapIdx = colormapIdx;
      this.height = height;
    }
    public void Dispose() {
      texture = null;
      renderer.blockPool.Push(this);
    }
    public void AddBlock(short top, short bottom, short left, short right) {
      if (blockCount >= blocks.Length) {
        Array.Resize(ref blocks, blocks.Length * 2);
      }
      blocks[blockCount++] = new(top, bottom, left, right, 0, 0);
    }

    public void RenderAll() {
      blocks.AsSpan(..blockCount).Sort((a, b) => {
        var ret = a.left - b.left;
        return ret != 0 ? ret : a.top - b.top;
      });
      for (int i = 1; i < blockCount; i++) {
        blocks[i].prev = (short)(i - 1);
        blocks[i - 1].next = (short)i;
      }
      blocks[0].prev = (short)(blockCount - 1);
      while (true) {
        var index = blocks[0].next;
        if (index == 0) break;
        var b = blocks[index];
        blocks[0].next = b.next;
        blocks[b.next].prev = 0;
        RenderBlock(b.next, b.top, b.bottom, b.left, b.right);
      }
      blockCount = 1;
    }
    void DrawRect(short top, short bottom, short left, short right) {
      for (short row = top; row < bottom; row++) {
        renderer.DrawFlatRow(row, left, right, texture, colormapIdx, height);
      }
    }
    void RenderBlock(short index, short top, short bottom, short left, short right) {
      while (index != 0) {
        var b = blocks[index];
        if (right < b.left) break;
        if (bottom <= b.top) {
          if (right == b.left) break;
          index = b.next;
          continue;
        }
        if (b.bottom <= top) {
          blocks[b.prev].next = b.next;
          blocks[b.next].prev = b.prev;
          RenderBlock(b.next, b.top, b.bottom, b.left, b.right);
          index = blocks[b.prev].next;
          continue;
        }
        if (top < b.top) {
          DrawRect(top, b.top, left, right);
          top = b.top;
        } else if (b.top < top) {
          RenderBlock(b.next, b.top, top, b.left, b.right);
          b.top = top;
        }
        if (bottom < b.bottom) {
          right = b.right;
          blocks[index].top = bottom;
          index = b.next;
        } else if (b.bottom < bottom) {
          top = b.bottom;
          blocks[b.prev].next = b.next;
          blocks[b.next].prev = b.prev;
          RenderBlock(b.next, b.top, b.bottom, left, b.right);
          index = blocks[b.prev].next;
        } else {
          right = b.right;
          blocks[b.prev].next = b.next;
          blocks[b.next].prev = b.prev;
          index = b.next;
        }
      }
      DrawRect(top, bottom, left, right);
    }
  }  
}
