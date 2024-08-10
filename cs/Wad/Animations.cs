static class AnimatedTextures {
  static Dictionary<ShortString, ShortString> CreateDict(params (string name, string frames)[] pairs) {
    return (
      from a in pairs
      let frames = a.frames.Split()
      from b in frames.Zip(frames.Skip(1).Append(frames[0]))
      select ((ShortString)(a.name + b.First), (ShortString)(a.name + b.Second))
    ).ToDictionary();
  }
  public static Dictionary<ShortString, ShortString> Walls = CreateDict(
    ("SLADRIP", "1 2 3"),   ("BLODGR",  "1 2 3 4"),  
    ("BLODRIP", "1 2 3 4"), ("FIREBLU", "1 2"),
    ("FIRELAV", "3 A"),     ("FIREMAG", "1 2 3"),   
    ("FIREWAL", "A B L"),   ("GSTFONT", "1 2 3"),   
    ("ROCKRED", "1 2 3"),   ("BFALL",   "1 2 3 4"),
    ("SFALL",   "1 2 3 4"), ("WFALL",   "1 2 3 4"),  
    ("DBRAIN",  "1 2 3 4")
  );
  public static Dictionary<ShortString, ShortString> Flats = CreateDict(
    ("NUKAGE", "1 2 3"),   ("BLOOD",  "1 2 3"),
    ("FWATER", "1 2 3 4"), ("LAVA",   "1 2 3 4"),
    ("RROCK0", "5 6 7 8"), ("SLIME0", "1 2 3 4"),
    ("SLIME0", "5 6 7 8"), ("SLIME",  "09 10 11 12")
  );
}
static class ThingType {
  public record DataEntry (
    (ShortString sprite, bool mirrored)[][] sprites, 
    bool hanging, bool transparent, bool multiangle
  ) {
  }
  static Dictionary<int, DataEntry> CreateDict(params (int type, string data)[] entries) {
    var ret = new Dictionary<int, DataEntry>();
    foreach (var (type, dataSheet) in entries) {
      var data = dataSheet.Split(' ', StringSplitOptions.RemoveEmptyEntries);
      var (prefix, frames, suffixes) = (data[0], data[1], data[2..]);
      bool hanging= prefix.Contains('^');
      bool spectre = prefix.Contains('#');
      prefix = prefix[..4];
      bool multiangle = suffixes.Length != 0;
      var rows = new (ShortString, bool)[frames.Length][];
      if (multiangle) {
        int idx = 0;
        foreach (char ch in frames) {
          var row = rows[idx++] = new (ShortString, bool)[8];
          foreach (string suffix in suffixes) {
            for (int i = 0; i < suffix.Length; i += 2) {
              if (suffix[i] != ch) continue;
              row[suffix[i + 1] - '1'] = (prefix + suffix, i != 0);
            }
          }
        }
      } else {
        for (int i = 0; i < frames.Length; i++) {
          rows[i] = [(prefix + frames[i] + '0', false)];
        }
      }
      ret[type] = new DataEntry(rows, hanging, spectre, multiangle);
    }
    return ret;
  }
  public static Dictionary<int, DataEntry> Data = CreateDict(
    (68,   "BSPI  AB A1D1 A2A8 A3A7 A4A6 A5D5 B1E1 B2B8 B3B7 B4B6 B5E5"),
    (64,   "VILE  AB A1D1 A2D8 A3D7 A4D6 A5D5 A6D4 A7D3 A8D2 B1E1 B2E8 B3E7 B4E6 B5E5 B6E4 B7E3 B8E2"),
    (3003, "BOSS  AB A1 A2A8 A3A7 A4A6 A5 B1 B2B8 B3B7 B4B6 B5"),
    (3005, "HEAD  A  A1 A2A8 A3A7 A4A6 A5"),
    (72,   "KEEN^ A"),
    (16,   "CYBR  AB A1 A2 A3 A4 A5 A6 A7 A8 B1 B2 B3 B4 B5 B6 B7 B8"),
    (3002, "SARG  AB A1 A2A8 A3A7 A4A6 A5 B1 B2B8 B3B7 B4B6 B5"),
    (65,   "CPOS  AB A1 A2 A3 A4 A5 A6 A7 A8 B1 B2 B3 B4 B5 B6 B7 B8"),
    (69,   "BOS2  AB A1C1 A2C8 A3C7 A4C6 A5C5 A6C4 A7C3 A8C2 B1D1 B2D8 B3D7 B4D6 B5D5 B6D4 B7D3 B8D2"),
    (3001, "TROO  AB A1 A2A8 A3A7 A4A6 A5 B1 B2B8 B3B7 B4B6 B5"),
    (3006, "SKUL  AB A1 A8A2 A7A3 A6A4 A5 B1 B8B2 B7B3 B6B4 B5"),
    (67,   "FATT  AB A1 A2A8 A3A7 A4A6 A5 B1 B2B8 B3B7 B4B6 B5"),
    (71,   "PAIN  A  A1 A2A8 A3A7 A4A6 A5"),
    (66,   "SKEL  AB A1D1 A2D8 A3D7 A4D6 A5D5 A6D4 A7D3 A8D2 B1E1 B2E8 B3E7 B4E6 B5E5 B6E4 B7E3 B8E2"),
    (9,    "SPOS  AB A1 A2A8 A3A7 A4A6 A5 B1 B2B8 B3B7 B4B6 B5"),
    (58,   "SARG# AB A1 A2A8 A3A7 A4A6 A5 B1 B2B8 B3B7 B4B6 B5"),
    (7,    "SPID  AB A1D1 A2A8 A3A7 A4A6 A5D5 B1E1 B2B8 B3B7 B4B6 B5E5"),
    (84,   "SSWV  A  A1 A2 A3 A4 A5 A6 A7 A8"),
    (3004, "POSS  AB A1 A2A8 A3A7 A4A6 A5 B1 B2B8 B3B7 B4B6 B5"),

    (2006, "BFUG  A"),
    (2002, "MGUN  A"),
    (2005, "CSAW  A"),
    (2004, "PLAS  A"),
    (2003, "LAUN  A"),
    (2001, "SHOT  A"),
    (82,   "SGN2  A"),
    (2008, "SHEL  A"),
    (2048, "AMMO  A"),
    (2046, "BROK  A"),
    (2049, "SBOX  A"),
    (2007, "CLIP  A"),
    (2047, "CELL  A"),
    (17,   "CELP  A"),
    (2010, "ROCK  A"),

    (2015, "BON2  ABCDCB"),
    (2023, "PSTR  A"),
    (2026, "PMAP  ABCDCB"),
    (2014, "BON1  ABCDCB"),
    (2022, "PINV  ABCD"),
    (2045, "PVIS  AB"),
    (83,   "MEGA  ABCD"),
    (2024, "PINS  ABCD"),
    (2013, "SOUL  ABCDCB"),
    (2018, "ARM1  AB"),
    (8,    "BPAK  A"),
    (2012, "MEDI  A"),
    (2019, "ARM2  AB"),
    (2025, "SUIT  A"),
    (2011, "STIM  A"),
    (5,    "BKEY  AB"),
    (40,   "BSKU  AB"),
    (13,   "RKEY  AB"),
    (38,   "RSKU  AB"),
    (6,    "YKEY  AB"),
    (39,   "YSKU  AB"),
    (47,   "SMIT  A"),
    (70,   "FCAN  ABC"),
    (43,   "TRE1  A"),
    (35,   "CBRA  A"),
    (41,   "CEYE  ABCB"),
    (2035, "BAR1  AB"),
    (28,   "POL2  A"),
    (42,   "FSKU  ABC"),
    (2028, "COLU  A"),
    (53,   "GOR5^ A"),
    (52,   "GOR4^ A"),
    (78,   "HDB6^ A"),
    (75,   "HDB3^ A"),
    (77,   "HDB5^ A"),
    (76,   "HDB4^ A"),
    (50,   "GOR2^ A"),
    (74,   "HDB2^ A"),
    (73,   "HDB1^ A"),
    (51,   "GOR3^ A"),
    (49,   "GOR1^ ABCB"),
    (25,   "POL1  A"),
    (54,   "TRE2  A"),
    (29,   "POL3  AB"),
    (55,   "SMBT  ABCD"),
    (56,   "SMGT  ABCD"),
    (31,   "COL2  A"),
    (36,   "COL5  AB"),
    (57,   "SMRT  ABCD"),
    (33,   "COL4  A"),
    (37,   "COL6  A"),
    (86,   "TLP2  ABCD"),
    (27,   "POL4  A"),
    (44,   "TBLU  ABCD"),
    (45,   "TGRN  ABCD"),
    (30,   "COL1  A"),
    (46,   "TRED  ABCD"),
    (32,   "COL3  A"),
    (48,   "ELEC  A"),
    (85,   "TLMP  ABCD"),
    (26,   "POL6  AB"),
    (10,   "PLAY  W"),
    (12,   "PLAY  W"),
    (34,   "CAND  A"),
    (22,   "HEAD  L"),
    (21,   "SARG  N"),
    (18,   "POSS  L"),
    (19,   "SPOS  L"),
    (20,   "TROO  M"),
    (23,   "SKUL  K"),
    (15,   "PLAY  N"),
    (62,   "GOR5^ A"),
    (60,   "GOR4^ A"),
    (59,   "GOR2^ A"),
    (61,   "GOR3^ A"),
    (63,   "GOR1^ ABCB"),
    (79,   "POB1  A"),
    (80,   "POB2  A"),
    (24,   "POL5  A"),
    (81,   "BRS1  A"),
    (88,   "BBRN A"),
    (1,    "PLAY  E E1 E2E8 E3E7 E4E6 E5"),
    (2,    "PLAY  A A1 A2A8 A3A7 A4A6 A5"),
    (3,    "PLAY  B B1 B2B8 B3B7 B4B6 B5"),
    (4,    "PLAY  C C1 C2C8 C3C7 C4C6 C5")
  );
}
