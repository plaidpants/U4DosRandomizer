﻿using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace U4DosRandomizer
{
    public abstract class WorldMapAbstract : IWorldMap
    {
        protected const string filename = "WORLD.MAP";
        public const int SIZE = 256;
        protected byte[,] _worldMapTiles;

        public abstract void Load(string path, int v, Random random1, Random random2);

        public abstract void Randomize(UltimaData ultimaData, Random random1, Random random2);

        public void Save(string path)
        {
            var file = Path.Combine(path, filename);
            using (var worldFile = new System.IO.BinaryWriter(new System.IO.FileStream(file, System.IO.FileMode.OpenOrCreate)))
            {
                WriteMapToOriginalFormat(worldFile);
            }
        }

        private void WriteMapToOriginalFormat(System.IO.BinaryWriter worldFile)
        {
            int chunkwidth = 32;
            int chunkSize = chunkwidth * chunkwidth;
            byte[] chunk = new byte[chunkSize];

            for (int chunkCount = 0; chunkCount < 64; chunkCount++)
            {
                // Copy the chunk over
                for (int i = 0; i < chunkSize; i++)
                {
                    chunk[i] = _worldMapTiles[i % chunkwidth + chunkCount % 8 * chunkwidth, i / chunkwidth + chunkCount / 8 * chunkwidth];
                }

                worldFile.Write(chunk);
            }
        }

        public static bool IsMatchingTile(ITile coord, List<byte> validTiles)
        {
            return validTiles.Contains(coord.GetTile());
        }

        public static bool IsWalkable(ITile coord)
        {
            return (coord.GetTile() >= TileInfo.Swamp && coord.GetTile() <= TileInfo.Hills) || (coord.GetTile() >= TileInfo.Dungeon_Entrance && coord.GetTile() <= TileInfo.Village);
        }

        public static bool IsWalkableOrSailable(ITile coord)
        {
            return (coord.GetTile() >= TileInfo.Swamp && coord.GetTile() <= TileInfo.Hills) || (coord.GetTile() >= TileInfo.Dungeon_Entrance && coord.GetTile() <= TileInfo.Village) || coord.GetTile() == TileInfo.Deep_Water || coord.GetTile() == TileInfo.Medium_Water;
        }

        public static bool IsGrassOrSailable(ITile coord)
        {
            return coord.GetTile() == TileInfo.Grasslands || coord.GetTile() == TileInfo.Deep_Water || coord.GetTile() == TileInfo.Medium_Water;
        }

        public static bool IsGrass(ITile coord)
        {
            return coord.GetTile() == TileInfo.Grasslands;
        }

        public Tile GetCoordinate(int x, int y)
        {
            return new Tile(x, y, _worldMapTiles, v => Wrap(v));
        }

        public static byte Wrap(int input)
        {
            return Wrap(input, SIZE);
        }

        public static byte Wrap(int input, int divisor)
        {
            return Convert.ToByte((input % divisor + divisor) % divisor);
        }

        public static bool Between(byte x, int v1, int v2)
        {
            if (v1 <= v2)
            {
                return x >= v1 && x <= v2;
            }
            else
            {
                return x >= v1 || x <= v2;
            }

            //return ((v1 <= v2) && (x >= v1 && x <= v2)) || ((v1 > v2) && (x >= v1 || x <= v2));
        }

        public SixLabors.ImageSharp.Image ToImage()
        {
            var image = new SixLabors.ImageSharp.Image<Rgba32>(WorldMapGenerateMap.SIZE, WorldMapGenerateMap.SIZE);
            for (int y = 0; y < WorldMapGenerateMap.SIZE; y++)
            {
                Span<Rgba32> pixelRowSpan = image.GetPixelRowSpan(y);
                for (int x = 0; x < WorldMapGenerateMap.SIZE; x++)
                {
                    if (colorMap.ContainsKey(_worldMapTiles[x, y]))
                    {
                        pixelRowSpan[x] = colorMap[_worldMapTiles[x, y]];
                    }
                    else
                    {
                        pixelRowSpan[x] = SixLabors.ImageSharp.Color.White;
                    }

                }
            }

            return image;
        }

        static private Dictionary<byte, SixLabors.ImageSharp.Color> colorMap = new Dictionary<byte, SixLabors.ImageSharp.Color>()
        {
            {TileInfo.Deep_Water, SixLabors.ImageSharp.Color.FromRgb(0, 0, 112) },
            {TileInfo.Medium_Water, SixLabors.ImageSharp.Color.FromRgb(20,20,112) },
            {TileInfo.Shallow_Water, SixLabors.ImageSharp.Color.FromRgb(60,60,112) },
            {TileInfo.Swamp, SixLabors.ImageSharp.Color.FromRgb(112, 0, 112) },
            {TileInfo.Grasslands, SixLabors.ImageSharp.Color.FromRgb(18, 112+18, 18) },
            {TileInfo.Scrubland, SixLabors.ImageSharp.Color.FromRgb(68, 112+68, 68) },
            {TileInfo.Forest, SixLabors.ImageSharp.Color.FromRgb(108,112+108,108) },
            {TileInfo.Hills, SixLabors.ImageSharp.Color.FromRgb(112+45,112+45,112+45) },
            {TileInfo.Mountains, SixLabors.ImageSharp.Color.FromRgb(112+15,112+15,112+15) },
            {TileInfo.Fire_Field, SixLabors.ImageSharp.Color.Orange },
            {TileInfo.Lava_Flow, SixLabors.ImageSharp.Color.Red },
            //{TileInfo.Slime_2, SixLabors.ImageSharp.Color.Purple },
        };
    }
}