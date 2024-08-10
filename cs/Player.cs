class Player {
  Scene scene;
  public double x, y, height, losDx, losDy, vertAngle, fov;
  public int angle, sectorIdx;
  public Player(Scene scene) {
    this.scene = scene;
    var thing = Array.FindLast(scene.things, thing => thing.type == 1);
    x = thing.x;
    y = thing.y;
    angle = thing.angle;
    fov = 2;
  }
  public void Update(InputState input) {
    double dx = 0, dy = 0;
    if (input.forward) {
      dx += losDx;
      dy += losDy;
    } else if (input.back) {
      dx -= losDx;
      dy -= losDy;
    }
    if (input.right) {
      if (input.strafe) {
        dx += losDy;
        dy -= losDx;
      } else {
        angle -= input.creep ? 1 : 7;
        if (angle < 0) angle += 360;
      }
    } else if (input.left) {
      if (input.strafe) {
        dx -= losDy;
        dy += losDx;
      } else {
        angle += input.creep ? 1 : 7;
        if (angle >= 360) angle -= 360;
      }
    }
    if (dx != 0 || dy != 0) {
      double len = Math.Sqrt(dx * dx + dy * dy);
      double speed = input.creep ? 2 : 15;
      x += dx / len * speed;
      y += dy / len * speed;
    }
    if (input.up) {
      vertAngle += input.creep ? 0.01 : 0.07;
      if (vertAngle > 1) vertAngle = 1;
    }
    if (input.down) {
      vertAngle -= input.creep ? 0.01 : 0.07;
      if (vertAngle < -1) vertAngle = -1;
    }
    if (input.zoomIn) {
      fov /= input.creep ? 1.01 : 1.06;
      if (fov < 0.1) fov = 0.1;
    }
    if (input.zoomOut) {
      fov *= input.creep ? 1.01 : 1.06;
      if (fov > 10) fov = 10;
    }

    losDx = Scene.Sins[angle + 90];
    losDy = Scene.Sins[angle];
    sectorIdx = scene.FindSectorIdx(x, y);
    ref var sector = ref scene.sectors[sectorIdx];
    height = Math.Min(sector.floorHeight + 41, sector.ceilingHeight - 1);
  }
}
