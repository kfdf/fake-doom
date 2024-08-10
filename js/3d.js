import * as wad from './wad.js'
let { max, min, floor, ceil, sqrt, abs } = Math

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

let sins = new Float64Array(360 + 90)
for (let ang = 0; ang < sins.length; ang++) {
  sins[ang] = Math.sin(ang / 180 * Math.PI)
}

let clipping_mask = new Uint32Array(10)
function clipping_mask_clear() {
  clipping_mask.fill(0)
}
function clipping_mask_set_bit(column) {
  clipping_mask[column >> 5] |= 1 << (column & 0b11111)
}
function clipping_mask_check_bit(column) {
  return (clipping_mask[column >> 5] & 1 << (column & 0b11111)) === 0
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

let block_renderer_pool = []
function acquire_block_renderer() {
  return block_renderer_pool.pop() ?? create_block_renderer()
}
function release_block_renderer(renderer) {
  block_renderer_pool.push(renderer)
}
function create_block_renderer() {
  let list = [-1 << 20, 0]
  let result_rects = []

  function add_block(top, bottom, left, right) {
    list.push(left << 20 | top << 12, right << 20 | bottom << 12)
  }
  function sort_blocks() {
    for (let gap of [1402, 602, 264, 114, 46, 20, 8, 2]) {
      for (let i = gap; i < list.length; i += 2) {
        let left_top = list[i]
        let right_bottom = list[i + 1]
        for (var j = i; j >= gap; j -= gap) {
          if (left_top >= list[j - gap]) break
          list[j] = list[j - gap]
          list[j + 1] = list[j - gap + 1]
        }
        list[j] = left_top
        list[j + 1] = right_bottom
      }
    }
    for (let i = 2; i < list.length; i += 2) {
      list[i] |= i - 2
      list[i - 1] |= i
    }
    list[0] |= list.length - 2
  }
  function render_block(index, top, bottom, left, right) {
    while (index != 0) {
      let lt_prev = list[index]
      let other_left = lt_prev >> 20
      if (right < other_left) break
      let other_top = lt_prev >> 12 & 0xff
      if (bottom <= other_top) {
        if (right === other_left) break
        index = list[index + 1] & 0xfff
        continue
      }
      let rb_next = list[index + 1]
      let other_right = rb_next >> 20
      let other_bottom = rb_next >> 12 & 0xff
      let next = rb_next & 0xfff
      let prev = lt_prev & 0xfff
      if (other_bottom <= top) {
        connect_blocks(prev, next)
        render_block(next, other_top, other_bottom, other_left, other_right)
        index = list[prev + 1] & 0xfff
        continue
      }
      if (top < other_top) {
        result_rects.push(top, other_top, left, right)
        top = other_top
      } else if (other_top < top) {
        render_block(next, other_top, top, other_left, other_right)
        other_top = top
      }
      if (bottom < other_bottom) {
        right = other_right
        other_top = bottom
        list[index] = list[index] & ~0xff000 | other_top << 12
        index = next
      } else if (other_bottom < bottom) {
        top = other_bottom
        connect_blocks(prev, next)
        render_block(next, other_top, other_bottom, left, other_right)
        index = list[prev + 1] & 0xfff
      } else {
        right = other_right
        connect_blocks(prev, next)
        index = next
      }
    }
    result_rects.push(top, bottom, left, right)
  }
  function connect_blocks(prev, next) {
    list[prev + 1] = list[prev + 1] & ~0xfff | next
    list[next] = list[next] & ~0xfff | prev
  }
  function fill_result_rects() {
    sort_blocks()
    while (true) {
      let index = list[1] & 0xfff
      if (index === 0) break
      let lt_prev = list[index]
      let rb_next = list[index + 1]
      let left = lt_prev >> 20
      let right = rb_next >> 20
      let top = lt_prev >> 12 & 0xff
      let bottom = rb_next >> 12 & 0xff
      let next = rb_next & 0xfff
      connect_blocks(0, next)
      render_block(next, top, bottom, left, right)      
    }
    list.length = 2
  }
  return { add_block, fill_result_rects, result_rects }
}

let sprite_indexes = wad.read_all_sprite_indexes()
let sprites_by_type = new Map(wad.sprites_by_type.map(([type, angle_sprites]) => [
  type,
  angle_sprites.map(frame_sprites => (
    frame_sprites.map(name => (
      wad.read_picture(sprite_indexes.get(name))))
    )
  )
]))
let radii_by_thing_type = new Map()
for (let [type, sprites] of sprites_by_type) {
  let width = 0
  for (let list of sprites) {
    for (let sprite of list) {
      width = Math.max(width, sprite.width)
    }
  }
  radii_by_thing_type.set(type, width >> 1)
}

let ascending_order = (a, b) => a - b

let default_palette = wad.read_all_palettes()[0]

let normal_palettes = wad.read_all_colormaps().map(colormap => {
  let palette = new Uint32Array(256)
  for (let i = 0; i < 256; i++) {
    palette[i] = default_palette[colormap[i]]
  }
  return palette
})
let overbright_palettes = normal_palettes.map(palette => palette.map(color => {
  let r = color & 0xff
  let g = color >> 8 & 0xff
  let b = color >> 16 & 0xff
  r = min(0xff, (r + 30) * 1.5 | 0)
  g = min(0xff, (g + 30) * 1.5 | 0)
  b = min(0xff, (b + 30) * 1.5 | 0)
  return 0xff000000 | b << 16 | g << 8 | r
}))
let palettes = overbright_palettes

let textures_by_name = new Map(wad
  .read_all_textures()
  .map(texture => [texture.name, texture])
)

let all_flats = wad.read_all_flats()
let flats_by_name = new Map(all_flats.map(flat => [flat.name, flat]))
let flats_by_id = new Map(all_flats.map(flat => [flat.id, flat]))
let sky_texture = textures_by_name.get('SKY1')
let clipping_range_start = new Uint8Array(320)
let clipping_range_end = new Uint8Array(320)

let current_loop_id = 0
let overbright_checkbox = document.querySelector('#overbright-checkbox')
let open_doors = null
let doors_checkbox = document.querySelector('#doors-checkbox')
let light_levels_checkbox = document.querySelector('#light-levels-checkbox')
let map_select = document.querySelector('#map-select')
let stats_elem = document.querySelector('#stats')
map_select.disabled = false
map_select.onchange = () => {
  map_select.blur()
  clearTimeout(current_loop_id)
  init_map(map_select.value)
}
init_map(map_select.value)

function init_map(map_name) {
  let vertexes = wad.read_vertexes(map_name)
  let linedefs = wad.read_linedefs(map_name)
  let nodes = wad.read_nodes(map_name).map(node => ({
    ...node, thing_ids: []
  }))
  let subsectors = wad.read_subsectors(map_name).map(subsector => ({
    ...subsector, thing_ids: []
  }))
  let sidedefs = wad.read_sidedefs(map_name)
  let segments = wad.read_segments(map_name).map(segment => {
    let linedef = linedefs[segment.linedef_id]
    let front_sidedef_id = segment.is_back_side ? linedef.back_sidedef_id : linedef.front_sidedef_id
    let back_sidedef_id = segment.is_back_side ? linedef.front_sidedef_id : linedef.back_sidedef_id
    let front_sector_id = sidedefs[front_sidedef_id].sector_id
    let back_sector_id = back_sidedef_id == -1 ? -1 : sidedefs[back_sidedef_id].sector_id
    let start = vertexes[segment.start_vertex_id]
    let end = vertexes[segment.end_vertex_id]
    let inv_length = 1 / sqrt((start.x - end.x) ** 2 + (start.y - end.y) ** 2)
    let dx = (end.x - start.x) * inv_length
    let dy = (end.y - start.y) * inv_length
    return { ...segment, dx, dy, inv_length, front_sidedef_id, back_sidedef_id, front_sector_id, back_sector_id }
  })
  let sectors = wad.read_sectors(map_name)
  let light_sectors = []
  let types = [1, 2, 3, 8, 12, 13]
  for (let i = 0; i < sectors.length; i++) {
    let sector = sectors[i]
    if (types.includes(sector.type)) {
      light_sectors.push(i)
    }
    sector.palette_index = ~(sector.light_level >> 3) & 0x1f
  }
  let scrolling_walls = []
  for (let i = 0; i < linedefs.length; i++) {
    if (linedefs[i].line_type == 48) {
      scrolling_walls.push(linedefs[i].front_sidedef_id)
    }
  }  
  let things = wad.read_things(map_name).map(thing => {
    let sector_id = find_sector_id(thing.x, thing.y)
    let type = thing.type == 58 ? 3002 : thing.type
    return { ...thing, type, sector_id }
  })
  for (let thing_id = 0; thing_id < things.length; thing_id++) {
    let thing = things[thing_id]
    if (!(sprites_by_type.has(thing.type) && (thing.flags & 0b10100) === 0b00100)) continue
    let radius = radii_by_thing_type.get(thing.type)
    let node = nodes[nodes.length - 1]
    while (true) {
      let dist = (thing.x - node.x) * node.dy - (thing.y - node.y) * node.dx
      let child = dist >= radius ? node.right : dist <= -radius ? node.left : null
      if (child != null &&
        thing.y <= child.top - radius &&
        thing.y >= child.bottom + radius &&
        thing.x <= child.right - radius &&
        thing.x >= child.left + radius
      ) {
        if (child.is_node) {
          node = nodes[child.child_id]
        } else {
          subsectors[child.child_id].thing_ids.push(thing_id)
          break
        }
      } else {
        node.thing_ids.push(thing_id)
        break
      }
    }
  }
  let fullbright = false
  function get_palette(index, dist) {
    if (fullbright) return palettes[0]
    index += min((dist >> 4) - 10, 0)
    return palettes[max(index, 0)]
  }
  function get_wall_texture(name) {
    if (name == null) return null
    if (name.startsWith('SLADRIP')) {
      let fc = frame_count & 0x1f
      name = fc < 11 ? 'SLADRIP1' : fc < 22 ? 'SLADRIP2' : 'SLADRIP3'
    }
    return textures_by_name.get(name)
  }
  function get_flat_texture(name) {
    if (name == null) return null
    if (name.startsWith('NUKAGE')) {
      let fc = frame_count & 0x1f
      name = fc < 11 ? 'NUKAGE1' : fc < 22 ? 'NUKAGE2' : 'NUKAGE3'
    }
    return flats_by_name.get(name)
  }
  function find_sector_id(x, y) {
    let node = nodes[nodes.length - 1]
    while (true) {
      let right_side = (x - node.x) * node.dy - (y - node.y) * node.dx >= 0
      var child = right_side ? node.right : node.left
      if (!child.is_node) break
      node = nodes[child.child_id]
    }
    let subsector = subsectors[child.child_id]
    let segment = segments[subsector.first_seg_id]
    return segment.front_sector_id
  }

  let is_holding_space = false
  open_doors = null
  let player_height = 0
  let horizon = 99.5

  let player_x = things[0].x
  let player_y = things[0].y
  let player_ang = things[0].angle
  let player_los_dx = sins[player_ang + 90]
  let player_los_dy = sins[player_ang]
  
  /**
  @param {typeof things[number]} thing */
  function* render_thing(thing, dist) {
    let proj = (thing.x - player_x) * player_los_dy - (thing.y - player_y) * player_los_dx
    let scale = 160 / dist
    let sprite_angle = 0, mirror = false
    if (sprites_by_type.get(thing.type).length > 1) {
      let angle = thing.angle - 22
      if (angle < 0) angle += 360
      let thing_dx = sins[angle + 90]
      let thing_dy = sins[angle]
      let proj = (player_x - thing.x) * thing_dx + (player_y - thing.y) * thing_dy
      let dist = (player_x - thing.x) * thing_dy - (player_y - thing.y) * thing_dx
      let quar = abs(proj) >= abs(dist)
      sprite_angle = proj >= 0 ? (quar ? 0 : 1) : (quar ? 3 : 2)
      mirror = dist >= 0
      if (mirror) sprite_angle += 1
    }
    let sprites = sprites_by_type.get(thing.type)[sprite_angle]
    let sprite = sprites[(frame_count & 0x1f) * sprites.length >> 5]
  
    let sprite_scr_left = (proj - sprite.left_offset) * scale + 159.5
    let sprite_scr_right = sprite_scr_left + sprite.width * scale
    let column_from = min(max(ceil(sprite_scr_left), 0), 320)
    let column_upto = min(max(ceil(sprite_scr_right), 0), 320)
    if (column_from === column_upto) return

    let sector = sectors[thing.sector_id]
    let palette = get_palette(sector.palette_index, dist)
    let sprite_world_bottom = sector.floor_height - player_height
    let sprite_world_top = sprite_world_bottom + sprite.height
    let sprite_scr_top = horizon - sprite_world_top * scale
    let sprite_scr_bottom = horizon - sprite_world_bottom * scale
    let col_screen_to_sprite = sprite.width / (sprite_scr_right - sprite_scr_left)
    let row_sprite_to_screen = (sprite_scr_bottom - sprite_scr_top) / sprite.height
    let row_screen_to_sprite = 1 / row_sprite_to_screen
    for (let column = column_from; column < column_upto; column++) {
      let sprite_col = floor((column - sprite_scr_left) * col_screen_to_sprite)
      if (mirror) sprite_col = sprite.width - 1 - sprite_col
      let sprite_pixels_col_start = sprite_col * sprite.height
      let posts = sprite.columns[sprite_col]
      let { range_from, range_upto } = zbuffer_pop_until(column, dist)
      let rendered_something = false
      for (let post of posts) {
        let post_from = ceil((post & 0xffff) * row_sprite_to_screen + sprite_scr_top) 
        let post_upto = ceil((post >> 16) * row_sprite_to_screen + sprite_scr_top)
        post_from = min(max(post_from, range_from), range_upto)
        post_upto = min(max(post_upto, range_from), range_upto)
        if (post_from >= post_upto) continue
        rendered_something = true
        let sprite_pixels_row = (post_from - sprite_scr_top) * row_screen_to_sprite
        let buffer_idx_from = post_from * 320 + column
        let buffer_idx_upto = post_upto * 320 + column
        for (let idx = buffer_idx_from; idx < buffer_idx_upto; idx += 320) {
          let pixels_idx = sprite_pixels_col_start + floor(sprite_pixels_row)
          frame_buffer[idx] = palette[sprite.pixels[pixels_idx]]
          sprite_pixels_row += row_screen_to_sprite
        }
      }
      if (is_holding_space && rendered_something) yield 10
    }
  }
  function create_flat_renderer(is_ceiling) {
    let lines_from = new Uint16Array(200)
    let upper_from = 0
    let upper_upto = 0
    let lower_from = 0  
    let lower_upto = 0
    let column_from = 0
    let column_upto = 0
    let texture = null
    let palette_index = 0
    let height = 0
    let block_renderer_cache = new Map()
    let curr_block_renderer = null
    function* init(sector) {
      let surface_height = is_ceiling ? sector.ceiling_height : sector.floor_height
      let surface_texture = is_ceiling ? sector.ceiling_texture : sector.floor_texture
      let new_texture = get_flat_texture(surface_texture)
      let new_height = player_height - surface_height
      let new_palette_index = sector.palette_index
      if (texture === new_texture && height === new_height && palette_index === new_palette_index) return
      add_column(0, 0, 0)
      if (is_holding_space) yield* draw_deferred_rows()
      texture = new_texture
      height = new_height
      palette_index = new_palette_index
      curr_block_renderer = null
    }
    function* flush() {
      add_column(0, 0, 0)
      if (is_holding_space) yield* draw_deferred_rows()
      for (let [key, renderer] of block_renderer_cache) {
        palette_index = key & 0x1f
        texture = flats_by_id.get(key >> 5 & 0x7ff)
        height = key >> 16
        renderer.fill_result_rects()
        let rects = renderer.result_rects
        while (rects.length > 0) {
          let right = rects.pop()
          let left = rects.pop()
          let bottom = rects.pop()
          let top = rects.pop()
          for (let i = bottom - 1; i >= top; i--) {
            draw_row(i, left, right)
            if (is_holding_space) yield 10
          }
        }
        release_block_renderer(renderer)
      }
      block_renderer_cache.clear()
      curr_block_renderer = null
    }
    function draw_row(row, from, upto) {
      let dist = height / (row - horizon) * 160
      let offset = (from - 159.5) * (1 / 160)
      let column_dx = player_los_dx + player_los_dy * offset
      let column_dy = player_los_dy - player_los_dx * offset
      let step_x = player_los_dy * (1 / 160) * dist
      let step_y = -player_los_dx * (1 / 160) * dist
      let flat_x = player_x + column_dx * dist
      let flat_y = player_y + column_dy * dist
      let row_start = row * 320
      let palette = get_palette(palette_index, dist)
      for (let col = from; col < upto; col++) {
        let idx = ((~flat_y & 0x3f) << 6) + (flat_x & 0x3f)
        flat_x += step_x
        flat_y += step_y
        frame_buffer[row_start + col] = palette[texture.pixels[idx]]
      }
    }
    let deferred_rows = []
    function* draw_deferred_rows() {
      deferred_rows.reverse()
      while (deferred_rows.length > 0) {
        let row = deferred_rows.pop()
        let from = deferred_rows.pop()
        let upto = deferred_rows.pop()
        draw_row(row, from, upto)
        if (is_holding_space) yield 10
      }
    }
    function draw_edge_rows(from, upto) {
      if (is_holding_space) {
        for (let i = from; i < upto; i++) {
          deferred_rows.push(i, lines_from[i], column_upto)
        }
      } else {
        for (let i = from; i < upto; i++) {
          draw_row(i, lines_from[i], column_upto)
        }
      }
    }
    function draw_core_rows(from, upto) {
      if (is_holding_space) {
        for (let i = from; i < upto; i++) {
          deferred_rows.push(i, column_from, column_upto)
        }
      } else {
        for (let i = from; i < upto; i++) {
          draw_row(i, column_from, column_upto)
        }
      }
    }    
    function add_column(column, from, upto) {
      if (column != column_upto || from === upto || 
        lower_upto <= from  || upto <= upper_from || 
        upto >= lower_upto + 10 || from <= upper_from - 10 ||
        upto <= lower_from - 10 || from >= upper_upto + 10
      ) {
        draw_edge_rows(upper_from, upper_upto)
        if (lower_from - upper_upto <= 10) {
          draw_core_rows(upper_upto, lower_from)
        } else {
          if (curr_block_renderer === null) {
            let key = height << 16 | texture.id << 5 | palette_index
            curr_block_renderer = block_renderer_cache.get(key)
            if (curr_block_renderer == null) {
              curr_block_renderer = acquire_block_renderer()
              block_renderer_cache.set(key, curr_block_renderer)
            }
          }
          curr_block_renderer.add_block(upper_upto, lower_from, column_from, column_upto)
          if (is_holding_space) {
            let red = Math.random() * 200 + 50 | 0
            let blue = Math.random() * 200 + 50 | 0
            let color = 0xff00bb00 | red | blue << 16
            for (let row = upper_upto; row < lower_from; row++) {
              let buffer_row_idx = row * 320
              for (let col = column_from; col < column_upto; col++) {
                frame_buffer[buffer_row_idx + col] = color
              }
            }
          }
        }
        draw_edge_rows(lower_from, lower_upto)
        column_from = column
        column_upto = column + 1
        upper_from = upper_upto = from
        lower_from = lower_upto = upto
        return
      }
      if (from <= upper_from) {
        for (let i = from; i < upper_from; i++) {
          lines_from[i] = column
        }
        upper_from = from
      } else if (from <= upper_upto) {
        draw_edge_rows(upper_from, from)
        upper_from = from
      } else if (from <= lower_from) {
        draw_edge_rows(upper_from, upper_upto)
        draw_core_rows(upper_upto, from)
        upper_from = upper_upto = from
      } else {
        draw_edge_rows(upper_from, upper_upto)
        draw_core_rows(upper_upto, lower_from)
        draw_edge_rows(lower_from, from)
        upper_from = upper_upto = lower_from = from
      }
      if (upto >= lower_upto) {
        for (let i = lower_upto; i < upto; i++) {
          lines_from[i] = column
        }
        lower_upto = upto
      } else if (upto >= lower_from) {
        draw_edge_rows(upto, lower_upto)
        lower_upto = upto
      } else if (upto >= upper_upto) {
        draw_core_rows(upto, lower_from)
        draw_edge_rows(lower_from, lower_upto)
        lower_from = lower_upto = upto
      } else {
        draw_edge_rows(upto, upper_upto)
        draw_core_rows(upper_upto, lower_from)
        draw_edge_rows(lower_from, lower_upto)
        lower_from = lower_upto = upper_upto = upto
      }
      column_upto = column + 1
    }
    return { add_column, init, draw_deferred_rows, flush }
  }

  let ceiling_renderer = create_flat_renderer(true)
  let floor_renderer = create_flat_renderer(false)

  let accum_time = 0
  let frame_count = 0
  let thing_pairs = []
  let floater_renderers = []
  let floater_pairs = []

  let zbuffer = new Uint32Array(320 * 16)
  function zbuffer_clear() {
    for (let i = 0; i < zbuffer.length; i += 16) {
      zbuffer[i] = 0
    }
  }
  function zbuffer_push(column, from, upto, dist) {
    let offset = column << 4
    let index = zbuffer[offset] + 1
    if (index == 16) return
    zbuffer[offset] = index
    zbuffer[offset + index] = dist << 16 | from << 8 | upto
  }
  function zbuffer_pop_until(column, dist) {
    let offset = column << 4
    let index = zbuffer[offset]
    let from = 0, upto = 200
    while (true) {
      if (index == 0) {
        from = 0, upto = 200
        break
      }
      let value = zbuffer[offset + index]
      if ((value >> 16) < dist) {
        from = value >> 8 & 0xff
        upto = value & 0xff
        break    
      }
      zbuffer[offset] = --index
    }
    return { range_from: from, range_upto: upto }
  }
  let floater_renderer_pool = []
  function acquire_floater_renderer() {
    return floater_renderer_pool.pop() ?? create_floater_renderer()
  }
  function release_floater_renderer(renderer) {
    floater_renderer_pool.push(renderer)
  }
  function create_floater_renderer() {
    let patch = null
    let left_dist = 0
    let right_dist = 0
    let segment = null
    let vertex = null
    let sidedef = null
    let sector = null
    let wall_base_height = 0
    let floor_height = 0, ceiling_height = 0
    let intersec_dist = 0, intersec_offset = 0
    let column = 0, column_upto = 0, column_step = 0
    let unpegged = false
    let min_dist = 0
    let player_proj = 0, player_dist = 0
    /** 
    @param {typeof segments[number]} segment_ */
    function init(segment_, left_edge, right_edge, left_dist_, right_dist_) {
      segment = segment_
      left_dist = left_dist_
      right_dist = right_dist_
      if (left_dist > right_dist) {
        min_dist = max(0, right_dist - 0.00001)
        column = left_edge - 1
        column_upto = right_edge - 1
        column_step = 1
      } else {
        min_dist = max(0, left_dist - 0.00001)
        column = right_edge
        column_upto = left_edge
        column_step = -1
      }
      vertex = vertexes[segment.start_vertex_id]
      unpegged = linedefs[segment.linedef_id].lower_unpegged
      sector = sectors[segment.front_sector_id]
      let back_sector = sectors[segment.back_sector_id]

      sidedef = sidedefs[segment.front_sidedef_id]
      patch = get_wall_texture(sidedef.middle_texture).patches[0]
      floor_height = max(sector.floor_height, back_sector.floor_height)
      ceiling_height = min(sector.ceiling_height, back_sector.ceiling_height)
      wall_base_height = unpegged ? floor_height : ceiling_height - patch.height
      player_dist = (player_x - vertex.x) * segment.dy - (player_y - vertex.y) * segment.dx
      player_proj = (player_x - vertex.x) * segment.dx + (player_y - vertex.y) * segment.dy
    }
    function move_to_next_column() {
      if (column == column_upto) return intersec_dist = -1
      column += column_step
      let offset = (column - 159.5) * (1 / 160)
      let column_x = player_x + player_los_dx + player_los_dy * offset
      let column_y = player_y + player_los_dy - player_los_dx * offset
      let column_dist = (column_x - vertex.x) * segment.dy - (column_y - vertex.y) * segment.dx
      let column_proj = (column_x - vertex.x) * segment.dx + (column_y - vertex.y) * segment.dy
      let ratio = (column_proj - player_proj) / (column_dist - player_dist)
      intersec_offset = column_proj - column_dist * ratio
      intersec_dist = left_dist + (right_dist - left_dist) * intersec_offset * segment.inv_length
      return intersec_dist
    }
    function* render_until(min_dist) {
      while (intersec_dist >= min_dist) {
        let scale = 160 / intersec_dist
        let column_scr_bottom = horizon + (player_height - wall_base_height) * scale
        let column_scr_top = horizon + (player_height - wall_base_height - patch.height) * scale
        let patch_x = floor(segment.offset + intersec_offset + sidedef.x_offset) % patch.width
        if (patch_x < 0) patch_x += patch.width
        let palette = get_palette(sector.palette_index, intersec_dist)
        let { range_from, range_upto } = zbuffer_pop_until(column, intersec_dist)
        let scr_ceiling = ceil(horizon + (player_height - ceiling_height) * scale)
        let scr_floor = ceil(horizon + (player_height - floor_height) * scale)
        range_from = min(max(scr_ceiling, range_from), range_upto)
        range_upto = min(max(scr_floor, range_from), range_upto)
        let patch_to_screen = (column_scr_bottom - column_scr_top) / patch.height
        let screen_to_patch = 1 / patch_to_screen
        let patch_pixels_row = patch_x * patch.height
        let rendered_something = false
        for (let post of patch.columns[patch_x]) {
          let post_from = post & 0xffff
          let post_upto = post >> 16
          post_from = ceil(post_from * patch_to_screen + column_scr_top)
          post_upto = ceil(post_upto * patch_to_screen + column_scr_top)
          post_from = min(max(post_from, range_from), range_upto)
          post_upto = min(max(post_upto, range_from), range_upto)
          if (post_from >= post_upto) continue
          rendered_something = true
          let patch_y = (post_from - column_scr_top) * screen_to_patch
          let buffer_idx_from = post_from * 320 + column
          let buffer_idx_upto = post_upto * 320 + column 
          for (let idx = buffer_idx_from; idx < buffer_idx_upto; idx += 320) {
            frame_buffer[idx] = palette[patch.pixels[patch_pixels_row + floor(patch_y)]]
            patch_y += screen_to_patch
          }
        }
        if (is_holding_space && rendered_something) yield 10
        move_to_next_column()
      }
      return intersec_dist
    }
    function get_min_dist() {
      return min_dist
    }
    return { init, move_to_next_column, render_until, get_min_dist }
  }

  function draw_column(texture, texture_top, texture_x, dist, column, from, upto, palette) {
    let tex_col = floor(texture_x) % texture.width
    if (tex_col < 0) tex_col += texture.width
    let tex_world_step = (1 / 160) * dist
    let tex_world_row = (horizon - from) * tex_world_step + player_height
    let texture_col_start = texture.height * tex_col
    let buffer_from = from * 320 + column
    let buffer_upto = upto * 320 + column
    for (let idx = buffer_from; idx < buffer_upto; idx += 320) {
      let tex_row = floor(texture_top - tex_world_row) & 0x7f
      tex_world_row -= tex_world_step
      frame_buffer[idx] = palette[texture.pixels[texture_col_start + tex_row]]
    }               
  }
  function draw_sky_column(column, from, upto) {
    let idx = from * 320 + column
    let palette = palettes[0]
    let offset = -player_ang / 90 + column / 320
    let sky_column = floor((offset - floor(offset)) * sky_texture.width)
    let sky_pixels_col_start = sky_column * sky_texture.height
    let sky_from = ceil(horizon - 100)
    let sky_from_clamped = min(max(sky_from, from), upto)
    for (let row = from; row < sky_from_clamped; row++) {
      let sky_idx = sky_pixels_col_start + (row & 7)
      frame_buffer[idx] = palette[sky_texture.pixels[sky_idx]]
      idx += 320
    }
    for (let row = sky_from_clamped; row < upto; row++) {
      let sky_idx = sky_pixels_col_start + (row - sky_from & 0x7f)
      frame_buffer[idx] = palette[sky_texture.pixels[sky_idx]]
      idx += 320
    }
  }  
  function* main_loop() {
    let node_count = 0
    let segment_count = 0
    let thing_count = 0
    let time_start = performance.now()
    let player_dx = 0, player_dy = 0
    if (keys.has('ArrowUp') || keys.has('Numpad8')) {
      player_dx += player_los_dx
      player_dy += player_los_dy
    } else if (keys.has('ArrowDown') || keys.has('Numpad2')) {
      player_dx -= player_los_dx
      player_dy -= player_los_dy
    }
    if (keys.has('ArrowRight') || keys.has('Numpad6')) {
      if (keys.has('AltLeft')) {
        player_dx += player_los_dy
        player_dy -= player_los_dx
      } else {
        player_ang -= keys.has('ShiftLeft') ? 1 : 7
        if (player_ang < 0) player_ang += 360
      }
    } else if (keys.has('ArrowLeft') || keys.has('Numpad4')) {
      if (keys.has('AltLeft')) {
        player_dx -= player_los_dy
        player_dy += player_los_dx
      } else {
        player_ang += keys.has('ShiftLeft') ? 1 : 7
        if (player_ang >= 360) player_ang -= 360
      }
    }
    if (player_dx != 0 || player_dy != 0) {
      let len = sqrt(player_dx ** 2 + player_dy ** 2)
      let speed = keys.has('ShiftLeft') ? 2 : 15
      player_x += player_dx / len * speed
      player_y += player_dy / len * speed
    }
    if (keys.has('KeyQ')) {
      horizon = min(199.5, horizon + (keys.has('ShiftLeft') ? 2 : 10))
    }
    if (keys.has('KeyA')) {
      horizon = max(-0.5, horizon - (keys.has('ShiftLeft') ? 2 : 10))
    }
    player_los_dx = sins[player_ang + 90]
    player_los_dy = sins[player_ang]
    let player_sector_id = find_sector_id(player_x, player_y)
    let player_sector = sectors[player_sector_id]
    player_height = min(player_sector.floor_height + 41, player_sector.ceiling_height - 1)

    function project_line_segment_edges(x1, y1, x2, y2) {
      let dist1 = (x1 - player_x) * player_los_dx + (y1 - player_y) * player_los_dy
      let dist2 = (x2 - player_x) * player_los_dx + (y2 - player_y) * player_los_dy
      let col1 = 0, col2 = 0
      do {
        if (dist1 > 0 || dist2 > 0) {
          let proj1, proj2
          if (dist1 <= 0) {
            let side = (player_x - x1) * (y2 - y1) - (player_y - y1) * (x2 - x1)
            if (side === 0) break
            proj1 = side > 0 ? -Infinity : Infinity
          } else {
            proj1 = (x1 - player_x) * player_los_dy - (y1 - player_y) * player_los_dx
            proj1 *= 160 / dist1
          }
          if (dist2 <= 0) {
            let side = (player_x - x2) * (y1 - y2) - (player_y - y2) * (x1 - x2)
            if (side === 0) break
            proj2 = side > 0 ? -Infinity : Infinity
          } else {
            proj2 = (x2 - player_x) * player_los_dy - (y2 - player_y) * player_los_dx
            proj2 *= 160 / dist2
          }
          col1 = min(max(ceil(proj1 + 159.5), 0), 320)
          col2 = min(max(ceil(proj2 + 159.5), 0), 320)
        }
      } while (false)
      return { col1, col2, dist1, dist2 }
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
      let { col1, col2 } = project_line_segment_edges(left_x, left_y, right_x, right_y)
      return col1 != col2 && clipping_mask_check_range(col1, col2)
    }
    /**
    @param {typeof segments[number]} segment */
    function* project_segment(segment, from, upto) {
      let { x: start_x, y: start_y } = vertexes[segment.start_vertex_id]
      let player_dist = (player_x - start_x) * segment.dy - (player_y - start_y) * segment.dx
      let player_proj = (player_x - start_x) * segment.dx + (player_y - start_y) * segment.dy
      for (let column = from; column < upto; column++) {
        if (!clipping_mask_check_bit(column)) continue
        let offset = (column - 159.5) * (1 / 160)
        let column_x = player_x + player_los_dx + player_los_dy * offset
        let column_y = player_y + player_los_dy - player_los_dx * offset
        let column_dist = (column_x - start_x) * segment.dy - (column_y - start_y) * segment.dx
        let column_proj = (column_x - start_x) * segment.dx + (column_y - start_y) * segment.dy
        let ratio = (column_proj - player_proj) / (column_dist - player_dist)
        let inter_offset = column_proj - column_dist * ratio
        yield { column, inter_offset }
      }
    }
    function* render_node(node_id, is_node) {
      node_count += 1
      if (is_node) {
        let node = nodes[node_id]
        thing_pairs.push(...node.thing_ids)
        let on_right_side = (player_x - node.x) * node.dy - (player_y - node.y) * node.dx >= 0
        let front_child = on_right_side ? node.right : node.left
        let back_child = on_right_side ? node.left : node.right
        if (is_node_child_visibile(front_child)) {
          yield* render_node(front_child.child_id, front_child.is_node)
        }
        if (is_node_child_visibile(back_child)) {
          yield* render_node(back_child.child_id, back_child.is_node)
        }
        return
      }

      let subsector = subsectors[node_id]
      thing_pairs.push(...subsector.thing_ids)
      for (let i = subsector.first_seg_id; i <= subsector.last_seg_id; i++) {
        let segment = segments[i]
        let start = vertexes[segment.start_vertex_id]
        let end = vertexes[segment.end_vertex_id]
        let { col1: left_edge, col2: right_edge, dist1: left_dist, dist2: right_dist } = 
          project_line_segment_edges(start.x, start.y, end.x, end.y)
        if (left_edge >= right_edge || !clipping_mask_check_range(left_edge, right_edge)) continue
        let sidedef = sidedefs[segment.front_sidedef_id]
        let linedef = linedefs[segment.linedef_id]
        let front_sector = sectors[segment.front_sector_id]
        if (segment.back_sector_id === -1) {
          segment_count += 1
          let wall_texture = get_wall_texture(sidedef.middle_texture)
          yield* ceiling_renderer.init(front_sector)
          yield* floor_renderer.init(front_sector)
          for (let { column, inter_offset } of project_segment(segment, left_edge, right_edge)) {
            let dist = left_dist + (right_dist - left_dist) * inter_offset * segment.inv_length
            let scale = 160 / dist
            let palette = get_palette(front_sector.palette_index, dist)
            let range_from = clipping_range_start[column]
            let range_upto = clipping_range_end[column]
            let wall_from = horizon - scale * (front_sector.ceiling_height - player_height)
            let wall_upto = horizon - scale * (front_sector.floor_height - player_height)
            wall_from = min(max(ceil(wall_from), range_from), range_upto)
            wall_upto = min(max(ceil(wall_upto), range_from), range_upto)
            let rendered_something = false
            if (range_from < wall_from && front_sector.ceiling_texture != null) {
              if (front_sector.ceiling_texture == 'F_SKY1') {
                rendered_something = true
                draw_sky_column(column, range_from, wall_upto)
              } else {
                ceiling_renderer.add_column(column, range_from, wall_from)
                if (is_holding_space) yield* ceiling_renderer.draw_deferred_rows()
              }
            }
            if (wall_upto < range_upto && front_sector.floor_texture != null) {
              floor_renderer.add_column(column, wall_upto, range_upto)
              if (is_holding_space) yield* floor_renderer.draw_deferred_rows()
            }

            if (wall_from < wall_upto && wall_texture != null) {
              let texture_x = segment.offset + inter_offset + sidedef.x_offset
              let texture_top = linedef.lower_unpegged ? 
                front_sector.floor_height + wall_texture.height : 
                front_sector.ceiling_height
              texture_top += sidedef.y_offset
              rendered_something = true
              draw_column(wall_texture, texture_top, texture_x, dist, 
                column, wall_from, wall_upto, palette)
            }
            zbuffer_push(column, 0, 0, dist)
            if (is_holding_space && rendered_something) yield 10
          }
          clipping_mask_set_range(left_edge, right_edge)
          continue
        }
        if (sidedef.middle_texture != null) {
          let renderer = acquire_floater_renderer()
          renderer.init(segment, left_edge, right_edge, left_dist, right_dist)
          floater_renderers.push(renderer)
        }
        let back_sector = sectors[segment.back_sector_id]
        let different_ceilings = 
          front_sector.ceiling_height !== back_sector.ceiling_height ||
          front_sector.palette_index !== back_sector.palette_index ||
          front_sector.ceiling_texture !== back_sector.ceiling_texture
        let different_floors = 
          front_sector.floor_height !== back_sector.floor_height ||
          front_sector.palette_index !== back_sector.palette_index ||
          front_sector.floor_texture !== back_sector.floor_texture
        let is_sky_wall = 
          front_sector.ceiling_texture == 'F_SKY1' && 
          back_sector.ceiling_texture == 'F_SKY1'
        if ((different_ceilings && !is_sky_wall) || different_floors) {
          segment_count += 1
          let is_closed_door = back_sector.ceiling_height == back_sector.floor_height
          let upper_wall_texture = get_wall_texture(sidedef.upper_texture)
          let lower_wall_texture = get_wall_texture(sidedef.lower_texture)
          yield* ceiling_renderer.init(front_sector)
          yield* floor_renderer.init(front_sector)
          for (let { column, inter_offset } of project_segment(segment, left_edge, right_edge)) {
            let dist = left_dist + (right_dist - left_dist) * inter_offset * segment.inv_length
            let scale = 160 / dist
            let palette = get_palette(front_sector.palette_index, dist)
            let range_from = clipping_range_start[column]
            let range_upto = clipping_range_end[column]
            let rendered_something = false
            if ((different_ceilings || is_closed_door) && !is_sky_wall) {
              let ceiling_upto = horizon - scale * (front_sector.ceiling_height - player_height)
              ceiling_upto = min(max(ceil(ceiling_upto), range_from), range_upto)
              if (range_from < ceiling_upto && front_sector.ceiling_texture != null) {
                if (front_sector.ceiling_texture == 'F_SKY1') {
                  rendered_something = true
                  draw_sky_column(column, range_from, ceiling_upto)
                } else {
                  ceiling_renderer.add_column(column, range_from, ceiling_upto)
                  if (is_holding_space) yield* ceiling_renderer.draw_deferred_rows()
                }
              }
              range_from = ceiling_upto
            }
            if (different_floors || is_closed_door) {
              let floor_from =  horizon - scale * (front_sector.floor_height - player_height)
              floor_from = min(max(ceil(floor_from), range_from), range_upto)
              if (floor_from < range_upto && front_sector.floor_texture != null) {
                floor_renderer.add_column(column, floor_from, range_upto)
                if (is_holding_space) yield* floor_renderer.draw_deferred_rows()
              }
              range_upto = floor_from
            }
            if (front_sector.ceiling_height > back_sector.ceiling_height && !is_sky_wall) {
              let wall_upto = ceil(horizon - scale * (back_sector.ceiling_height - player_height))
              wall_upto = min(max(wall_upto, range_from), range_upto)
              if (range_from < wall_upto && sidedef.upper_texture != null) {
                let texture_x = segment.offset + inter_offset + sidedef.x_offset
                let texture_top = linedef.upper_unpegged ? 
                  front_sector.ceiling_height : 
                  back_sector.ceiling_height + upper_wall_texture.height
                texture_top += sidedef.y_offset
                rendered_something = true
                draw_column(upper_wall_texture, texture_top, texture_x, dist, 
                  column, range_from, wall_upto, palette)
              }              
              range_from = wall_upto
            }
            if (front_sector.floor_height < back_sector.floor_height) {
              let wall_from = ceil(horizon - scale * (back_sector.floor_height - player_height))
              wall_from = min(max(wall_from, range_from), range_upto)
              if (wall_from < range_upto && sidedef.lower_texture != null) {
                let texture_x = segment.offset + inter_offset + sidedef.x_offset
                let texture_top = linedef.lower_unpegged ? 
                  front_sector.ceiling_height : back_sector.floor_height
                texture_top += sidedef.y_offset
                rendered_something = true
                draw_column(lower_wall_texture, texture_top, texture_x, dist, 
                  column, wall_from, range_upto, palette)
              }              
              range_upto = wall_from
            }
            if (range_from === range_upto) {
              clipping_mask_set_bit(column)
            } else {
              clipping_range_start[column] = range_from
              clipping_range_end[column] = range_upto
            }
            if (back_sector.floor_height < front_sector.floor_height ||
                back_sector.floor_height > player_height ||
                back_sector.ceiling_height < player_height ||
                back_sector.ceiling_height > front_sector.ceiling_height
            ) {
              zbuffer_push(column, range_from, range_upto, dist)
            }
            if (is_holding_space && rendered_something) yield 10
          } 
        }
      }
    }
    for (let sidedef_id of scrolling_walls) {
      let sidedef = sidedefs[sidedef_id]
      sidedef.x_offset += 1
    }    
    fullbright = !light_levels_checkbox.checked
    for (let sector_id of light_sectors) {
      let sector = sectors[sector_id]
      let index = ~(sector.light_level >> 3) & 0x1f
      if (sector.type == 8) {
        index += abs(16 - (frame_count + sector_id >> 1 & 0x1f))
      } else if (sector.type == 1) {
        if ((frame_count + sector_id & 0b1111110) === 0) index += 10
      } else {
        let shifted_fc = frame_count + (sector.type <= 3 ? sector_id : 0)
        let mask = sector.type == 3 || sector.type == 12 ? 0b111100 : 0b11100
        if ((shifted_fc & mask) !== 0) index += 15
      }
      sector.palette_index = min(index, 31)
    }
    if (overbright_checkbox.checked) {
      if (palettes === normal_palettes) {
        palettes = overbright_palettes
      }
    } else if (palettes === overbright_palettes) {
      palettes = normal_palettes
    }
    if (!open_doors == doors_checkbox.checked) {
      if (open_doors) {
        for (let [door_id, raise_ceiling] of open_doors) {
          let sector = sectors[door_id]
          if (raise_ceiling) {
            sector.ceiling_height = sector.floor_height
          } else {
            sector.floor_height = sector.ceiling_height
          }
        }
        open_doors = null
      } else {
        open_doors = new Map()
        for (let segment of segments) {
          let door_id = segment.back_sector_id
          if (door_id == -1) continue
          let sector = sectors[door_id]
          if (sector.floor_height != sector.ceiling_height) continue
          let adjacent_sector = sectors[segment.front_sector_id]
          if (sidedefs[segment.front_sidedef_id].upper_texture != null) {
            let adjacent_height = adjacent_sector.ceiling_height
            let lowest_height = open_doors.get(door_id) ?? 100000
            open_doors.set(door_id, min(adjacent_height, lowest_height))
          } else {
            let adjacent_height = adjacent_sector.floor_height
            let highest_height = open_doors.get(door_id) ?? -100000
            open_doors.set(door_id, max(adjacent_height, highest_height))
          }
        }
        for (let [door_id, target_height] of open_doors) {
          let raise_ceiling = sectors[door_id].floor_height < target_height
          if (raise_ceiling) {
            sectors[door_id].ceiling_height = target_height
          } else {
            sectors[door_id].floor_height = target_height
          }
          open_doors.set(door_id, raise_ceiling)
        }
      }
    }
    for (let i = 0; i < 1; i++) {
      zbuffer_clear()
      frame_buffer.fill(0xff88dd88)
      clipping_mask_clear()
      clipping_range_start.fill(0)
      clipping_range_end.fill(200)
      yield* render_node(nodes.length - 1, true)
      yield* ceiling_renderer.flush()
      yield* floor_renderer.flush()
      for (let i = 0; i < floater_renderers.length; i++) {
        let renderer = floater_renderers[i]
        let dist = renderer.move_to_next_column()
        if (dist == -1) continue
        floater_pairs.push(dist << 16 | i)
      }
      floater_pairs.sort(ascending_order)

      for (let i = 0; i < thing_pairs.length; i++) {
        let thing = things[thing_pairs[i]]
        let dist = (thing.x - player_x) * player_los_dx + (thing.y - player_y) * player_los_dy
        dist <<= 16
        if (dist <= 0) {
          let last = thing_pairs.pop()
          if (i == thing_pairs.length) break
          thing_pairs[i--] = last
        } else {
          thing_pairs[i] |= dist
        }
      }
      thing_pairs.sort(ascending_order)
      thing_count = thing_pairs.length
      while (true) {
        let floater_pair = floater_pairs.length == 0 ? -1 : floater_pairs.at(-1)
        let thing_pair = thing_pairs.length == 0 ? -1 : thing_pairs.at(-1)
        if (thing_pair == -1 && floater_pair == -1) break
        if (thing_pair > floater_pair) {
          thing_pairs.pop()
          let thing_id = thing_pair & 0xffff
          let dist = thing_pair >> 16
          yield* render_thing(things[thing_id], dist)
        } else {
          let floater_id = floater_pair & 0xffff
          let renderer = floater_renderers[floater_id]
          let target_dist = max(renderer.get_min_dist(), thing_pair >> 16)
          let idx = floater_pairs.length - 2
          for (; idx >= 0; idx--) {
            let pair = floater_pairs[idx]
            let closer_id = pair & 0xffff
            let closer_dist = pair >> 16
            if (closer_dist <= target_dist) break
            if (closer_id < floater_id) continue
            target_dist = closer_dist
            break
          }
          let floater_dist = yield* renderer.render_until(target_dist)
           
          if (floater_dist == -1) {
            floater_pairs.pop()
          } else {
            floater_pair = floater_dist << 16 | floater_id
            while (idx >= 0 && floater_pairs[idx] > floater_pair) idx--
            idx += 1
            let i = floater_pairs.length - 1
            while (i > idx) floater_pairs[i] = floater_pairs[--i]
            floater_pairs[idx] = floater_pair
          }
        }
      }
      while (floater_renderers.length > 0) {
        release_floater_renderer(floater_renderers.pop())
      }
    }
    while (is_holding_space) yield 50
    let frame_time = performance.now() - time_start
    accum_time += frame_time
    if (++frame_count % 10 === 0) {
      stats_elem.textContent = `\
Time: ${(accum_time / 10).toFixed(2)}
Nodes: ${node_count}/${nodes.length + subsectors.length}
Segments: ${segment_count}
Things: ${thing_count}
Sector: ${player_sector_id}
x: ${player_x | 0}
y: ${player_y | 0}`
      accum_time = 0
    }
    return max(0, 25 - frame_time / 1000 | 0)
  }

  let rator = main_loop()
  current_loop_id = setTimeout(function tick() {
    is_holding_space = keys.has('Space')
    let { done, value } = rator.next()
    render_frame_buffer()
    if (done) rator = main_loop()
    current_loop_id = setTimeout(tick, value ?? 25)
  }, 25)
}
