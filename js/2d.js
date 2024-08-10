import * as wad from './wad.js'
let { max, min, ceil, PI, cos, sin, abs, floor, sqrt } = Math

let canvas = document.querySelector('canvas')
canvas.width = 320
canvas.height = 200
let canvas_ctx = canvas.getContext('2d')
let canvas_image = canvas_ctx.createImageData(canvas.width, canvas.height)

let frame_buffer = new Uint32Array(canvas_image.data.buffer)

function render_frame_buffer() {
  canvas_ctx.putImageData(canvas_image, 0, 0)
}

let keys = new Set()
addEventListener('keydown', e => keys.add(e.code))
addEventListener('keyup', e => keys.delete(e.code))

let clipping_mask = new Uint32Array(10)
function clipping_mask_clear() {
  clipping_mask.fill(0)
}
function clipping_mask_set_range(from, upto) {
  let i_from = from >> 5
  let i_upto = --upto >> 5
  from = from & 0b11111
  upto = 31 - (upto & 0b11111)
  if (i_from == i_upto) {
    clipping_mask[i_from] |= -1 >>> from << from << upto >>> upto
  } else {
    clipping_mask[i_from++] |= -1 >>> from << from
    clipping_mask[i_upto--] |= -1 << upto >>> upto
    for (let i = i_from; i <= i_upto; i++) clipping_mask[i] = -1
  }
}
function clipping_mask_check_range(from, upto) {
  let i_from = from >> 5
  let i_upto = --upto >> 5
  from = from & 0b11111
  upto = 31 - (upto & 0b11111)
  if (i_from == i_upto) {
    return (~clipping_mask[i_from] >>> from << from << upto) !== 0
  } 
  if ((~clipping_mask[i_from++] >>> from) !== 0) return true
  if ((~clipping_mask[i_upto--] << upto) !== 0) return true
  for (let i = i_from; i <= i_upto; i++) {
    if (~clipping_mask[i] !== 0) return true
  }
  return false
}

function draw_line_unsafe(x1, y1, x2, y2, color) {
  color |= 0xff000000
  let steps = max(abs(x2 - x1), abs(y2 - y1))
  let dx = (x2 - x1) / steps
  let dy = (y2 - y1) / steps
  for (let i = 0; i < steps; i++) {
    frame_buffer[floor(y1) * 320 + floor(x1)] = color
    x1 += dx, y1 += dy
  }
}
function draw_line(x1, y1, x2, y2, color) {
  if (y1 > y2) {
    [x1, y1, x2, y2] = [x2, y2, x1, y1]
  }
  if (y2 < 0 || 200 <= y1) {
    return
  }  
  if (y1 < 0 && 0 <= y2) {
    x1 += (x2 - x1) * (0 - y1) / (y2 - y1)
    y1 = 0
  }
  if (y1 <= 200 && 200 < y2) {
    x2 += (x1 - x2) * (y2 - 200) / (y2 - y1)
    y2 = 200
  }
  if (x1 > x2) {
    [x1, y1, x2, y2] = [x2, y2, x1, y1]
  }
  if (x2 < 0 || 320 <= x1) {
    return
  }  
  if (x1 < 0 && 0 <= x2) {
    y1 += (y2 - y1) * (0 - x1) / (x2 - x1)
    x1 = 0
  }
  if (x1 <= 320 && 320 < x2) {
    y2 += (y1 - y2) * (x2 - 320) / (x2 - x1)
    x2 = 320
  }
  draw_line_unsafe(x1, y1, x2, y2, color)
} 
let is_holding_space = false
let current_loop_id = 0
let segments_checkbox = document.querySelector('#segments-checkbox')
let culling_checkbox = document.querySelector('#culling-checkbox')
let map_select = document.querySelector('#map-select')
map_select.disabled = false
map_select.onchange = () => {
  map_select.blur()
  clearTimeout(current_loop_id)
  init_map(map_select.value)
}
init_map(map_select.value)

function init_map(map_name) {
  let vertexes_orig = wad.read_vertexes(map_name)

  let min_world_x = vertexes_orig.reduce((a, b) => a.x < b.x ? a : b).x
  let min_world_y = vertexes_orig.reduce((a, b) => a.y < b.y ? a : b).y
  let len_world_x = vertexes_orig.reduce((a, b) => a.x > b.x ? a : b).x - min_world_x
  let len_world_y = vertexes_orig.reduce((a, b) => a.y > b.y ? a : b).y - min_world_y

  let len_canvas_x = 310
  let len_canvas_y = 190

  if (len_world_x / len_world_y > len_canvas_x / len_canvas_y) {
    len_canvas_y = len_canvas_x * len_world_y / len_world_x
  } else {
    len_canvas_x = len_canvas_y * len_world_x / len_world_y
  }
  let min_canvas_y = (200 - len_canvas_y) / 2
  let min_canvas_x = (320 - len_canvas_x) / 2

  function map_x_world_to_canvas(world_x) {
    return (world_x - min_world_x) / len_world_x * len_canvas_x + min_canvas_x
  }
  function map_y_world_to_canvas(world_y) {
    return 200 - ((world_y - min_world_y) / len_world_y * len_canvas_y + min_canvas_y)
  }
  
  let vertexes = vertexes_orig.map(vertex => ({
    ...vertex,
    canvas_x: map_x_world_to_canvas(vertex.x),
    canvas_y: map_y_world_to_canvas(vertex.y),
  }))
  let linedefs = wad.read_linedefs(map_name)
  let nodes = wad.read_nodes(map_name)
  let subsectors = wad.read_subsectors(map_name)
  let segments = wad.read_segments(map_name)
  let things = wad.read_things(map_name).map(thing => ({
    ...thing,
    canvas_x: map_x_world_to_canvas(thing.x),
    canvas_y: map_y_world_to_canvas(thing.y),
  }))
  function draw_box({ left, right, top, bottom }, color) {
    left = floor(map_x_world_to_canvas(left))
    right = floor(map_x_world_to_canvas(right))
    top = floor(map_y_world_to_canvas(top))
    bottom = floor(map_y_world_to_canvas(bottom))
    color |= 0xff000000
    let top_row = top * 320
    let bottom_row = bottom * 320
    for (let col = left; col < right; col++) {
      frame_buffer[top_row + col] = color
      frame_buffer[bottom_row + col] = color
    }
    for (let row = top_row; row < bottom_row; row += 320) {
      frame_buffer[row + left] = color
      frame_buffer[row + right] = color
    }    
  }
  function draw_map() {
    frame_buffer.fill(0xff444444)
    for (let linedef of linedefs) {
      let v1 = vertexes[linedef.start_vertex_id]
      let v2 = vertexes[linedef.end_vertex_id]
      let color = linedef.back_sidedef_id == -1 ? 0x0000ff : 0x0088cc
      draw_line_unsafe(v1.canvas_x, v1.canvas_y, v2.canvas_x, v2.canvas_y, color)
    }
    for (let thing of things) {
      let col = floor(thing.canvas_x)
      let row = floor(thing.canvas_y)
      frame_buffer[row * 320 + col] = 0xffff88ff
    }
  }
  function get_box_edges_from_players_pov({ left, right, top, bottom }) {
    let x1, y1, x2, y2
    if (player_x > right) {
      if (player_y > top) {
        x1 = right, y1 = bottom, x2 = left, y2 = top
      } else if (player_y < bottom) {
        x1 = left, y1 = bottom, x2 = right, y2 = top          
      } else {
        x1 = right, y1 = bottom ,x2 = right, y2 = top
      }
    } else if (player_x < left) {
      if (player_y > top) {
        x1 = right, y1 = top, x2 = left, y2 = bottom
      } else if (player_y < bottom) {
        x1 = left, y1 = top, x2 = right, y2 = bottom          
      } else {
        x1 = left, y1 = top, x2 = left, y2 = bottom
      }
    } else {
      if (player_y > top) {
        x1 = right, y1 = top, x2 = left, y2 = top
      } else if (player_y < bottom) {
        x1 = left, y1 = bottom, x2 = right, y2 = bottom
      } else {
        x1 = NaN, y1 = NaN, x2 = NaN, y2 = NaN
      }
    }
    return [x1, y1, x2, y2]
  }
  function get_line_segment_projection(x1, y1, x2, y2) {
    let dist1 = (x1 - player_x) * player_los_dx + (y1 - player_y) * player_los_dy
    let dist2 = (x2 - player_x) * player_los_dx + (y2 - player_y) * player_los_dy
    let from = 0, upto = 0
    if (dist1 > 0 || dist2 > 0) {
      let proj1, proj2
      if (dist1 <= 0) {
        let is_on_right_side = (player_x - x1) * (y2 - y1) - (player_y - y1) * (x2 - x1) >= 0
        proj1 = is_on_right_side ? -Infinity : Infinity
      } else {
        proj1 = (x1 - player_x) * player_los_dy - (y1 - player_y) * player_los_dx
        proj1 *= 160 / dist1
      }
      if (dist2 <= 0) {
        let is_on_right_side = (player_x - x2) * (y1 - y2) - (player_y - y2) * (x1 - x2) >= 0
        proj2 = is_on_right_side ? -Infinity : Infinity
      } else {
        proj2 = (x2 - player_x) * player_los_dy - (y2 - player_y) * player_los_dx
        proj2 *= 160 / dist2
      }
      from = min(max(ceil(proj1 + 159.5), 0), 320)
      upto = min(max(ceil(proj2 + 159.5), 0), 320)
    }
    return [from, upto]
  }
  function is_node_child_visibile({ left, right, top, bottom }) {
    let left_x, left_y, right_x, right_y
    if (player_x > right) {
      if (player_y > top) {
        left_x = right, left_y = bottom
        right_x = left, right_y = top
      } else if (player_y < bottom) {
        left_x = left, left_y = bottom
        right_x = right, right_y = top          
      } else {
        left_x = right, left_y = bottom
        right_x = right, right_y = top
      }
    } else if (player_x < left) {
      if (player_y > top) {
        left_x = right, left_y = top
        right_x = left, right_y = bottom
      } else if (player_y < bottom) {
        left_x = left, left_y = top
        right_x = right, right_y = bottom          
      } else {
        left_x = left, left_y = top
        right_x = left, right_y = bottom
      }
    } else {
      if (player_y > top) {
        left_x = right, left_y = top
        right_x = left, right_y = top
      } else if (player_y < bottom) {
        left_x = left, left_y = bottom
        right_x = right, right_y = bottom
      } else {
        return true
      }
    }
    let [from, upto] = get_line_segment_projection(left_x, left_y, right_x, right_y)
    return from < upto && clipping_mask_check_range(from, upto)
  }  
  let player_x = things[0].x
  let player_y = things[0].y
  let player_ang = things[0].angle / 180 * PI
  let player_los_dx = 1
  let player_los_dy = 0
  
  function* animate_bsp_search() {
    let node = nodes[nodes.length - 1]
    let splitters = []
    while (true) {
      let split_len = sqrt(node.dx ** 2 + node.dy ** 2)
      let split_x = node.x
      let split_y = node.y
      let split_dx = node.dx / split_len
      let split_dy = node.dy / split_len
      
      let line_x1 = split_x - split_dx * 10000
      let line_y1 = split_y - split_dy * 10000
      let line_x2 = split_x + split_dx * 10000
      let line_y2 = split_y + split_dy * 10000

      for (let { split_x, split_y, split_dx, split_dy, on_right_side } of splitters) {
        let dist1 = (line_x1 - split_x) * split_dy - (line_y1 - split_y) * split_dx
        let dist2 = (line_x2 - split_x) * split_dy - (line_y2 - split_y) * split_dx
        if (dist1 <= 0 && dist2 <= 0 || dist1 >= 0 && dist2 >= 0) continue
        let adjust_p1 = dist1 < 0 == on_right_side
        dist1 = abs(dist1)
        dist2 = abs(dist2)
        let proj1 = (line_x1 - split_x) * split_dx + (line_y1 - split_y) * split_dy
        let proj2 = (line_x2 - split_x) * split_dx + (line_y2 - split_y) * split_dy
        let len = (proj1 * dist2 + proj2 * dist1) / (dist2 + dist1)
        let new_x = split_x + len * split_dx 
        let new_y = split_y + len * split_dy
        if (adjust_p1) {
          line_x1 = new_x
          line_y1 = new_y
        } else {
          line_x2 = new_x
          line_y2 = new_y
        }        
      }
      line_x1 = map_x_world_to_canvas(line_x1)
      line_y1 = map_y_world_to_canvas(line_y1)
      line_x2 = map_x_world_to_canvas(line_x2)
      line_y2 = map_y_world_to_canvas(line_y2)
      draw_line(line_x1, line_y1, line_x2, line_y2, 0xff88ff)
      if (is_holding_space) yield 300

      let on_right_side = (player_x - split_x) * split_dy - (player_y - split_y) * split_dx >= 0
      let child = on_right_side ? node.right : node.left
      if (!child.is_node) break
      splitters.push({ split_x, split_y, split_dx, split_dy, on_right_side })
      node = nodes[child.child_id]
    }
  }
  function* main_loop() {
    if (keys.has('ArrowUp') || keys.has('Numpad8')) {
      player_x += player_los_dx * 25
      player_y += player_los_dy * 25
    } else if (keys.has('ArrowDown') || keys.has('Numpad2')) {
      player_x -= player_los_dx * 25
      player_y -= player_los_dy * 25
    }
    if (keys.has('ArrowRight') || keys.has('Numpad6')) {
      if (keys.has('AltLeft')) {
        player_x += player_los_dy * 20
        player_y -= player_los_dx * 20
      } else {
        player_ang -= 0.1
        if (player_ang < 0) player_ang += 2 * PI
      }
    } else if (keys.has('ArrowLeft') || keys.has('Numpad4')) {
      if (keys.has('AltLeft')) {
        player_x -= player_los_dy * 20
        player_y += player_los_dx * 20
      } else {
        player_ang += 0.1
        if (player_ang >= 2 * PI) player_ang -= 2 * PI
      }
    }
    player_los_dx = cos(player_ang)
    player_los_dy = sin(player_ang)
    let draw_segments = segments_checkbox.checked
    let use_clipping_mask = culling_checkbox.checked
    function* draw_node(node_id, is_node) {
      if (is_node) {
        let node = nodes[node_id]
        let on_right_side = (player_x - node.x) * node.dy - (player_y - node.y) * node.dx >= 0
        let front_child = on_right_side ? node.right : node.left
        let back_child = on_right_side ? node.left : node.right
        if (is_node_child_visibile(front_child)) {
          if (!draw_segments) {
            draw_box(front_child, 0x00ff00)
            if (is_holding_space) yield 50
          }
          yield* draw_node(front_child.child_id, front_child.is_node)
        }
        if (is_node_child_visibile(back_child)) {
          if (!draw_segments) {
            draw_box(back_child, 0x00ff00)
            if (is_holding_space) yield 50
          }
          yield* draw_node(back_child.child_id, back_child.is_node)
        }
      } else if (use_clipping_mask || draw_segments) {
        let subsector = subsectors[node_id]
        for (let i = subsector.first_seg_id; i <= subsector.last_seg_id; i++) {
          let segment = segments[i]
          let start = vertexes[segment.start_vertex_id]
          let end = vertexes[segment.end_vertex_id]
          if (use_clipping_mask) {
            let [from, upto] = get_line_segment_projection(start.x, start.y, end.x, end.y)
            if (from >= upto || !clipping_mask_check_range(from, upto)) continue
            let is_solid_wall = linedefs[segment.linedef_id].back_sidedef_id == -1
            if (is_solid_wall) clipping_mask_set_range(from, upto)
          }
          if (draw_segments) {
            draw_line_unsafe(start.canvas_x, start.canvas_y, end.canvas_x, end.canvas_y, 0x00ff00)
            if (is_holding_space) yield 50
          }
        }
      }
    }
    draw_map()
    for (let delta of [-PI / 4, PI / 4]) {
      let x1 = map_x_world_to_canvas(player_x)
      let y1 = map_y_world_to_canvas(player_y)
      let x2 = map_x_world_to_canvas(player_x + 500 * cos(player_ang + delta))
      let y2 = map_y_world_to_canvas(player_y + 500 * sin(player_ang + delta))
      draw_line(x1, y1, x2, y2, 0x00ffff)
    }  
    clipping_mask_clear()
    if (is_holding_space) yield* animate_bsp_search()
    yield* draw_node(nodes.length - 1, true)
    while (is_holding_space) yield 50
  }
  let rator = main_loop()
  current_loop_id = setTimeout(function tick() {
    is_holding_space = keys.has('Space')
    let { done, value } = rator.next()
    if (done) rator = main_loop()
    render_frame_buffer()
    current_loop_id = setTimeout(tick, value ?? 25)
  }, 25)
}
