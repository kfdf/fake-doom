let response = await fetch('../doom1.wad')
let wad_data = new DataView(await response.arrayBuffer())

function read_string(offset, size) {
  let ret = ''
  for (let i = offset; i < offset + size; i++) {
    let code = wad_data.getUint8(i)
    if (code == 0) break
    ret += String.fromCharCode(code)
  }
  return ret.toUpperCase()
}
function read_byte(offset) {
  return wad_data.getUint8(offset)
}
function read_int(offset) {
  return wad_data.getInt32(offset, true)
}
function read_short(offset) {
  return wad_data.getInt16(offset, true)
}
function read_texture_name(offset) {
  let name = read_string(offset, 8)
  return name !== '-' ? name : null
}
let wad_type = read_string(0, 4)
let lump_count = read_int(4)
let directory_offset = read_int(8)

let directory = Array.from(function* () {
  for (let i = 0; i < lump_count; i++) {
    let entry_offset = directory_offset + i * 16
    let data_from = read_int(entry_offset)
    let data_size = read_int(entry_offset + 4)
    let name = read_string(entry_offset + 8, 8)
    let data_upto = data_from + data_size
    yield { name, data_from, data_upto }
  }
}())
function get_lump_index(...lump_names) {
  let index = -1
  for (let lump_name of lump_names) {
    while (directory[++index].name != lump_name) ;
  }
  return index
}

export function read_vertexes(map_name) {
  let ret = []
  let index = get_lump_index(map_name, 'VERTEXES')
  let { data_from, data_upto } = directory[index]
  for (let i = data_from; i < data_upto; i += 4) {
    let x = read_short(i)
    let y = read_short(i + 2)
    ret.push({ x, y })
  }
  return ret
}

export function read_linedefs(map_name) {
  let ret = []
  let index = get_lump_index(map_name, 'LINEDEFS')
  let { data_from, data_upto } = directory[index]
  for (let i = data_from; i < data_upto; i += 14) {
    let flags = read_short(i + 4)
    ret.push({
      start_vertex_id: read_short(i),
      end_vertex_id: read_short(i + 2),
      flags,
      upper_unpegged: (flags & 0b1000) !== 0,
      lower_unpegged: (flags & 0b10000) !== 0,
      line_type: read_short(i + 6),
      sector_tag: read_short(i + 8),
      front_sidedef_id: read_short(i + 10),
      back_sidedef_id: read_short(i + 12),
    })
  }  
  return ret
}
function read_child_data(i, j) {
  let child = read_short(j)
  let is_node = child >= 0
  if (!is_node) child += 0x8000
  return {
    top: read_short(i),
    bottom: read_short(i + 2),
    left: read_short(i + 4),
    right: read_short(i + 6),
    is_node,
    child_id: child,
  }  
}

export function read_nodes(map_name) {
  let ret = []
  let index = get_lump_index(map_name, 'NODES')
  let { data_from, data_upto } = directory[index]
  for (let i = data_from; i < data_upto; i += 28) {
    let dx = read_short(i + 4)
    let dy = read_short(i + 6)
    let len = Math.sqrt(dx * dx + dy * dy)
    dx /= len
    dy /= len
    ret.push({
      x: read_short(i),
      y: read_short(i + 2),
      dx, dy,
      right: read_child_data(i + 8, i + 24),
      left: read_child_data(i + 16, i + 26),
    })
  }
  return ret
}
export function read_subsectors(map_name) {
  let ret = []
  let index = get_lump_index(map_name, 'SSECTORS')
  let { data_from, data_upto } = directory[index]
  for (let i = data_from; i < data_upto; i += 4) {
    let seg_count = read_short(i)
    let first_seg_id = read_short(i + 2)
    let last_seg_id = first_seg_id + seg_count - 1
    ret.push({ first_seg_id, last_seg_id })
  }
  return ret
}
export function read_segments(map_name) {
  let ret = []
  let index = get_lump_index(map_name, 'SEGS')
  let { data_from, data_upto } = directory[index]
  for (let i = data_from; i < data_upto; i += 12) {
    ret.push({
      start_vertex_id: read_short(i),
      end_vertex_id: read_short(i + 2),
      angle: read_short(i + 4),
      linedef_id: read_short(i + 6),
      is_back_side: Boolean(read_short(i + 8)),
      offset: read_short(i + 10),
    })
  }
  return ret
}

export function read_things(map_name) {
  let ret = []
  let index = get_lump_index(map_name, 'THINGS')
  let { data_from, data_upto } = directory[index]
  for (let i = data_from; i < data_upto; i += 10) {
    ret.push({
      x: read_short(i),
      y: read_short(i + 2),
      angle: read_short(i + 4),
      type: read_short(i + 6),
      flags: read_short(i + 8),
    })
  }  
  return ret
}

export function read_sectors(map_name) {
  let ret = []
  let index = get_lump_index(map_name, 'SECTORS')
  let { data_from, data_upto } = directory[index]
  for (let i = data_from; i < data_upto; i += 26) {
    ret.push({
      floor_height: read_short(i),
      ceiling_height: read_short(i + 2),
      floor_texture: read_texture_name(i + 4),
      ceiling_texture: read_texture_name(i + 12),
      light_level: read_short(i + 20),
      palette_index: 0,
      type: read_short(i + 22),
      tag: read_short(i + 24),
    })
  }
  return ret
}

export function read_sidedefs(map_name) {
  let ret = []
  let index = get_lump_index(map_name, 'SIDEDEFS')
  let { data_from, data_upto } = directory[index]
  for (let i = data_from; i < data_upto; i += 30) {
    ret.push({
      x_offset: read_short(i),
      y_offset: read_short(i + 2),
      upper_texture: read_texture_name(i + 4),
      lower_texture: read_texture_name(i + 12),
      middle_texture: read_texture_name(i + 20),
      sector_id: read_short(i + 28),
    })
  }
  return ret
}
export function read_all_palettes() {
  let ret = []
  let index = get_lump_index('PLAYPAL')
  let { data_from, data_upto } = directory[index]
  for (let i = data_from; i < data_upto; i += 256 * 3) {
    let palette = new Uint32Array(256)
    let idx = 0
    for (let j = i; j < i + 256 * 3; j += 3) {
      let red = read_byte(j)
      let green = read_byte(j + 1)
      let blue = read_byte(j + 2)
      let color = 0xff000000 | blue << 16 | green << 8 | red
      palette[idx++] = color
    }
    ret.push(palette)
  }
  return ret
}
export function read_picture(index) {
  let { data_from, name } = directory[index]
  let width = read_short(data_from)
  let height = read_short(data_from + 2)
  let pixels = new Uint8Array(width * height)
  let left_offset = read_short(data_from + 4)
  let top_offset = read_short(data_from + 6)
  let columns = []
  for (let i = 0; i < width; i++) {
    let column = []
    columns.push(column)
    let offset = data_from + read_int(data_from + 8 + i * 4)
    while (true) {
      let post_from = read_byte(offset)
      if (post_from == 0xFF) break
      let post_length = read_byte(offset + 1)
      let post_upto = post_from + post_length
      column.push(post_from | post_upto << 16)
      let dest_row_from = i * height
      for (let j = 0; j < post_length; j++) {
        let color = read_byte(offset + 3 + j)
        pixels[dest_row_from + post_from + j] = color
      }
      offset += post_length + 4
    }
  }
  return { name, width, height, left_offset, top_offset, pixels, columns }
}
export function read_sprites(names) {
  let ret = []
  let index_from = get_lump_index('S_START') + 1
  let index_upto = get_lump_index('S_END')
  let set_of_names = new Set(names)
  for (let i = index_from; i < index_upto; i++) {
    if (!set_of_names.has(directory[i].name)) continue
    ret.push(read_picture(i))
  }
  return ret
}
export function read_all_sprite_indexes() {
  let ret = new Map()
  let index_from = get_lump_index('S_START') + 1
  let index_upto = get_lump_index('S_END')
  for (let i = index_from; i < index_upto; i++) {
    ret.set(directory[i].name, i)
  }
  return ret
}
export function read_all_flats() {
  let ret = []
  let index_from = get_lump_index('F_START') + 1
  let index_upto = get_lump_index('F_END')
  let id = 0
  for (let i = index_from; i < index_upto; i++) {
    let { data_from, data_upto, name } = directory[i]
    if (data_upto - data_from != 64 * 64) continue
    let pixels = new Uint32Array(64 * 64)
    for (let j = 0; j < pixels.length; j++) {
      pixels[j] = read_byte(data_from + j)
    }
    id += 1
    ret.push({ name, pixels, id })
  }
  return ret
}

function read_all_patches() {
  let ret = []
  let index_from = get_lump_index('P_START') + 1
  let index_upto = get_lump_index('P_END')
  for (let i = index_from; i < index_upto; i++) {
    let { data_from, data_upto, name } = directory[i]
    if (data_from === data_upto) continue
    let picture = read_picture(i)
    ret.push({ name, picture })
  }
  return ret
}
function read_pnames() {
  let ret = []
  let index = get_lump_index('PNAMES')
  let { data_from, data_upto } = directory[index]
  for (let i = data_from + 4; i < data_upto; i += 8) {
    ret.push(read_string(i, 8))
  }
  return ret
}

export function read_all_textures(palette) {
  let ret = []
  let pnames = read_pnames()
  let all_patches = new Map(read_all_patches(palette).map(p => [p.name, p.picture]))
  let index = get_lump_index('TEXTURE1')
  let { data_from } = directory[index]
  let texture_count = read_int(data_from)
  for (let i = 0; i < texture_count; i++) {
    let offset = data_from + read_int(data_from + 4 + i * 4)
    let name = read_string(offset, 8)
    let flags = read_int(offset + 8)
    let width = read_short(offset + 12)
    let height = read_short(offset + 14)
    let pixels = new Uint8Array(width * height)
    let patch_count = read_short(offset + 20)
    let patch_data_from = offset + 22
    let patch_data_upto = patch_data_from + patch_count * 10
    let patches = []
    for (let i = patch_data_from; i < patch_data_upto; i += 10) {
      let x_offset = read_short(i)
      let y_offset = read_short(i + 2)
      let patch_name = pnames[read_short(i + 4)]
      let patch = all_patches.get(patch_name)
      patches.push(patch)
      let i_from = Math.max(0, -x_offset)
      let i_upto = Math.min(patch.width, width - x_offset)
      for (let i = i_from; i < i_upto; i++) {
        let src_row_idx = i * patch.height
        let dst_row_idx = (i + x_offset) * height
        let posts = patch.columns[i]
        for (let post of posts) {
          let post_from = post & 0xffff
          let post_upto = post >> 16
          post_from += Math.max(0, 0 - post_from - y_offset)
          post_upto += Math.min(0, height - post_upto - y_offset)
          for (let j = post_from; j < post_upto; j++) {
            let src_idx = src_row_idx + j
            let dst_idx = dst_row_idx + j + y_offset
            pixels[dst_idx] = patch.pixels[src_idx]
          }
        }
      }
    }
    ret.push({ name, width, height, pixels, patches })
  }
  return ret
}

export function read_all_colormaps() {
  let ret = []
  let index = get_lump_index('COLORMAP')
  let { data_from, data_upto } = directory[index]
  for (let i = data_from; i < data_upto; i += 256) {
    ret.push(new Uint8Array(wad_data.buffer.slice(i, i + 256)))
  }
  return ret
}
/**
@type {([number, string[][]])[]} */
export let sprites_by_type = [
  [3004, [
    ['POSSA1', 'POSSB1'],
    ['POSSA2A8', 'POSSB2B8'],
    ['POSSA3A7', 'POSSB3B7'],
    ['POSSA4A6', 'POSSB4B6'],
    ['POSSA5', 'POSSB5'],
  ]],
  [9,    [
    ['SPOSA1', 'SPOSB1'],
    ['SPOSA2A8', 'SPOSB2B8'],
    ['SPOSA3A7', 'SPOSB3B7'],
    ['SPOSA4A6', 'SPOSB4B6'],
    ['SPOSA5', 'SPOSB5'],
  ]],
  [3002, [
    ['SARGA1', 'SARGB1'],
    ['SARGA2A8', 'SARGB2B8'],
    ['SARGA3A7', 'SARGB3B7'],
    ['SARGA4A6', 'SARGB4B6'],
    ['SARGA5', 'SARGB5'],
  ]],
  [3001, [
    ['TROOA1', 'TROOB1'],
    ['TROOA2A8', 'TROOB2B8'],
    ['TROOA3A7', 'TROOB3B7'],
    ['TROOA4A6', 'TROOB4B6'],
    ['TROOA5', 'TROOB5'],
  ]],
  [3003, [
    ['BOSSA1', 'BOSSB1'],
    ['BOSSA2A8', 'BOSSB2B8'],
    ['BOSSA3A7', 'BOSSB3B7'],
    ['BOSSA4A6', 'BOSSB4B6'],
    ['BOSSA5', 'BOSSB5'],
  ]],
  [2002, [['MGUNA0']]],
  [2005, [['CSAWA0']]],
  [2003, [['LAUNA0']]],
  [2001, [['SHOTA0']]],
  [2008, [['SHELA0']]],
  [2048, [['AMMOA0']]],
  [2046, [['BROKA0']]],
  [2049, [['SBOXA0']]],
  [2007, [['CLIPA0']]],
  [2010, [['ROCKA0']]],
  [2015, [['BON2A0', 'BON2B0', 'BON2C0', 'BON2D0', 'BON2C0', 'BON2B0']]],
  [2026, [['PMAPA0', 'PMAPB0', 'PMAPC0', 'PMAPD0', 'PMAPC0', 'PMAPB0']]],
  [2014, [['BON1A0', 'BON1B0', 'BON1C0', 'BON1D0', 'BON1C0', 'BON1B0']]],
  [2045, [['PVISA0', 'PVISB0']]],
  [2024, [['PINSA0', 'PINSB0', 'PINSC0', 'PINSD0']]],
  [2013, [['SOULA0', 'SOULB0', 'SOULC0', 'SOULD0', 'SOULC0', 'SOULB0']]],
  [2018, [['ARM1A0', 'ARM1B0']]],
  [8,    [['BPAKA0']]],
  [2012, [['MEDIA0']]],
  [2019, [['ARM2A0', 'ARM2B0']]],
  [2025, [['SUITA0']]],
  [2011, [['STIMA0']]],
  [5,    [['BKEYA0', 'BKEYB0']]],
  [13,   [['RKEYA0', 'RKEYB0']]],
  [6,    [['YKEYA0', 'YKEYB0']]],
  [35,   [['CBRAA0']]],
  [2035, [['BAR1A0', 'BAR1B0']]],
  [2028, [['COLUA0']]],
  [46,   [['TREDA0', 'TREDB0', 'TREDC0', 'TREDD0']]],
  [48,   [['ELECA0']]],
  [10,   [['PLAYW0']]],
  [12,   [['PLAYW0']]],
  [34,   [['CANDA0']]],
  [15,   [['PLAYN0']]],
  [24,   [['POL5A0']]],
  [1,    [["PLAYE1"], ["PLAYE2E8"], ["PLAYE3E7"], ["PLAYE4E6"], ["PLAYE5"]]],
  [2,    [["PLAYA1"], ["PLAYA2A8"], ["PLAYA3A7"], ["PLAYA4A6"], ["PLAYA5"]]],
  [3,    [["PLAYB1"], ["PLAYB2B8"], ["PLAYB3B7"], ["PLAYB4B6"], ["PLAYB5"]]],
  [4,    [["PLAYC1"], ["PLAYC2C8"], ["PLAYC3C7"], ["PLAYC4C6"], ["PLAYC5"]]],
]
