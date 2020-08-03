﻿using System.Collections.Generic;

namespace U4DosRandomizer
{
    public class UltimaData
    {
        public List<Tile> LCB { get; set; }
        public List<Tile> Castles { get; set; }
        public List<Tile> Towns { get; set; }
        public List<Tile> Shrines { get; set; }
        public List<Tile> Moongates { get; set; }
        public List<Tile> Dungeons { get; set; }
        public List<Item> Items { get; set; }

        public UltimaData()
        {
            LCB = new List<Tile>();
            Castles = new List<Tile>();
            Towns = new List<Tile>();
            Shrines = new List<Tile>();
            Moongates = new List<Tile>();
            Dungeons = new List<Tile>();
            Items = new List<Item>();
        }
    }
}