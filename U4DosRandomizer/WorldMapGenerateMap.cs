using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Processing;
using SixLabors.Fonts;
using SimplexNoise;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using U4DosRandomizer.Helpers;
using U4DosRandomizer.Resources;
using SixLabors.ImageSharp.Drawing;

namespace U4DosRandomizer
{
    public class WorldMapGenerateMap : WorldMapAbstract, IWorldMap
    {
        private double[,] _worldMapGenerated;
        private double[,] _clothMapGenerated;

        private byte[,] _mountainOverlay;

        private double _generatedMin;
        private double _generatedMax;
        private double _mountainMin;
        private double _mountainMax;
        private double[,] _mountHeightMap;
        private HashSet<ITile> excludeLocations = new HashSet<ITile>();
        private List<ITile> usedLocations = new List<ITile>();

        private Random randomDownhill;

        private List<Region> Regions;
        private List<ITile> Ocean;
        public static List<River> Rivers { get; internal set; }

        private SpoilerLog SpoilerLog { get; }

        public WorldMapGenerateMap(SpoilerLog spoilerLog)
        {
            SpoilerLog = spoilerLog;

            Regions = new List<Region>();
        }

        private List<Tile> _potentialSwamps = new List<Tile>();
        private List<List<ITile>> _swamps = new List<List<ITile>>();
        private void MapGeneratedMapToUltimaTiles()
        {
            var mapGenerated = _worldMapGenerated;
            var percentInMap = new Dictionary<byte, double>(_percentInMap);

            // Kill shallow water for now... May want to special place that later
            percentInMap[TileInfo.Grasslands] += percentInMap[TileInfo.Shallow_Water];
            percentInMap.Remove(TileInfo.Shallow_Water);
            // Kill Scrubs and forest we are special placing that later
            percentInMap[TileInfo.Grasslands] += percentInMap[TileInfo.Scrubland];
            percentInMap.Remove(TileInfo.Scrubland);
            percentInMap[TileInfo.Grasslands] += percentInMap[TileInfo.Forest];
            percentInMap.Remove(TileInfo.Forest);
            // Kill Mountains and hills too! We are special placing that later. (Maybe change the name of this function...)
            percentInMap[TileInfo.Grasslands] += percentInMap[TileInfo.Mountains];
            percentInMap.Remove(TileInfo.Mountains);
            percentInMap[TileInfo.Grasslands] += percentInMap[TileInfo.Hills];
            percentInMap.Remove(TileInfo.Hills);
            // Lock Lake will get added later and be about 128 tiles
            percentInMap[TileInfo.Grasslands] += (128 / SIZE / SIZE);


            _worldMapTiles = ClampToValuesInSetRatios(mapGenerated, percentInMap, SIZE);
            _clothMapTiles = ClampToValuesInSetRatios(_clothMapGenerated, percentInMap, SIZE * 4);

            // Kill swamps after generation so we can use their placements for later
            for (int x = 0; x < SIZE; x++)
            {
                for (int y = 0; y < SIZE; y++)
                {
                    if (_worldMapTiles[x, y] == TileInfo.Swamp)
                    {
                        var tile = GetCoordinate(x, y);
                        _potentialSwamps.Add(tile);
                        tile.SetTile(TileInfo.Grasslands);
                    }
                }
            }

            for (int x = 0; x < SIZE * 4; x++)
            {
                for (int y = 0; y < SIZE * 4; y++)
                {
                    if (_clothMapTiles[x, y] == TileInfo.Swamp)
                    {
                        _clothMapTiles[x, y] = TileInfo.Grasslands;
                    }
                }
            }

            var scrubNoiseLayerThree = ReadNoiseFromFile(ClothMap.scrubnoise, SIZE);

            // Map the noise to the same number range as the generated world maps so we can put them together
            var targetRangeMin = Double.MaxValue;
            for (int x = 0; x < SIZE * 4; x++)
            {
                for (int y = 0; y < SIZE * 4; y++)
                {
                    if (_clothMapTiles[x, y] == TileInfo.Deep_Water && targetRangeMin > _clothMapGenerated[x, y])
                    {
                        targetRangeMin = _clothMapGenerated[x, y];
                    }
                }
            }
            var targetRangeMax = Double.MinValue;
            for (int x = 0; x < SIZE * 4; x++)
            {
                for (int y = 0; y < SIZE * 4; y++)
                {
                    if (_clothMapTiles[x, y] == TileInfo.Medium_Water && targetRangeMax < _clothMapGenerated[x, y])
                    {
                        targetRangeMax = _clothMapGenerated[x, y];
                    }
                }
            }
            var scrubMin = scrubNoiseLayerThree.Cast<double>().Min();
            var scrubMax = scrubNoiseLayerThree.Cast<double>().Max();
            for (int x = 0; x < SIZE * 4; x++)
            {
                for (int y = 0; y < SIZE * 4; y++)
                {
                    //https://math.stackexchange.com/questions/914823/shift-numbers-into-a-different-range/914843
                    _clothMapGenerated[x, y] = scrubMin + (((scrubMax - scrubMin) / (targetRangeMax - targetRangeMin)) * (_clothMapGenerated[x, y] - targetRangeMin));
                }
            }

            for (int x = 0; x < SIZE * 4; x++)
            {
                for (int y = 0; y < SIZE * 4; y++)
                {
                    _clothMapGenerated[x, y] = _clothMapGenerated[x, y] + (scrubNoiseLayerThree[x % SIZE, y % SIZE] * 0.1);
                }
            }

            var newClothMapTiles = ClampToValuesInSetRatios(_clothMapGenerated, percentInMap, SIZE * 4);
            for(int x = 0; x < SIZE*4; x++)
            {
                for(int y = 0; y < SIZE*4; y++)
                {
                    if(IsWater(newClothMapTiles[x,y]) && IsWater(_clothMapTiles[x,y]))
                    {
                        _clothMapTiles[x, y] = newClothMapTiles[x, y];
                    }
                }
            }

            return;
        }

        private static byte[,] ClampToValuesInSetRatios(double[,] mapGenerated, Dictionary<byte, double> percentInMap, int size)
        {
            var worldMapFlattened = new double[size * size];

            for (int x = 0; x < size; x++)
            {
                for (int y = 0; y < size; y++)
                {
                    worldMapFlattened[x + y * size] = mapGenerated[x, y];
                }
            }

            //var min = worldMapFlattened.Min();
            //var max = worldMapFlattened.Max();
            var worldMapFlattenedList = worldMapFlattened.ToList();
            worldMapFlattenedList.Sort();

            var rangeList = new List<Tuple<byte, double, double>>();
            var lowerBound = worldMapFlattened.Min() - 1;
            var upperBound = 0.0;
            var percentSum = 0.0;
            foreach (var key in percentInMap)
            {
                percentSum += key.Value;
                var index = Convert.ToInt32(Math.Floor((worldMapFlattenedList.Count - 1) * percentSum));
                upperBound = worldMapFlattenedList[index];
                rangeList.Add(new Tuple<byte, double, double>(key.Key, lowerBound, upperBound));
                lowerBound = upperBound;
            }
            var last = rangeList.Last();
            rangeList[rangeList.Count - 1] = new Tuple<byte, double, double>(last.Item1, last.Item2, worldMapFlattened.Max());

            var worldMapUlt = new byte[size, size];
            for (int x = 0; x < size; x++)
            {
                for (int y = 0; y < size; y++)
                {
                    // Smush it down to the number of tile types we want
                    //int res = Convert.ToInt32(Linear(worldMapDS[x, y], min, max, 0, percentInMap.Count));
                    byte res = 99;
                    foreach (var range in rangeList)
                    {
                        var value = mapGenerated[x, y];
                        if (mapGenerated[x, y] > range.Item2 && mapGenerated[x, y] <= range.Item3)
                        {
                            res = range.Item1;
                            break;
                        }
                    }

                    // Spread it back out so we can see some color differences
                    //res = Convert.ToInt32(Linear(res, 0, percentInMap.Count, 0, 255));
                    //Color newColor = Color.FromArgb(res, res, res);
                    worldMapUlt[x, y] = res;
                }
            }

            return worldMapUlt;
        }

        internal static void Restore(string path)
        {
            var file = System.IO.Path.Combine(path, filename);
            FileHelper.Restore(file);
        }

        private Tuple<byte[,], byte[,], double[,], byte[,]>  MountainMap(Random random)
        {
            var seed = random.Next();
            var mountainNoiseFloatLayerOne = SeamlessSimplexNoise.simplexnoise(seed, SIZE*4, SIZE*4, 0.0f, 0.8f);
            seed = random.Next();
            var mountainNoiseFloatLayerTwo = SeamlessSimplexNoise.simplexnoise(seed, SIZE*4, SIZE*4, 0.2f, 1.6f);
            //seed = random.Next();
            //var mountainNoiseFloatLayerThree = SeamlessSimplexNoise.simplexnoise(seed, SIZE, SIZE, 0.2f, 51.2f/4);

            var avgOne = mountainNoiseFloatLayerOne.Cast<float>().Average();
            var avgTwo = mountainNoiseFloatLayerOne.Cast<float>().Average();


            var mountainNoiseLayerOne = Float2dToDouble2d(mountainNoiseFloatLayerOne, SIZE*4);
            var mountainNoiseLayerTwo = Float2dToDouble2d(mountainNoiseFloatLayerTwo, SIZE*4);
            //var mountainNoiseLayerThree = Float2dToDouble2d(mountainNoiseFloatLayerThree, SIZE);
            //WriteNoiseToFile("mountainnoise", mountainNoiseLayerThree);
            var mountainNoiseLayerThree = ReadNoiseFromFile(ClothMap.mountainnoise, SIZE);

            //for(int x = 0; x < SIZE; x++)
            //{
            //    for(int y = 0; y < SIZE; y++)
            //    {
            //        if(mountainNoiseLayerThree[x,y] != readNoise[x,y])
            //        {
            //            throw new Exception("Ya dun messed up.");
            //        }
            //    }
            //}

            // 1 - abs of noise
            var mountainNoise = new double[SIZE*4, SIZE*4];
            for (int x = 0; x < SIZE*4; x++)
            {
                for (int y = 0; y < SIZE*4; y++)
                {
                    mountainNoise[x, y] = (1 - Math.Abs(mountainNoiseLayerOne[x, y] - avgOne)) + ((1 - Math.Abs(mountainNoiseLayerTwo[x, y] - avgTwo)) * 0.3);
                }
            }


            var totalGrass = _percentInMap[TileInfo.Mountains] + _percentInMap[TileInfo.Hills] + _percentInMap[TileInfo.Scrubland] + _percentInMap[TileInfo.Forest] + _percentInMap[TileInfo.Swamp] + _percentInMap[TileInfo.Shallow_Water] + _percentInMap[TileInfo.Grasslands];
            // As we will be overlaying it on the grass, scrubland and forest so bump up the percentage so the ratio stays correct
            var hillsPercent = _percentInMap[TileInfo.Hills] / totalGrass;
            var mountainsPercent = _percentInMap[TileInfo.Mountains] / totalGrass;
            var percentInMap = new Dictionary<byte, double>()
            {
                {TileInfo.Grasslands,(1.0-hillsPercent)-mountainsPercent},
                {TileInfo.Hills,hillsPercent},
                {TileInfo.Mountains,mountainsPercent }
            };

            var clothMapSized = ClampToValuesInSetRatios(mountainNoise, percentInMap, SIZE * 4);

            // Get something with slightly larger hills and mountains so the hill overlay doesn't line up exactly with the hills
            mountainsPercent = mountainsPercent * 1.05;
            hillsPercent = hillsPercent * 1.8;
            var percentInMapWithLargerMountainsForOverlay = new Dictionary<byte, double>()
            {
                {TileInfo.Grasslands,(1.0-hillsPercent)-mountainsPercent},
                {TileInfo.Hills,hillsPercent},
                {TileInfo.Mountains,mountainsPercent }
            };
            var mountainOverlay = ClampToValuesInSetRatios(mountainNoise, percentInMapWithLargerMountainsForOverlay, SIZE * 4);

            // Shrink down to the size of the in game map
            var mapSized = new byte[SIZE, SIZE];
            for (int x = 0; x < SIZE; x++)
            {
                for (int y = 0; y < SIZE; y++)
                {
                    mapSized[x, y] = clothMapSized[x * 4, y * 4];
                }
            }

            var mapSizedMountains = new double[SIZE, SIZE];
            for (int x = 0; x < SIZE; x++)
            {
                for (int y = 0; y < SIZE; y++)
                {
                    mapSizedMountains[x, y] = mountainNoise[x * 4, y * 4];
                }
            }

            // Now that we're done making the other stuff rough up the edges of the hills in the cloth map
            for (int x = 0; x < SIZE * 4; x++)
            {
                for (int y = 0; y < SIZE * 4; y++)
                {
                    mountainNoise[x, y] = mountainNoise[x, y] + (mountainNoiseLayerThree[x%SIZE, y%SIZE] * 0.025);
                }
            }
            clothMapSized = ClampToValuesInSetRatios(mountainNoise, percentInMap, SIZE * 4);

            return new Tuple<byte[,], byte[,], double[,], byte[,]>(mapSized, clothMapSized, mapSizedMountains, mountainOverlay);
        }

        private void WriteNoiseToFile(string path, double[,] output)
        {
            using (FileStream file = File.Create(path))
            {
                using (BinaryWriter writer = new BinaryWriter(file))
                {
                    foreach (double value in output)
                    {
                        writer.Write(value);
                    }
                }
            }
        }

        private double[,] ReadNoiseFromFile(byte[] data, int size)
        {
            var result = new double[size, size];

            var idx = 0;
            using (var file = new MemoryStream(data))
            {
                using (BinaryReader reader = new BinaryReader(file))
                {
                    while (idx < size * size)
                    {
                        var read = reader.ReadDouble();
                        result[idx / size, idx % size] = read;
                        idx++;
                    }
                }
            }

            return result;
        }

        private Tuple<byte[,],byte[,]> ScrubMap(Random random)
        {
            var seed = random.Next();
            var scrubNoiseFloatLayerOne = SeamlessSimplexNoise.simplexnoise(seed, SIZE * 4, SIZE * 4, 0.0f, 3.2f);
            seed = random.Next();
            var scrubNoiseFloatLayerTwo = SeamlessSimplexNoise.simplexnoise(seed, SIZE * 4, SIZE * 4, 0.2f, 6.4f);
            //seed = random.Next();
            //var scrubNoiseFloatLayerThree = SeamlessSimplexNoise.simplexnoise(seed, SIZE, SIZE, 0.0f, 51.2f/4);


            var scrubNoiseLayerOne = Float2dToDouble2d(scrubNoiseFloatLayerOne, SIZE * 4);
            var scrubNoiseLayerTwo = Float2dToDouble2d(scrubNoiseFloatLayerTwo, SIZE * 4);
            //var scrubNoiseLayerThree = Float2dToDouble2d(scrubNoiseFloatLayerThree, SIZE);
            //WriteNoiseToFile("scrubnoise", scrubNoiseLayerThree);
            var scrubNoiseLayerThree = ReadNoiseFromFile(ClothMap.scrubnoise, SIZE);

            var scrubNoise = new double[SIZE * 4, SIZE * 4];
            for (int x = 0; x < SIZE * 4; x++)
            {
                for (int y = 0; y < SIZE * 4; y++)
                {
                    scrubNoise[x, y] = scrubNoiseLayerOne[x, y] + (scrubNoiseLayerTwo[x, y] * 0.5);
                }
            }

            var totalGrass = _percentInMap[TileInfo.Scrubland] + _percentInMap[TileInfo.Forest] + _percentInMap[TileInfo.Swamp] + _percentInMap[TileInfo.Shallow_Water] + _percentInMap[TileInfo.Grasslands];
            // As we will be overlaying it on the grass so bump up the percentage so the ratio stays correct
            var scrubPercent = _percentInMap[TileInfo.Scrubland] / totalGrass;
            var forestPercent = _percentInMap[TileInfo.Forest] / totalGrass;
            var percentInMap = new Dictionary<byte, double>()
            {
                {TileInfo.Grasslands,(1.0-scrubPercent)-forestPercent},
                {TileInfo.Scrubland,scrubPercent},
                {TileInfo.Forest,forestPercent }
            };

            var clothMapSized = ClampToValuesInSetRatios(scrubNoise, percentInMap, SIZE*4);
            var mapSized = new byte[SIZE, SIZE];
            for (int x = 0; x < SIZE; x++)
            {
                for(int y = 0; y < SIZE; y++)
                {
                    mapSized[x, y] = clothMapSized[x * 4, y * 4];
                }
            }

            for (int x = 0; x < SIZE * 4; x++)
            {
                for (int y = 0; y < SIZE * 4; y++)
                {
                    scrubNoise[x, y] = scrubNoise[x, y] + (scrubNoiseLayerThree[x%SIZE, y%SIZE] * 0.2);
                }
            }
            clothMapSized = ClampToValuesInSetRatios(scrubNoise, percentInMap, SIZE * 4);

            return new Tuple<byte[,], byte[,]>(mapSized, clothMapSized);
        }

        private static double[,] Float2dToDouble2d(float[,] floatArray, int size)
        {
            var result = new double[size, size];
            for (int x = 0; x < size; x++)
            {
                for (int y = 0; y < size; y++)
                {
                    result[x, y] = floatArray[x, y];
                }
            }

            return result;
        }

        public void ScrubTest(Random random)
        {
            //_worldMapTiles = ScrubMap(random);
        }

        //public void SwampTest(Random random)
        //{
        //    _worldMapTiles = new byte[SIZE, SIZE];
        //    for (int x = 0; x < SIZE; x++)
        //    {
        //        for (int y = 0; y < SIZE; y++)
        //        {
        //            _worldMapTiles[x, y] = TileInfo.Grasslands;
        //        }
        //    }

        //    for (int i = 0; i < 23; i++)
        //    {
        //        _potentialSwamps.Add(GetCoordinate(random.Next(0, SIZE), random.Next(0, SIZE)));
        //    }

        //    AddSwamp(random);

        //    //var swampSize = 16;
        //    //var chosenSwampTile = GetCoordinate(SIZE/2, SIZE/2);
        //    //var swamp = SwampMap(random, swampSize);

        //    //for (int x = 0; x < swampSize; x++)
        //    //{
        //    //    for (int y = 0; y < swampSize; y++)
        //    //    {
        //    //        var tile = GetCoordinate(chosenSwampTile.X - swampSize / 2 + x, chosenSwampTile.Y - swampSize / 2 + y);
        //    //        if (tile.GetTile() == TileInfo.Grasslands)
        //    //        {
        //    //            tile.SetTile(swamp[x, y]);
        //    //        }
        //    //    }
        //    //}
        //}

        internal bool IsTile(int x, int y, int tile)
        {
            return _worldMapTiles[Wrap(x), Wrap(y)] == tile;
        }

        public override void Load(string path, int mapSeed, int mapGeneratorSeed, int otherRandomSeed, UltimaData ultimaData)
        {
            var mapGeneratorRandom = new Random(mapGeneratorSeed);
            var clothMapGeneratorRandom = new Random(mapGeneratorSeed);
            var randomMap = new Random(otherRandomSeed);
            randomDownhill = new Random(randomMap.Next());
            var file = System.IO.Path.Combine(path, filename);

            FileHelper.TryBackupOriginalFile(file);

            // The roughness does almost nothing for us since we squish it down into our tile space
            double magicRoughness = 184643518.256878;
            _worldMapGenerated = new DiamondSquare(SIZE, magicRoughness, mapSeed).getData(mapGeneratorRandom);
            _clothMapGenerated = new DiamondSquare(SIZE*4, magicRoughness, mapSeed).getData(clothMapGeneratorRandom);
            _generatedMin = _worldMapGenerated.Cast<double>().Min();
            _generatedMax = _worldMapGenerated.Cast<double>().Max();
            MapGeneratedMapToUltimaTiles();

            CleanupAndAddFeatures(randomMap);

            Center();

            ApplyRegions(ultimaData, randomMap);
        }


        private void ApplyRegions(UltimaData ultimaData, Random random)
        {
            var runicMap = new Dictionary<string, char>()
            {
                { "TH", '\u00C4' }, //�
                { "EE", '\u00C1' }, //�
                { "EA", '\u00C0' } //�
            };

            var bodiesOfWater = FindBodies(tile => tile.GetTile() == TileInfo.Deep_Water || tile.GetTile() == TileInfo.Medium_Water).OrderByDescending(b => b.Count());

            var waterEnumerator = bodiesOfWater.GetEnumerator();
            if(waterEnumerator.MoveNext())
            {
                Ocean = waterEnumerator.Current;
            }

            // Lock Lake already done in AddLockLake()
            //if (waterEnumerator.MoveNext())
            //{
            //    Regions.Add(new Region
            //    {
            //        Name = "Lock Lake",
            //        RunicName = "Lock Lake",
            //        Tiles = waterEnumerator.Current,
            //        Center = GetCenterOfRegion(waterEnumerator.Current)
            //    });
            //}

            var forests = FindBodies(tile => tile.GetTile() == TileInfo.Forest).OrderByDescending(b => b.Count());

            var forestEnumerator = forests.GetEnumerator();

            // See font here to get the unique characters https://www.dafont.com/ultima-runes.font
            if (forestEnumerator.MoveNext())
            {
                Regions.Add(new Region
                {
                    Name = "The Deep Forest",
                    RunicName = $"{runicMap["TH"]}e D{runicMap["EE"]}p Forest",
                    Tiles = forestEnumerator.Current,
                    Center = GetCenterOfRegion(forestEnumerator.Current)
                });
            }

            if (forestEnumerator.MoveNext())
            {
                Regions.Add(new Region
                {
                    Name = "Spiritwood",
                    RunicName = "Spiritwood",
                    Tiles = forestEnumerator.Current,
                    Center = GetCenterOfRegion(forestEnumerator.Current)
                });
            }

            var islandsAndContinents = FindBodies(tile => tile.GetTile() != TileInfo.Deep_Water && tile.GetTile() != TileInfo.Medium_Water && tile.GetTile() != TileInfo.Shallow_Water).OrderByDescending(b => b.Count());

            var islandEnumerator = islandsAndContinents.GetEnumerator();
            while (islandEnumerator.MoveNext() && islandEnumerator.Current.Count > 800) ;
            if (islandEnumerator.Current != null && islandEnumerator.Current.Count <= 800)
            {
                Regions.Add(new Region
                {
                    Name = "Verity Isle",
                    RunicName = "Verity Isle",
                    Tiles = islandEnumerator.Current,
                    Center = GetCenterOfRegion(islandEnumerator.Current)
                });
            }

            if (islandEnumerator.MoveNext())
            {
                Regions.Add(new Region
                {
                    Name = "Dagger Isle",
                    RunicName = "Dagger Isle",
                    Tiles = islandEnumerator.Current,
                    Center = GetCenterOfRegion(islandEnumerator.Current)
                });
            }

            if (islandEnumerator.MoveNext())
            {
                Regions.Add(new Region
                {
                    Name = "Isle of Deeds",
                    RunicName = $"Isle of D{runicMap["EE"]}ds",
                    Tiles = islandEnumerator.Current,
                    Center = GetCenterOfRegion(islandEnumerator.Current)
                });
            }

            if (islandEnumerator.MoveNext())
            {
                Regions.Add(new Region
                {
                    Name = "Valarian Isle",
                    RunicName = "Valarian Isle",
                    Tiles = islandEnumerator.Current,
                    Center = GetCenterOfRegion(islandEnumerator.Current)
                });
            }

            var plains = FindPlains(random, ultimaData).OrderByDescending(b => b.Count());
            var plainsEnumerator = plains.GetEnumerator();
            if (plainsEnumerator.MoveNext())
            {
                Regions.Add(new Region
                {
                    Name = "The High Stepes",
                    RunicName = $"{runicMap["TH"]}e High Stepes",
                    Tiles = plainsEnumerator.Current,
                    Center = GetCenterOfRegion(plainsEnumerator.Current)
                });
            }

            if (plainsEnumerator.MoveNext())
            {
                Regions.Add(new Region
                {
                    Name = "Bloody Plains",
                    RunicName = "Bloody Plains",
                    Tiles = plainsEnumerator.Current,
                    Center = GetCenterOfRegion(plainsEnumerator.Current)
                });
            }

            var swampsEnumerator = _swamps.OrderByDescending(b => b.Count()).GetEnumerator();
            if (swampsEnumerator.MoveNext())
            {
                Regions.Add(new Region
                {
                    Name = "Fens of the Dead",
                    RunicName = $"Fens of the D{runicMap["EA"]}d",
                    Tiles = swampsEnumerator.Current,
                    Center = GetCenterOfRegion(swampsEnumerator.Current)
                });
            }
        }

        public new Region FindNearestRegion(ICoordinate targetTile, UltimaData data, out IList<ITile> outPath)
        {
            Region nearestRegion = null;
            var bestDistanceToRegion = int.MaxValue;
            IList<ITile> bestPath = null;
            foreach (var region in Regions)
            {
                var path = Search.GetPath(SIZE, SIZE, region.Tiles, c => c.Equals(targetTile), c => { return true; });
                if(path.Count < bestDistanceToRegion)
                {
                    nearestRegion = region;
                    bestDistanceToRegion = path.Count;
                    bestPath = path;
                }
            }
            outPath = bestPath;
            return nearestRegion;
        }

        private System.Drawing.Point GetCenterOfRegion(List<ITile> region)
        {
            var centerOfRegion = new System.Drawing.Point(0, 0);

            for (int i = 0; i < region.Count; i++)
            {
                centerOfRegion.X += region[i].X;
                centerOfRegion.Y += region[i].Y; 
            }
            centerOfRegion.X = centerOfRegion.X / region.Count;
            centerOfRegion.Y = centerOfRegion.Y / region.Count;

            var closestToCenter = region.OrderBy(r => DistanceSquared(GetCoordinate(centerOfRegion.X, centerOfRegion.Y), r)).First();
            centerOfRegion.X = closestToCenter.X;
            centerOfRegion.Y = closestToCenter.Y;

            return centerOfRegion;
        }

        private void Center()
        {
            var bestScore = 0;
            var bestOffsets = new List<System.Drawing.Point>();

            for(int xOffset = 0; xOffset < SIZE; xOffset++)
            {
                for(int yOffset = 0; yOffset < SIZE; yOffset++)
                {
                    var currentScore = 0;
                    for (int x = 0; x < SIZE; x++)
                    {
                        if(IsWater(_worldMapTiles[Wrap(x+xOffset),Wrap(0+yOffset)]))
                        {
                            currentScore++;
                        }

                        if (IsWater(_worldMapTiles[Wrap(x + xOffset), Wrap(SIZE - 1 + yOffset)]))
                        {
                            currentScore++;
                        }
                    }

                    for (int y = 0; y < SIZE; y++)
                    {
                        if (IsWater(_worldMapTiles[Wrap(0 + xOffset), Wrap(y + yOffset)]))
                        {
                            currentScore++;
                        }

                        if (IsWater(_worldMapTiles[Wrap(SIZE - 1 + xOffset), Wrap(y + yOffset)]))
                        {
                            currentScore++;
                        }
                    }
                    if (currentScore > bestScore)
                    {
                        bestScore = currentScore;
                        bestOffsets.Clear();
                        bestOffsets.Add(new System.Drawing.Point(xOffset, yOffset));
                    }
                    else if (currentScore == bestScore)
                    {
                        bestOffsets.Add(new System.Drawing.Point(xOffset, yOffset));
                    }
                }
            }

            var bestOffset = bestOffsets[bestOffsets.Count / 2];
            var newWorldMap = new byte[SIZE, SIZE];

            for(int x = 0; x < SIZE; x++)
            {
                for(int y = 0; y < SIZE; y++)
                {
                    newWorldMap[x, y] = _worldMapTiles[Wrap(bestOffset.X + x), Wrap(bestOffset.Y + y)];
                }
            }
            _worldMapTiles = newWorldMap;

            var newClothMap = new byte[SIZE*4, SIZE*4];
            for (int x = 0; x < SIZE*4; x++)
            {
                for (int y = 0; y < SIZE*4; y++)
                {
                    newClothMap[x, y] = _clothMapTiles[WrapInt(bestOffset.X*4 + x, SIZE*4), WrapInt(bestOffset.Y*4 + y, SIZE*4)];
                }
            }
            _clothMapTiles = newClothMap;

            var newMountainOverlay = new byte[SIZE * 4, SIZE * 4];
            for (int x = 0; x < SIZE * 4; x++)
            {
                for (int y = 0; y < SIZE * 4; y++)
                {
                    newMountainOverlay[x, y] = _mountainOverlay[WrapInt(bestOffset.X * 4 + x, SIZE * 4), WrapInt(bestOffset.Y * 4 + y, SIZE * 4)];
                }
            }
            _mountainOverlay = newMountainOverlay;

            foreach(var swamp in _swamps)
            {
                for(int i = 0; i < swamp.Count(); i++)
                {
                    swamp[i] = GetCoordinate(swamp[i].X - bestOffset.X, swamp[i].Y - bestOffset.Y);
                }
            }

            foreach(var region in Regions)
            {
                var offsetTiles = new List<ITile>();
                foreach(var tile in region.Tiles)
                {
                    offsetTiles.Add(GetCoordinate(tile.X - bestOffset.X, tile.Y - bestOffset.Y));
                }
                region.Tiles = offsetTiles;
                region.Center = GetCenterOfRegion(offsetTiles);
            }

            foreach(var river in Rivers)
            {
                river.LevelOrderTraversal(tile => tile.Coordinate = GetCoordinate(tile.Coordinate.X - bestOffset.X, tile.Coordinate.Y - bestOffset.Y));
            }
        }

        public override void Randomize(UltimaData ultimaData, Random randomLocations, Random randomItems, Random randomLocations2)
        {
            
            RandomizeLocations(ultimaData, randomLocations, randomLocations2);

            RandomizeItems(ultimaData, randomItems);

            //var plains = FindPlains(randomLocations, ultimaData);

            //for(int i = 0; i < plains.Count; i++)
            //{
            //    var plain = plains[i];
            //    foreach(var tile in plain)
            //    {
            //        tile.SetTile((byte)(TileInfo.A + i));
            //    }
            //}

            //var plains = _swamps;
            //for (int i = 0; i < plains.Count; i++)
            //{
            //    var plain = plains[i];
            //    foreach (var tile in plain)
            //    {
            //        tile.SetTile((byte)(TileInfo.A + i));
            //    }
            //}

            for (int i = 0; i < Regions.Count; i++)
            {
                foreach (var tile in Regions[i].Tiles)
                {
                    //_worldMapTiles[tile.X, tile.Y] = (byte)(TileInfo.A + i);
                }
            }

            WriteSpoilerLog(ultimaData);
        }

        private void WriteSpoilerLog(UltimaData data)
        {
            SpoilerLog.Add(SpoilerCategory.MiscWorldLocation, $"Pirate Cove at {Talk.GetSextantText(data.PirateCoveSpawnTrigger, ' ')}");
            for (int i = 0; i < data.LOC_HUMILITY - 1; i++)
            {
                SpoilerLog.Add(SpoilerCategory.Location, $"{data.LocationNames[i]} at {Talk.GetSextantText(data.GetLocation(i), ' ')}");
            }
            for (int i = 0; i < 8; i++)
            {
                SpoilerLog.Add(SpoilerCategory.Location, $"Moongate at {Talk.GetSextantText(data.Moongates[i])}");
            }
            for (int i = 0; i < data.Items.Count; i++)
            {
                SpoilerLog.Add(SpoilerCategory.Location, $"{data.ItemNames[i]} at {Talk.GetSextantText(data.Items[i], ' ')}");
            }
        }


        private void RandomizeLocations(UltimaData ultimaData, Random random, Random random2)
        {
            // Lay down Stygian Abyss first so it doesn't stomp on other things
            // TODO: Make the entrance to the Abyss more random instead of laying down what is in the base game
            // Find a reasonable mountainous area
            var avatarIsleSizeX = 40 + 12;
            var avatarIsleSizeY = 80 + 12;
            var possibleLocations = GetAllMatchingTiles(c => AreaIsAll(TileInfo.Deep_Water, avatarIsleSizeX, avatarIsleSizeY, c, false));
            var stygianUpperLeft = possibleLocations[random.Next(0, possibleLocations.Count)];

            var stygian = GetCoordinate(stygianUpperLeft.X + 20 + 5, stygianUpperLeft.Y + 60 + 4);

            // Get a path from the entrance to water
            //var entranceToStygian = GetCoordinate(stygian.X - 14, stygian.Y - 9);
            //var entrancePathToWater = worldMap.GetRiverPath(entranceToStygian, c => { return c.GetTile() == TileInfo.Deep_Water; } );

            var shapeLoc = new Coordinate(stygianUpperLeft.X + 6, stygianUpperLeft.Y + 6);
            ApplyShape(shapeLoc, "abyss", false);

            //var entrancePathToWater = Search.GetPath(SIZE, SIZE, entranceToStygian,
            //    c => { return ocean.Contains(c); }, // Find deep water to help make sure a boat can reach here. TODO: Make sure it reaches the ocean.
            //    c => { return !(Between(c.X, Wrap(shapeLoc.X - 12), Wrap(shapeLoc.X + 12)) && Between(c.Y, Wrap(shapeLoc.Y - 12), Wrap(shapeLoc.Y + 12))); },
            //    GoDownhillHueristic);

            //for (int i = 0; i < entrancePathToWater.Count; i++)
            //{
            //    GetCoordinate(entrancePathToWater[i].X, entrancePathToWater[i].Y).SetTile(TileInfo.Medium_Water);
            //}

            for (int x = 0; x <= avatarIsleSizeX; x++)
            {
                for (int y = 0; y <= avatarIsleSizeY; y++)
                {
                    excludeLocations.Add(GetCoordinate(stygianUpperLeft.X + x, stygianUpperLeft.Y + y));
                }
            }

            var lockLake = Regions.Where(r => r.Name == "Lock Lake").FirstOrDefault();

            //Pirate Cove - Set locations based off Stygian location
            var originalX = 0xe9; // Original Stygian location
            var originalY = 0xe9;
            for (var i = 0; i < ultimaData.PirateCove.Count; i++)
            {
                ultimaData.PirateCove[i].X = Convert.ToByte(Wrap(ultimaData.PirateCove[i].X - originalX + stygian.X));
                ultimaData.PirateCove[i].Y = Convert.ToByte(Wrap(ultimaData.PirateCove[i].Y - originalY + stygian.Y));
            }
            ultimaData.PirateCoveSpawnTrigger = new Coordinate(ultimaData.PirateCoveSpawnTrigger.X - originalX + stygian.X, ultimaData.PirateCoveSpawnTrigger.Y - originalY + stygian.Y);
            //worldMap.GetCoordinate(ultimaData.PirateCoveSpawnTrigger.X, ultimaData.PirateCoveSpawnTrigger.Y).SetTile(TileInfo.A);

            // Blink Exclusion
            // Cast exclusion isn't precise enough so allow them to cast anywhere and exclude the destination
            ultimaData.BlinkCastExclusionX1 = 0x01;
            ultimaData.BlinkCastExclusionX2 = 0x01;
            ultimaData.BlinkCastExclusionY1 = 0x01;
            ultimaData.BlinkCastExclusionY2 = 0x01;
            ultimaData.BlinkDestinationExclusionX1 = Convert.ToByte(Wrap(stygianUpperLeft.X));
            ultimaData.BlinkDestinationExclusionY1 = Convert.ToByte(Wrap(stygianUpperLeft.Y));
            ultimaData.BlinkDestinationExclusionX2 = Convert.ToByte(Wrap(stygianUpperLeft.X + avatarIsleSizeX));
            ultimaData.BlinkDestinationExclusionY2 = Convert.ToByte(Wrap(stygianUpperLeft.Y + avatarIsleSizeY));

            //for (int x = 0; x < WorldMap.SIZE; x++)
            //{
            //    for (int y = 0; y < WorldMap.SIZE; y++)
            //    {
            //        if (WorldMap.Between(Convert.ToByte(x), ultimaData.BlinkExclusionX1, ultimaData.BlinkExclusionX2)
            //            && WorldMap.Between(Convert.ToByte(y), ultimaData.BlinkExclusionY1, ultimaData.BlinkExclusionY2))
            //        {
            //            worldMap.GetCoordinate(x, y).SetTile(TileInfo.Lava_Flow);
            //        }
            //    }
            //}

            // Buildings
            possibleLocations = GetAllMatchingTiles(WorldMapGenerateMap.IsWalkableGround);
            possibleLocations.RemoveAll(c => excludeLocations.Contains(c));
            List<ITile> evenlyDistributedLocations = GetEvenlyDistributedValidLocations(random, 8, usedLocations, possibleLocations, ultimaData, false);
            possibleLocations = possibleLocations.Except(evenlyDistributedLocations).ToList();

            // Towns
            ITile loc = null;
            for (int i = 0; i < 7; i++)
            {
                loc = RandomSelectFromListCheckPathChangeAndRemove(random, evenlyDistributedLocations, TileInfo.Town);
                ultimaData.Towns[i].X = loc.X;
                ultimaData.Towns[i].Y = loc.Y;
                usedLocations.Add(ultimaData.Towns[i]);
            }
            loc = RandomSelectFromListCheckPathChangeAndRemove(random, evenlyDistributedLocations, TileInfo.Ruins); // Magincia
            ultimaData.Towns[7].X = loc.X;
            ultimaData.Towns[7].Y = loc.Y;

            // Castles
            var numLocations = 3 + ultimaData.Castles.Count;
            evenlyDistributedLocations = GetEvenlyDistributedValidLocations(random, numLocations, usedLocations, possibleLocations, ultimaData, true);
            possibleLocations = possibleLocations.Except(evenlyDistributedLocations).ToList();
            for (int i = 0; i < 3; i++)
            {
                loc = RandomSelectFromListCheckPathChangeAndRemove(random, evenlyDistributedLocations, TileInfo.Castle);
                ultimaData.Castles[i].X = loc.X;
                ultimaData.Castles[i].Y = loc.Y;
            }

            // Villages
            for (int i = 0; i < 3; i++)
            {
                loc = RandomSelectFromListCheckPathChangeAndRemove(random, evenlyDistributedLocations, TileInfo.Village);
                ultimaData.Towns[i + 8].X = loc.X;
                ultimaData.Towns[i + 8].Y = loc.Y;
            }
            // Cove
            //possibleLocations = GetAllMatchingTiles(c => c.GetTile() == TileInfo.Mountains);
            //possibleLocations = GetAllMatchingTiles(c => AreaIsAll(TileInfo.Mountains, 5, c));
            possibleLocations = GetAllMatchingTiles(c => AreaIsAll(IsNotWater, 5, 5, c, false) && AreaIsAll(c => c.GetTile() == TileInfo.Mountains, 4, 4, c, false));
            evenlyDistributedLocations = GetEvenlyDistributedValidLocations(random, 80, usedLocations, possibleLocations, ultimaData, false);
            //possibleLocations = possibleLocations.Except(evenlyDistributedLocations).ToList();

            var closestToLocke = evenlyDistributedLocations.OrderBy(c => DistanceSquared(c, GetCoordinate(lockLake.Center.X, lockLake.Center.Y))).FirstOrDefault();
            loc = closestToLocke;
            var cove = GetCoordinate(loc.X + 3, loc.Y + 1);
            ultimaData.Towns[11].X = Wrap(cove.X);
            ultimaData.Towns[11].Y = Wrap(cove.Y);
            ApplyShape(GetCoordinate(loc.X, loc.Y), "cove", false);
            if (IsWater(GetCoordinate(lockLake.Center.X, lockLake.Center.Y).GetTile()))
            {
                ultimaData.WhirlpoolExit = new Coordinate(lockLake.Center.X, lockLake.Center.Y);
            }
            else
            {
                var lakeTiles = new List<ITile>(lockLake.Tiles);
                lakeTiles.Shuffle(random);
                ultimaData.WhirlpoolExit = new Coordinate(lakeTiles[0].X, lakeTiles[0].Y);
            }

            var headOfCoveRiver = GetCoordinate(loc.X + 1, loc.Y + 3);
            headOfCoveRiver.SetTile(TileInfo.Grasslands);
            
            int count = 0;
            byte[,] worldMapCache = new byte[SIZE, SIZE];
            for (int x = 0; x < SIZE; x++)
            {
                for (int y = 0; y < SIZE; y++)
                {
                    worldMapCache[x, y] = _worldMapTiles[x, y];
                }
            }
            var notAllowedForRiverTile = new List<ITile>() {
                GetCoordinate(cove.X-1, cove.Y-1),
                GetCoordinate(cove.X-1, cove.Y),
                GetCoordinate(cove.X-1, cove.Y+1),
                GetCoordinate(cove.X, cove.Y-1),
                GetCoordinate(cove.X, cove.Y),
                GetCoordinate(cove.X, cove.Y+1),
                GetCoordinate(cove.X+1, cove.Y-1),
                GetCoordinate(cove.X+1, cove.Y),
                GetCoordinate(cove.X+1, cove.Y+1),
            };
            
            ITile riverMouth = null;
            int riverLength = 0;
            Tuple<int, int> direction = null;

            var bodiesOfWater = FindBodies(tile => tile.GetTile() == TileInfo.Deep_Water || tile.GetTile() == TileInfo.Medium_Water).OrderByDescending(b => b.Count()).ToList();
            var bodiesOfWaterIncludingRiversAndBridges = FindBodies(tile => IsWater(tile) || tile.GetTile() == TileInfo.Bridge ).OrderByDescending(b => b.Count()).ToList();
            var lockLakeAndAttachedRivers = bodiesOfWaterIncludingRiversAndBridges.Single( b => b.Any( c => lockLake.Tiles.Contains(c)));
            
            var lockLakeTiles = new List<ITile>(lockLakeAndAttachedRivers);

            var attemptedLockLakeTiles = new List<ITile>();
            River river = null;
            River bestRiver = null;
            var shortestCheatPathToRiverCount = Int32.MaxValue;
            var shortestCheatPathToRiver = new List<ITile>();
            while (shortestCheatPathToRiverCount != 0 && count < 2048 && lockLakeTiles.Count > 0)
            {
                // Every 16 attempts try a new river mouth
                if (count % 16 == 0)
                {
                    var covePath = Search.GetPath(SIZE, SIZE, headOfCoveRiver,
                                    c => { return lockLakeTiles.Contains(c); }, // Find a spot in Lock Lake
                                    c => { return !attemptedLockLakeTiles.Contains(c); } // Any tile is fine just getting a starting point and a distance
                                    );

                    if(covePath.Count() == 0)
                    {
                        break;
                    }
                    riverMouth = covePath[covePath.Count() - 1];
                    riverLength = covePath.Count() - 2;
                    direction = new Tuple<int, int>(covePath[covePath.Count() - 2].X - riverMouth.X, covePath[covePath.Count() - 2].Y - riverMouth.Y);

                    lockLakeTiles.Remove(riverMouth);
                    attemptedLockLakeTiles.Add(riverMouth);
                }

                // Try a new river
                var currentNode = new RiverNode()
                {
                    Coordinate = riverMouth,
                    Parent = null,
                    Children = new List<RiverNode>(),
                    depth = 0
                };
                //RiverTributary(random, currentNode, direction, riverLength, TileInfo.A, c => { return (IsNotWater(c) || removedLockLakeTiles.Contains(c) || lockLakeTiles.Contains(c)) && !notAllowedForRiverTile.Contains(c); });
                RiverTributary(random, currentNode, direction, riverLength, TileInfo.A, c => { return IsNotWater(c) && !notAllowedForRiverTile.Contains(c); });
                river = new River()
                {
                    Tree = currentNode,
                    Direction = direction
                };
                //Rivers.Add(river);
                var riverCoords = new List<ITile>();
                river.LevelOrderTraversal(n =>
                {
                    n.Coordinate.SetTile(TileInfo.Shallow_Water);
                    //n.Coordinate.SetClothTile(TileInfo.Shallow_Water);
                    riverCoords.Add(n.Coordinate);
                });
                currentNode.Coordinate.SetTile(TileInfo.Medium_Water);

                var pathFromHeadToRiver = Search.GetPath(SIZE, SIZE, headOfCoveRiver,
                    c => { return riverCoords.Contains(c); },
                    c => { return !notAllowedForRiverTile.Contains(c); });

                if(pathFromHeadToRiver.Count > 0)
                {
                    foreach(var tile in pathFromHeadToRiver)
                    {
                        tile.SetTile(TileInfo.Medium_Water);
                    }
                }

                if (pathFromHeadToRiver.Count < shortestCheatPathToRiverCount)
                {
                    bestRiver = river;
                    shortestCheatPathToRiverCount = pathFromHeadToRiver.Count;
                    shortestCheatPathToRiver = pathFromHeadToRiver;
                }

                // Undo previous river
                for (int x = 0; x < SIZE; x++)
                {
                    for (int y = 0; y < SIZE; y++)
                    {
                        _worldMapTiles[x, y] = worldMapCache[x, y];
                    }
                }

                //Console.WriteLine($"CovePath: {coveRiverPath.Count}");
                count++;
            }

            if (bestRiver != null)
            {
                Rivers.Add(bestRiver);
                bestRiver.LevelOrderTraversal(n =>
                {
                    n.Coordinate.SetTile(TileInfo.Shallow_Water);
                });
                foreach (var tile in shortestCheatPathToRiver)
                {
                    tile.SetTile(TileInfo.Medium_Water);
                }

                var coveRiverPath = Search.GetPath(SIZE, SIZE, cove,
                                    c => { return lockLake.Tiles.Contains(c); }, // Find a spot in Lock Lake
                                    c => { return (IsWalkable(c) || IsWater(c) || c.GetTile() == TileInfo.Bridge) && !Ocean.Contains(c); },
                                    (c, cf, b) => { return IsWater(c) || c.GetTile() == TileInfo.Bridge ? 0 : 50; }); // Follow the river where able
                coveRiverPath.ForEach(c => { if (IsWater(c) || c.GetTile() == TileInfo.Bridge) { c.SetTile(TileInfo.Medium_Water); } });
            }
            else
            {
                // We couldn't get the lake connected. Give up and have the boat pop up in Cove.
                headOfCoveRiver.SetTile(TileInfo.Deep_Water);
                ultimaData.WhirlpoolExit = new Coordinate(headOfCoveRiver.X, headOfCoveRiver.Y);
            }

            //for (int k = 0; k < coveRiver.Count; k++)
            //{
            // //   GetCoordinate(coveRiver[k].X, coveRiver[k].Y).SetTile(TileInfo.Medium_Water);
            //}

            //loc.SetTile(TileInfo.A);


            // Clear the forests around towns
            var clearAreas = new List<ITile>();
            clearAreas.AddRange(ultimaData.Towns);
            clearAreas.RemoveAt(11); // Cove can hide
            clearAreas.RemoveAt(7); // Magincia can hide
            clearAreas.AddRange(ultimaData.LCB);
            clearAreas.AddRange(ultimaData.Castles);
            foreach(var clearing in clearAreas)
            {
                var grass = clearing.NeighborCoordinates().ToList();
                grass.Add(new Tile(clearing.X - 1, clearing.Y - 1, _worldMapTiles, v => Wrap(v)));
                grass.Add(new Tile(clearing.X + 1, clearing.Y + 1, _worldMapTiles, v => Wrap(v)));

                foreach(var tile in grass)
                {
                    if(tile.GetTile() == TileInfo.Forest)
                    {
                        tile.SetTile(TileInfo.Grasslands);
                    }
                }

                var scrub = clearing.NeighborAndAdjacentCoordinates().SelectMany(x => x.NeighborCoordinates());
                foreach (var tile in scrub)
                {
                    if (tile.GetTile() == TileInfo.Forest)
                    {
                        tile.SetTile(TileInfo.Scrubland);
                    }
                }
            }

            // Shrines
            numLocations = ultimaData.Shrines.Count - 1;
            // Get a place by the water
            var possibleShrineLocations = GetAllMatchingTiles(t => WorldMapGenerateMap.IsWalkableGround(t) && t.NeighborAndAdjacentCoordinates().Any(n => IsWater(n.GetTile())));
            possibleShrineLocations.RemoveAll(c => excludeLocations.Contains(c));
            // Get places far from eachother
            evenlyDistributedLocations = GetEvenlyDistributedValidLocations(random, numLocations, usedLocations, possibleShrineLocations, ultimaData, true);
            possibleLocations = possibleLocations.Except(evenlyDistributedLocations).ToList();
            for (int i = 0; i < 7; i++)
            {
                if (i == 6)
                {
                    // Unchanged spot for spirit
                }
                else
                {
                    //Get the place closest to its town
                    loc = evenlyDistributedLocations.OrderBy(x => DistanceSquared(x, ultimaData.Towns[i])).First();
                    evenlyDistributedLocations.Remove(loc);
                    loc.SetTile(TileInfo.Shrine);
                    ultimaData.Shrines[i].X = loc.X;
                    ultimaData.Shrines[i].Y = loc.Y;
                }
            }
            // Humility
            // TODO: Shrine prettier
            //possibleLocations = GetAllMatchingTiles(c => GoodForHumility(c));
            //possibleLocations.RemoveAll(c => excludeLocations.Contains(c));
            //loc = possibleLocations[random.Next(0, possibleLocations.Count)];
            loc = GetCoordinate(stygianUpperLeft.X + 23, stygianUpperLeft.Y + 47);
            loc.SetTile(TileInfo.Shrine);
            ultimaData.Shrines[7].X = loc.X;
            ultimaData.Shrines[7].Y = loc.Y;
            // Base game has spirit in same spot as humility
            ultimaData.Shrines[6].X = loc.X;
            ultimaData.Shrines[6].Y = loc.Y;
            //for (int y = -4; y < 0; y++)
            //{
            //    GetCoordinate(loc.X, loc.Y + y).SetTile(TileInfo.Hills);
            //}
            ultimaData.DaemonSpawnLocationX = loc.X;
            ultimaData.DaemonSpawnX1 = Wrap(loc.X - 1);
            ultimaData.DaemonSpawnX2 = Wrap(loc.X + 1);
            ultimaData.DaemonSpawnY1 = Wrap(loc.Y - 4);
            ultimaData.DaemonSpawnY2 = Wrap(loc.Y + 1);
            //ultimaData.BlinkDestinationExclusion2X1 = ultimaData.DaemonSpawnX1;
            //ultimaData.BlinkDestinationExclusion2X2 = ultimaData.DaemonSpawnX2;
            //ultimaData.BlinkDestinationExclusion2Y1 = ultimaData.DaemonSpawnY1;
            //ultimaData.BlinkDestinationExclusion2Y2 = ultimaData.DaemonSpawnY2;

            // Moongates
            List<ITile> path = new List<ITile>();
            List<byte> validTiles = new List<byte>() { TileInfo.Grasslands, TileInfo.Scrubland, TileInfo.Swamp, TileInfo.Forest, TileInfo.Hills };
            var moongateIndices = new List<int>() { 0, 1, 2, 3, 4, 5, 6, 7 };
            moongateIndices.Shuffle(random2);
            for (int i = 0; i < 8; i++)
            {
                path = new List<ITile>();
                var distance = random.Next(5, 10);
                while (path.Count == 0 && distance > 0)
                {
                    path = Search.GetPath(SIZE, SIZE, ultimaData.Towns[i],
                        // Move at least 9 spaces away from from the entrance
                        c => { return distance * distance <= DistanceSquared(c, ultimaData.Towns[i]) && IsWalkable(c); },
                        // Only valid if all neighbors all also mountains
                        c => { return IsMatchingTile(c, validTiles); },
                        (c, cf, b) => { return (float)random.NextDouble(); });
                    if (path.Count == 0)
                    {
                        Console.WriteLine($"Failed Moongate placement of {i} placement. Retrying.");
                        distance--;
                    }
                    else
                    {
                        loc = path[path.Count - 1];
                        loc.SetTile(TileInfo.Grasslands);
                        foreach (var n in loc.NeighborCoordinates())
                        {
                            if (validTiles.Contains(n.GetTile()))
                            {
                                n.SetTile(TileInfo.Grasslands);
                            }
                        }
                        possibleLocations.Remove(loc);
                        ultimaData.Moongates[moongateIndices[i]].X = loc.X;
                        ultimaData.Moongates[moongateIndices[i]].Y = loc.Y;
                        excludeLocations.Add(ultimaData.Moongates[i]);
                    }
                }
                if (distance == 0)
                {
                    Console.WriteLine($"Utterly failed at Moongate placement of {i} placement. Trying random.");
                    possibleLocations = GetAllMatchingTiles(IsGrass);
                    possibleLocations.RemoveAll(c => excludeLocations.Contains(c));

                    loc = RandomSelectFromListCheckPathChangeAndRemove(random, possibleLocations, TileInfo.Grasslands);
                    ultimaData.Moongates[i].X = loc.X;
                    ultimaData.Moongates[i].Y = loc.Y;
                }
            }

            // LCB
            var placed = false;
            while (!placed)
            {
                var lcb = GetRandomCoordinate(random);
                var lcbEntrance = GetCoordinate(lcb.X, lcb.Y + 1);
                Tile lcbWestSide = GetCoordinate(lcb.X - 1, lcb.Y);
                Tile lcbEastSide = GetCoordinate(lcb.X + 1, lcb.Y);

                path = new List<ITile>();
                if (IsWalkableGround(lcb) && IsWalkableGround(lcbEntrance) && !excludeLocations.Contains(lcb) && !excludeLocations.Contains(lcbWestSide) && !excludeLocations.Contains(lcbEastSide))
                {
                    path = Search.GetPath(SIZE, SIZE, lcbEntrance,
                        // Gotta be able to walk to a Moongate from LCB
                        c => { return ultimaData.Moongates.Contains(c); },
                        c => { return IsWalkable(c) && c != lcbEastSide && c != lcbWestSide && c != lcbEastSide; },
                        (c, cf, b) => { return (float)random.NextDouble(); });
                    if (path.Count > 0)
                    {
                        lcb.SetTile(TileInfo.Lord_British_s_Castle_Entrance);
                        ultimaData.LCB[0].X = lcb.X;
                        ultimaData.LCB[0].Y = lcb.Y;
                        lcbWestSide.SetTile(TileInfo.Lord_British_s_Caste_West);
                        ultimaData.LCB[1].X = lcbWestSide.X;
                        ultimaData.LCB[1].Y = lcbWestSide.Y;
                        lcbEastSide.SetTile(TileInfo.Lord_British_s_Castle_East);
                        ultimaData.LCB[2].X = lcbEastSide.X;
                        ultimaData.LCB[2].Y = lcbEastSide.Y;

                        placed = true;
                    }
                }
            }

            // Dungeons
            possibleLocations = GetAllMatchingTiles(c => c.GetTile() == TileInfo.Mountains && IsWalkableGround(GetCoordinate(c.X, c.Y + 1)) && Search.GetPath(SIZE, SIZE, c,
            coord => { return IsGrass(coord) || coord.GetTile() == TileInfo.Deep_Water; },
            IsWalkableOrSailable).Count > 0);
            possibleLocations.RemoveAll(c => excludeLocations.Contains(c));
            evenlyDistributedLocations = GetEvenlyDistributedValidLocations(random, 8, usedLocations, possibleLocations, ultimaData, false);
            possibleLocations = possibleLocations.Except(evenlyDistributedLocations).ToList();
            for (int i = 0; i < 6; i++)
            {
                loc = evenlyDistributedLocations.OrderBy(x => DistanceSquared(x, ultimaData.Towns[i])).First();
                evenlyDistributedLocations.Remove(loc);
                loc.SetTile(TileInfo.Dungeon_Entrance);
                ultimaData.Dungeons[i].X = loc.X;
                ultimaData.Dungeons[i].Y = loc.Y;
            }

            // Check if the path to the dungeon is blocked by a lake and make the lake connect to the ocean (or Lock Lake)
            for (int i = 0; i < 6; i++)
            {
                loc = ultimaData.Dungeons[i];
                path = Search.GetPath(SIZE, SIZE, loc,
                        // To Moongate
                        c => { return ultimaData.Moongates.Contains(c); },
                        c => { return IsWalkable(c); });
                if (path == null || path.Count == 0)
                {
                    path = Search.GetPath(SIZE, SIZE, loc,
                        // To Moongate
                        c => { return ultimaData.Moongates.Contains(c); },
                        c => { return IsWalkableOrSailable(c); },
                        (c, cf, b) =>
                        {
                            if (IsWater(c))
                            { return 50; }
                            else
                            { return 0; }
                        });

                    for (int j = 0; j < path.Count; j++)
                    {
                        if (IsWater(path[j]) && !Ocean.Contains(path[j]))
                        {
                            var entrancePathToWater = Search.GetPath(SIZE, SIZE, path[j],
                                c => { return Ocean.Contains(c) || lockLake.Tiles.Contains(c); }, // Find deep water to help make sure a boat can reach here. 
                                c => { return c.GetTile() < TileInfo.Mountains; },
                                // Follow water as long as you can then head downhill and away from the dungeon
                                (c, cf, b) => { if (IsWater(c)) { return 0.0f; } else { return GoDownhillHueristic(c, cf, b) + (float)Math.Sqrt(DistanceSquared(c, loc)) / SIZE; } }); 

                            for (int k = 0; k < entrancePathToWater.Count; k++)
                            {
                                GetCoordinate(entrancePathToWater[k].X, entrancePathToWater[k].Y).SetTile(TileInfo.Medium_Water);
                            }
                            break;
                        }
                    }
                }
            }

            // special for Hythloth
            // TODO: Hythloth prettier
            possibleLocations = GetAllMatchingTiles(c => AreaIsAll(TileInfo.Mountains, 4, c));

            path = new List<ITile>();
            while (path.Count == 0)
            {
                loc = possibleLocations[random.Next(0, possibleLocations.Count)];
                possibleLocations.Remove(loc);
                path = Search.GetPath(SIZE, SIZE, loc,
                    // Move at least 9 spaces away from from the entrance
                    c => { return 9 * 9 <= DistanceSquared(c, loc); },
                    // Only valid if all neighbors all also mountains
                    c => { return c.GetTile() == TileInfo.Mountains && c.NeighborAndAdjacentCoordinates().All(n => n.GetTile() == TileInfo.Mountains); },
                    GoDownhillHueristic);
                if (path.Count == 0)
                {
                    Console.WriteLine("Failed Hythloth placement. Retrying.");
                }
            }
            for (int i = 0; i < 3; i++)
            {
                path[i].SetTile(TileInfo.Grasslands);
            }
            for (int i = 3; i < path.Count; i++)
            {
                path[i].SetTile(TileInfo.Hills);
            }
            loc.SetTile(TileInfo.Dungeon_Entrance);
            //for(int i = 0; i < path.Count; i++)
            //{
            //    path[i].SetTile(WorldMap.Wrap( TileInfo.A + i));
            //}
            ultimaData.Dungeons[6].X = loc.X;
            ultimaData.Dungeons[6].Y = loc.Y;
            ultimaData.BalloonSpawn = path.Last();

            // Stygian Abyss
            ultimaData.Dungeons[7].X = stygian.X;
            ultimaData.Dungeons[7].Y = stygian.Y;

            // Move starting positions and abyss ejection locations to Towns
            for (int i = 0; i < 8; i++)
            {
                var validPositions = GetPathableTilesNear(ultimaData.Towns[i], 3, IsWalkable);
                loc = validPositions[random.Next(0, validPositions.Count)];
                ultimaData.StartingPositions[i].X = loc.X;
                ultimaData.StartingPositions[i].Y = loc.Y;
                ultimaData.AbyssEjectionLocations[i].X = loc.X;
                ultimaData.AbyssEjectionLocations[i].Y = loc.Y;
            }

            // More ejection locations for Castles
            for (int i = 0; i < 3; i++)
            {
                var validPositions = GetPathableTilesNear(ultimaData.Castles[i], 3, IsWalkable);
                loc = validPositions[random.Next(0, validPositions.Count)];
                ultimaData.AbyssEjectionLocations[i + 8].X = loc.X;
                ultimaData.AbyssEjectionLocations[i + 8].Y = loc.Y;
            }

            // Ejection for LCB
            ultimaData.AbyssEjectionLocations[11].X = ultimaData.LCB[0].X;
            ultimaData.AbyssEjectionLocations[11].Y = (byte)(ultimaData.LCB[0].Y + 1);

            // Ejection for Abyss
            ultimaData.AbyssEjectionLocations[12].X = stygian.X;
            ultimaData.AbyssEjectionLocations[12].Y = stygian.Y;

            //// Whirlpool exits in Lock Lake
            //if (lockLake != null)
            //{
            //    loc = lockLake.Tiles[random.Next(0, lockLake.Tiles.Count - 1)];
            //}
            //else
            //{
            //    throw new Exception("No Lock Lake found.");
            //}

            return;
        }

        public bool IsWalkableOrOcean(ITile coord)
        {
            return (coord.GetTile() >= TileInfo.Swamp && coord.GetTile() <= TileInfo.Hills) || (coord.GetTile() >= TileInfo.Dungeon_Entrance && coord.GetTile() <= TileInfo.Village) || ((coord.GetTile() == TileInfo.Deep_Water || coord.GetTile() == TileInfo.Medium_Water) && Ocean.Contains(coord));
        }

        private void RandomizeItems(UltimaData ultimaData, Random random)
        {
            ITile loc = null;// RandomizeLocation(random, TileInfo.Swamp, worldMap, WorldMap.IsWalkableGround, exclude);
            var possibleLocations = GetAllMatchingTiles(IsWalkableGround);
            possibleLocations.RemoveAll(c => excludeLocations.Contains(c));
            List<ITile> evenlyDistributedLocations = GetEvenlyDistributedValidLocations(random, 2, usedLocations, possibleLocations, ultimaData, true);
            possibleLocations = possibleLocations.Except(evenlyDistributedLocations).ToList();

            loc = RandomSelectFromListCheckPathChangeAndRemove(random, evenlyDistributedLocations, TileInfo.Swamp);
            ultimaData.Items[ultimaData.ITEM_MANDRAKE].X = loc.X;
            ultimaData.Items[ultimaData.ITEM_MANDRAKE].Y = loc.Y;
            excludeLocations.Add(loc);

            loc = RandomSelectFromListCheckPathChangeAndRemove(random, evenlyDistributedLocations, null);
            ultimaData.Items[ultimaData.ITEM_HORN].X = loc.X;
            ultimaData.Items[ultimaData.ITEM_HORN].Y = loc.Y;
            excludeLocations.Add(loc);

            possibleLocations = GetAllMatchingTiles(c => c.GetTile() == TileInfo.Swamp);
            possibleLocations.RemoveAll(c => excludeLocations.Contains(c));
            evenlyDistributedLocations = GetEvenlyDistributedValidLocations(random, 1, usedLocations, possibleLocations, ultimaData, true);
            possibleLocations = possibleLocations.Except(evenlyDistributedLocations).ToList();
            loc = RandomSelectFromListCheckPathChangeAndRemove(random, evenlyDistributedLocations, TileInfo.Swamp);
            ultimaData.Items[ultimaData.ITEM_MANDRAKE2].X = loc.X;
            ultimaData.Items[ultimaData.ITEM_MANDRAKE2].Y = loc.Y;
            excludeLocations.Add(loc);

            possibleLocations = GetAllMatchingTiles(c => c.GetTile() == TileInfo.Forest);
            possibleLocations.RemoveAll(c => excludeLocations.Contains(c));
            evenlyDistributedLocations = GetEvenlyDistributedValidLocations(random, 2, usedLocations, possibleLocations, ultimaData, true);
            possibleLocations = possibleLocations.Except(evenlyDistributedLocations).ToList();
            loc = RandomSelectFromListCheckPathChangeAndRemove(random, evenlyDistributedLocations, TileInfo.Forest);
            ultimaData.Items[ultimaData.ITEM_NIGHTSHADE].X = loc.X;
            ultimaData.Items[ultimaData.ITEM_NIGHTSHADE].Y = loc.Y;
            excludeLocations.Add(loc);

            loc = RandomSelectFromListCheckPathChangeAndRemove(random, evenlyDistributedLocations, TileInfo.Forest);
            ultimaData.Items[ultimaData.ITEM_NIGHTSHADE2].X = loc.X;
            ultimaData.Items[ultimaData.ITEM_NIGHTSHADE2].Y = loc.Y;
            excludeLocations.Add(loc);

            possibleLocations = GetAllMatchingTiles(c => AreaIsAll(TileInfo.Deep_Water, 14, c) && !excludeLocations.Contains(c));
            evenlyDistributedLocations = GetEvenlyDistributedValidLocations(random, 1, usedLocations, possibleLocations, ultimaData, true);
            possibleLocations = possibleLocations.Except(evenlyDistributedLocations).ToList();
            loc = evenlyDistributedLocations[0];
            ultimaData.Items[ultimaData.ITEM_SKULL].X = loc.X;
            ultimaData.Items[ultimaData.ITEM_SKULL].Y = loc.Y;
            excludeLocations.Add(loc);
            // Shape for the skull location is slightly off so shift it by one here as a hack
            ApplyShape(GetCoordinate(loc.X+1, loc.Y), "skull");
            

            possibleLocations = GetAllMatchingTiles(c => AreaIsAll(TileInfo.Deep_Water, 7, c) && !excludeLocations.Contains(c));
            evenlyDistributedLocations = GetEvenlyDistributedValidLocations(random, 1, usedLocations, possibleLocations, ultimaData, true);
            possibleLocations = possibleLocations.Except(evenlyDistributedLocations).ToList();
            loc = evenlyDistributedLocations[0];
            ultimaData.Items[ultimaData.ITEM_BELL].X = loc.X;
            ultimaData.Items[ultimaData.ITEM_BELL].Y = loc.Y;
            ApplyShape(loc, "bell");
            excludeLocations.Add(loc);

            // TODO Put in ocean
            possibleLocations = GetAllMatchingTiles(c => c.GetTile() == TileInfo.Deep_Water && !excludeLocations.Contains(c));
            evenlyDistributedLocations = GetEvenlyDistributedValidLocations(random, 1, usedLocations, possibleLocations, ultimaData, true);
            possibleLocations = possibleLocations.Except(evenlyDistributedLocations).ToList();
            loc = evenlyDistributedLocations[0];
            ultimaData.Items[ultimaData.ITEM_WHEEL].X = loc.X;
            ultimaData.Items[ultimaData.ITEM_WHEEL].Y = loc.Y;

            // TODO: Do I move the black stone?
            ultimaData.Items[ultimaData.ITEM_BLACK_STONE].X = ultimaData.Moongates[0].X;
            ultimaData.Items[ultimaData.ITEM_BLACK_STONE].Y = ultimaData.Moongates[0].Y;

            // White stone
            possibleLocations = GetAllMatchingTiles(c => AreaIsAll(TileInfo.Mountains, 4, c) && !excludeLocations.Contains(c));
            loc = possibleLocations[random.Next(0, possibleLocations.Count)];
            ultimaData.Items[ultimaData.ITEM_WHITE_STONE].X = Convert.ToByte(loc.X - 1);
            ultimaData.Items[ultimaData.ITEM_WHITE_STONE].Y = loc.Y;
            ApplyShape(loc, "white");
        }

        public void CleanupAndAddFeatures(Random random)
        {
            AddMountainsAndHills(random);
            // Original game only had single tiles in very special circumstances
            RemoveSingleTiles();

            var lockLake = AddLockLake(random);
            Regions.Add(lockLake);

            Rivers = AddRivers(random);

            // Original game only had single tiles in very special circumstances
            RemoveSingleTiles();

            AddBridges(random, Rivers);
            AddScrubAndForest(random, Rivers);
            AddSwamp(random);

        }

        //public void WriteHashes(string path)
        //{
        //    var file = Path.Combine(path, filename);

        //    var worldHash = new Dictionary<string, string>();

        //    var hash = HashHelper.GetHashSha256(file);
        //    Console.WriteLine($"{file}: {HashHelper.BytesToString(hash)}");
        //    worldHash.Add(Path.GetFileName(file), HashHelper.BytesToString(hash));

        //    string json = JsonConvert.SerializeObject(worldHash); // the dictionary is inside client object
        //                                                             //write string to file
        //    System.IO.File.WriteAllText(@"world_hash.json", json);
        //}

        private void RemoveSingleTiles()
        {
            for (int x = 0; x < SIZE; x++)
            {
                for (int y = 0; y < SIZE; y++)
                {
                    var adjacentTiles = new byte[] { _worldMapTiles[Wrap(x + 1), y], _worldMapTiles[Wrap(x - 1), y], _worldMapTiles[x, Wrap(y + 1)], _worldMapTiles[x, Wrap(y - 1)] };
                    if (!adjacentTiles.Contains(_worldMapTiles[x, y]))
                    {
                        var mostUsedAdjacentTile = (from item in adjacentTiles
                                                    group item by item into g
                                                    orderby g.Count() descending
                                                    select g.Key).First();
                        _worldMapTiles[x, y] = mostUsedAdjacentTile;
                    }
                }
            }

            return;
        }

        private void AddSwamp(Random random)
        {
            // 23
            int totalNumOfSwamps = 19 + random.Next(1, 3) + random.Next(1, 3);

            for (int i = 0; i < totalNumOfSwamps; i++)
            {
                var swampSize = 16;
                var chosenSwampTile = _potentialSwamps[random.Next(0, _potentialSwamps.Count() - 1)];
                var swamp = SwampMap(random, swampSize);

                var swampList = new List<ITile>();
                for (int x = 0; x < swampSize; x++)
                {
                    for (int y = 0; y < swampSize; y++)
                    {
                        var tile = GetCoordinate(chosenSwampTile.X - swampSize / 2 + x, chosenSwampTile.Y - swampSize / 2 + y);
                        if (tile.GetTile() == TileInfo.Grasslands || tile.GetTile() == TileInfo.Scrubland)
                        {
                            if (swamp.Item1[x, y] == TileInfo.Swamp)
                            {
                                tile.SetTile(swamp.Item1[x, y]);
                                swampList.Add(tile);
                            }
                        }
                    }
                }
                _swamps.Add(swampList);

                for (int x = 0; x < swampSize*4; x++)
                {
                    for (int y = 0; y < swampSize*4; y++)
                    {
                        var tileX = WrapInt(chosenSwampTile.X * 4 - swampSize * 4 / 2 + x,SIZE*4);
                        var tileY = WrapInt(chosenSwampTile.Y * 4 - swampSize * 4 / 2 + y,SIZE*4);
                        if (_clothMapTiles[tileX, tileY] == TileInfo.Grasslands || _clothMapTiles[tileX, tileY] == TileInfo.Scrubland)
                        {
                            if (swamp.Item2[x, y] == TileInfo.Swamp)
                            {
                                _clothMapTiles[tileX, tileY] = swamp.Item2[x, y];
                            }
                        }
                    }
                }
            }

            return;
        }

        private static Tuple<byte[,], byte[,]> SwampMap(Random random, int swampSize)
        {
            SimplexNoise.Noise.Seed = random.Next();
            var swampNoiseFloat = SimplexNoise.Noise.Calc2D(swampSize*4, swampSize*4, 0.1f / 4f);
            var swampNoise = Float2dToDouble2d(swampNoiseFloat, swampSize*4);

            var percentInMap = new Dictionary<byte, double>()
                {
                    {TileInfo.Grasslands,0.7},
                    {TileInfo.Swamp,0.3}
                };

            double halfSwampSize = Convert.ToDouble(swampSize*4) / 2;
            for (int x = 0; x < swampSize*4; x++)
            {
                for (int y = 0; y < swampSize*4; y++)
                {
                    swampNoise[x, y] = swampNoise[x, y]
                        * (-Math.Pow(((x - halfSwampSize) / halfSwampSize), 2) + 1)
                        * (-Math.Pow(((y - halfSwampSize) / halfSwampSize), 2) + 1);
                }
            }

            var clothMapSizedSwamp = ClampToValuesInSetRatios(swampNoise, percentInMap, swampSize * 4);
            var mapSized = new byte[swampSize, swampSize];
            for (int x = 0; x < swampSize; x++)
            {
                for (int y = 0; y < swampSize; y++)
                {
                    mapSized[x, y] = clothMapSizedSwamp[x * 4, y * 4];
                }
            }

            return new Tuple<byte[,], byte[,]>(mapSized, clothMapSizedSwamp);
        }

        private Region AddLockLake(Random random)
        {
            var lakeRegion = new Region()
            {
                Name = "Lock Lake",
                RunicName = "Lock Lake",
                Tiles = new List<ITile>()
            };
            var lockLakeSize = 16;
            var lockLake = LakeMap(random, lockLakeSize);
            var grassLocations = GetAllMatchingTiles(c => { return c.GetTile() == TileInfo.Grasslands; });

            grassLocations.Shuffle(random);

            var possibleCoveLocations = GetAllMatchingTiles(c => AreaIsAll(IsNotWater, 5, 5, c, false) && AreaIsAll(c => c.GetTile() == TileInfo.Mountains, 4, 4, c, false));

            var chosenLakeLocation = grassLocations.First( c => { 

            // Find how far away the mountain range is
            var pathToMountain = Search.GetPath(SIZE, SIZE, c,
                    c => possibleCoveLocations.Contains(c),
                    IsNotWater);

                // If it isn't too close or too far away
                if (pathToMountain != null && (pathToMountain.Count() > 16 && pathToMountain.Count() < 19))
                {
                    // If we can place it without overlapping the ocean or another body of water
                    // Need to look one space away too to not abut the ocean
                    for(int x = 0; x < lockLakeSize; x++)
                    {
                        for(int y = 0; y < lockLakeSize; y++)
                        {
                            var coord = GetCoordinate(c.X - lockLakeSize / 2 + x, c.Y - lockLakeSize / 2 + y);
                            if (lockLake.Item1[x, y] != TileInfo.Grasslands)
                            {
                                if (IsWater(coord))
                                {
                                    return false;
                                }
                                foreach(var neighbor in coord.NeighborAndAdjacentCoordinates())
                                {
                                    if (IsWater(neighbor))
                                    {
                                        return false;
                                    }
                                }
                            }
                        }
                    }
                }
                else
                {
                    return false;
                }

                return true;
            });

            for (int x = 0; x < lockLakeSize; x++)
            {
                for (int y = 0; y < lockLakeSize; y++)
                {
                    if (lockLake.Item1[x, y] != TileInfo.Grasslands)
                    {
                        var coord = GetCoordinate(chosenLakeLocation.X - lockLakeSize / 2 + x, chosenLakeLocation.Y - lockLakeSize / 2 + y);
                        coord.SetTile(lockLake.Item1[x, y]);
                        lakeRegion.Tiles.Add(coord);
                    }
                }
            }

            lakeRegion.Center = GetCenterOfRegion(lakeRegion.Tiles);

            for (int x = 0; x < lockLakeSize * 4; x++)
            {
                for (int y = 0; y < lockLakeSize * 4; y++)
                {
                    if (lockLake.Item2[x, y] != TileInfo.Grasslands)
                    {
                        var tileX = WrapInt(chosenLakeLocation.X * 4 - lockLakeSize * 4 / 2 + x, SIZE * 4);
                        var tileY = WrapInt(chosenLakeLocation.Y * 4 - lockLakeSize * 4 / 2 + y, SIZE * 4);
                        // Lock Lake has Deep Water that doesn't show up on the map
                        _clothMapTiles[tileX, tileY] = lockLake.Item2[x, y] == TileInfo.Deep_Water ? TileInfo.Shallow_Water : lockLake.Item2[x, y];
                    }
                }
            }

            return lakeRegion;
        }

        static public double Linear(double x, double x0, double x1, double y0, double y1)
        {
            if ((x1 - x0) == 0)
            {
                return (y0 + y1) / 2;
            }
            return y0 + (x - x0) * (y1 - y0) / (x1 - x0);
        }

        private static Tuple<byte[,], byte[,]> LakeMap(Random random, int lakeSize)
        {
            SimplexNoise.Noise.Seed = random.Next();
            var lakeNoiseFloat = SimplexNoise.Noise.Calc2D(lakeSize * 4, lakeSize * 4, 0.1f / 4f);
            var lakeNoise = Float2dToDouble2d(lakeNoiseFloat, lakeSize * 4);
            var min = lakeNoise.Cast<double>().Min();
            var max = lakeNoise.Cast<double>().Max();

            for(int x = 0; x < lakeSize*4; x++)
            {
                for(int y = 0; y < lakeSize*4; y++)
                {
                    var lerpX = Linear(Math.Abs(x- (lakeSize * 4 / 2)), 0, (lakeSize * 4/2), max, min);
                    var lerpY = Linear(Math.Abs(y - (lakeSize * 4 / 2)), 0, (lakeSize * 4/2), max, min);
                    lakeNoise[x, y] = (lerpX + lerpY + (3* lakeNoise[x,y]));
                }
            }

            var percentInMap = new Dictionary<byte, double>()
                {
                    {TileInfo.Grasslands,0.5},
                    {TileInfo.Medium_Water,0.4},
                    {TileInfo.Deep_Water,0.1}
                };

            double halfSwampSize = Convert.ToDouble(lakeSize * 4) / 2;
            for (int x = 0; x < lakeSize * 4; x++)
            {
                for (int y = 0; y < lakeSize * 4; y++)
                {
                    lakeNoise[x, y] = lakeNoise[x, y]
                        * (-Math.Pow(((x - halfSwampSize) / halfSwampSize), 2) + 1)
                        * (-Math.Pow(((y - halfSwampSize) / halfSwampSize), 2) + 1);
                }
            }

            var clothMapSizedSwamp = ClampToValuesInSetRatios(lakeNoise, percentInMap, lakeSize * 4);
            var mapSized = new byte[lakeSize, lakeSize];
            for (int x = 0; x < lakeSize; x++)
            {
                for (int y = 0; y < lakeSize; y++)
                {
                    mapSized[x, y] = clothMapSizedSwamp[x * 4, y * 4];
                }
            }

            return new Tuple<byte[,], byte[,]>(mapSized, clothMapSizedSwamp);
        }

        private List<List<ITile>> FindPlains(Random random, UltimaData ultimaData)
        {
            var plains = new List<List<ITile>>();

            var possiblePlainsStartingPoints = GetAllMatchingTiles(c => c.GetTile() == TileInfo.Grasslands);
            var plainsStartingPoints = GetEvenlyDistributedValidLocations(random, 6, null, possiblePlainsStartingPoints, ultimaData, false);

            var closedSet = new HashSet<ITile>();
            foreach(var startPoint in plainsStartingPoints)
            {
                var tile = startPoint;
                if (!closedSet.Contains(tile))
                {
                    var body = new List<ITile>();
                    Queue<ITile> queue = null;
                    var nextQueue = new Queue<ITile>();
                    nextQueue.Enqueue(tile);
                    var queueLayerCount = 0;
                    while (nextQueue.Count() > 0 && queueLayerCount < 31)
                    {
                        queue = nextQueue;
                        nextQueue = new Queue<ITile>();
                        while (queue.Count() > 0)
                        {
                            tile = queue.Dequeue();
                            if (!closedSet.Contains(tile) && tile.GetTile() == TileInfo.Grasslands)
                            {
                                body.Add(tile);

                                foreach (var n in tile.NeighborCoordinates())
                                {
                                    nextQueue.Enqueue(n);
                                }
                            }

                            closedSet.Add(tile);
                        }
                        queueLayerCount++;
                    }
                    plains.Add(body);
                }
            }

            return plains;
        }

        private List<List<ITile>> FindBodies(Func<ITile, bool> bodyMatcher)
        {
            var bodies = new List<List<ITile>>();
            var closedSet = new HashSet<ITile>();
            for (int x = 0; x < SIZE; x++)
            {
                for (int y = 0; y < SIZE; y++)
                {
                    ITile tile = GetCoordinate(x, y);
                    if (!closedSet.Contains(tile) && bodyMatcher(tile))
                    {
                        var body = new List<ITile>();
                        var queue = new Queue<ITile>();
                        queue.Enqueue(tile);
                        while (queue.Count() > 0)
                        {
                            tile = queue.Dequeue();
                            if (!closedSet.Contains(tile) && bodyMatcher(tile))
                            {
                                body.Add(tile);

                                foreach (var n in tile.NeighborAndAdjacentCoordinates())
                                {
                                    queue.Enqueue(n);
                                }
                            }

                            closedSet.Add(tile);
                        }
                        bodies.Add(body);
                    }
                }
            }           

            return bodies;
        }

        private void AddMountainsAndHills(Random random)
        {
            // Add other blobs
            var mountains = MountainMap(random);

            // Map the mountain noise to the same number range as the generated world maps so we can put them together
            var mountainMin = mountains.Item3.Cast<double>().Min();
            var mountainMax = mountains.Item3.Cast<double>().Max();
            for (int x = 0; x < SIZE; x++)
            {
                for (int y = 0; y < SIZE; y++)
                {
                    //https://math.stackexchange.com/questions/914823/shift-numbers-into-a-different-range/914843
                    mountains.Item3[x, y] = _generatedMin + (((_generatedMax - _generatedMin) / (mountainMax - mountainMin)) * (mountains.Item3[x, y] - mountainMin));
                    //mountains.Item2[x, y] = mountains.Item2[x, y] * mountains.Item2[x, y];
                }
            }

            for (int x = 0; x < SIZE; x++)
            {
                for (int y = 0; y < SIZE; y++)
                {
                    if (_worldMapTiles[x, y] == TileInfo.Grasslands)
                    {
                        _worldMapTiles[x, y] = mountains.Item1[x, y];
                    }
                }
            }

            for (int x = 0; x < SIZE * 4; x++)
            {
                for (int y = 0; y < SIZE * 4; y++)
                {
                    if (_clothMapTiles[x, y] == TileInfo.Grasslands)
                    {
                        _clothMapTiles[x, y] = mountains.Item2[x, y];
                    }
                }
            }

            _mountainOverlay = mountains.Item4;

            _generatedMin = _worldMapGenerated.Cast<double>().Min();
            _generatedMax = _worldMapGenerated.Cast<double>().Max();

            _mountHeightMap = mountains.Item3;
            _mountainMin = _mountHeightMap.Cast<double>().Min();
            _mountainMax = _mountHeightMap.Cast<double>().Max();
        }

        private void AddScrubAndForest(Random random, List<River> rivers)
        {
            // Add to Rivers
            var openSet = new HashSet<ITile>();
            var queue = new Queue<Tuple<ITile, int>>();
            foreach (var river in rivers)
            {
                river.LevelOrderTraversal(n =>
                {
                    if (!openSet.Contains(n.Coordinate))
                    {
                        openSet.Add(n.Coordinate);
                        queue.Enqueue(new Tuple<ITile, int>(n.Coordinate, 0));
                    }
                });
            }

            var finalSet = new HashSet<ITile>();
            var closedSet = new HashSet<ITile>();

            while (openSet.Count > 0)
            {
                var current = queue.Dequeue();
                openSet.Remove(current.Item1);
                closedSet.Add(current.Item1);

                if (current.Item2 < 4)
                {
                    foreach (var neighbor in current.Item1.NeighborCoordinates())
                    {
                        if (!closedSet.Contains(neighbor))
                        {
                            if (!openSet.Contains(neighbor))
                            {
                                if (neighbor.GetTile() == TileInfo.Grasslands)
                                {
                                    finalSet.Add(neighbor);
                                    openSet.Add(neighbor);
                                    queue.Enqueue(new Tuple<ITile, int>(neighbor, current.Item2 + 1));
                                }
                            }
                        }
                    }
                }
            }

            foreach (var tile in finalSet)
            {
                tile.SetTile(TileInfo.Scrubland);
            }

            // Add other blobs
            var scrub = ScrubMap(random);

            for (int x = 0; x < SIZE; x++)
            {
                for (int y = 0; y < SIZE; y++)
                {
                    if (_worldMapTiles[x, y] == TileInfo.Grasslands)
                    {
                        _worldMapTiles[x, y] = scrub.Item1[x, y];
                    }
                }
            }

            for (int x = 0; x < SIZE*4; x++)
            {
                for (int y = 0; y < SIZE*4; y++)
                {
                    if (_clothMapTiles[x, y] == TileInfo.Grasslands)
                    {
                        _clothMapTiles[x, y] = scrub.Item2[x, y];
                    }
                }
            }
        }

        private void AddBridges(Random random, List<River> rivers)
        {
            int totalNumOBridgedRivers = 9 + random.Next(1, 3) + random.Next(1, 3);

            var bridgeCount = 0;

            var riversCopy = rivers.ToList<River>();
            
            foreach (var river in rivers)
            {
                if (river.Direction.Item2 == 0)
                {
                    // Don't do rivers going east/west
                    riversCopy.Remove(river);
                }
                else
                {
                    var passesThroughMountains = false;
                    river.LevelOrderTraversal(n =>
                    {
                        foreach (var neighbor in n.Coordinate.NeighborAndAdjacentCoordinates())
                        {
                            if (neighbor.GetTile() == TileInfo.Mountains)
                            {
                                passesThroughMountains = true;
                            }
                        }
                    });
                    if (passesThroughMountains)
                    {
                        bridgeCount += AddBridge(random, river, true);
                        riversCopy.Remove(river);
                    }
                }
            }


            while (bridgeCount <= totalNumOBridgedRivers && riversCopy.Count > 0)
            {
                var index = random.Next(0, riversCopy.Count());
                bridgeCount += AddBridge(random, riversCopy[index], false);
                riversCopy.RemoveAt(index);
            }

            return;
        }

        private int AddBridge(Random random, River river, bool tryHarder)
        {
            

            var maxRiverDepth = 0;
            river.LevelOrderTraversal(n => maxRiverDepth = Math.Max(maxRiverDepth, n.depth));

            var var1 = random.Next(maxRiverDepth);
            var var2 = random.Next(maxRiverDepth);
            int minBridgeDepth = Math.Abs(((var1 + var2) / 2) - maxRiverDepth / 2);
            
            // Bridge can't be further out than the river

            var maxBridgeDepth = Math.Max(maxRiverDepth - 4, 0);
            minBridgeDepth = Math.Max(Math.Min(minBridgeDepth, maxRiverDepth - 1), 0);

            return river.AddBridges(this, minBridgeDepth, maxBridgeDepth);

            //var bridgeAdded = false;

            //var stop = false;
            //river.LevelOrderTraversal(n =>
            //{
            //    if(n.depth > correctedMinBridgeDepth && n.depth < maxRiverDepth && !stop)
            //    {
            //        if(n.Coordinate.GetTile() != TileInfo.Shallow_Water)
            //        {
            //            stop = true;
            //        }

            //        if (IsWalkableGround(GetCoordinate(n.Coordinate.X - 1, n.Coordinate.Y)) && IsWalkableGround(GetCoordinate(n.Coordinate.X + 1, n.Coordinate.Y)))
            //        {
            //            _worldMapTiles[n.Coordinate.X, n.Coordinate.Y] = TileInfo.Bridge;
            //            bridgeAdded = true;
            //            stop = true;
            //        }
            //    }
            //});

            //if (!bridgeAdded && tryHarder)
            //{
            //    stop = false;
            //    river.LevelOrderTraversal(n =>
            //    {
            //        if (n.depth > correctedMinBridgeDepth && !stop)
            //        {
            //            if (n.Coordinate.GetTile() != TileInfo.Shallow_Water)
            //            {
            //                stop = true;
            //            }

            //            if (IsWalkableGround(GetCoordinate(n.Coordinate.X - 1, n.Coordinate.Y)) && IsWalkableGround(GetCoordinate(n.Coordinate.X + 1, n.Coordinate.Y)))
            //            {
            //                _worldMapTiles[n.Coordinate.X, n.Coordinate.Y] = TileInfo.Bridge;
            //                bridgeAdded = true;
            //                stop = true;
            //            }
            //        }
            //    });
            //}
        }

        private List<River> AddRivers(Random random)
        {
            // There are ~32 in the original Ultima map
            int totalNumOfRivers = 28 + random.Next(1, 3) + random.Next(1, 3);

            //find possible points
            var possible = new List<ITile>();
            for(int x = 0; x < SIZE; x++)
            {
                for(int y = 0; y < SIZE; y++)
                {
                    var coord = GetCoordinate(x, y);
                    if(IsWalkable(coord))
                    {
                        if(IsSailableWater(GetCoordinate(x + 1, y)) ||
                           IsSailableWater(GetCoordinate(x - 1, y)) ||
                           IsSailableWater(GetCoordinate(x, y + 1)) ||
                           IsSailableWater(GetCoordinate(x, y - 1)))
                        {
                            possible.Add(coord);
                        }
                    }
                }
            }

            var usedLocations = new List<ITile>();
            var chosenLocations = GetEvenlyDistributedValidLocations(random, totalNumOfRivers, usedLocations, possible, null, false, 100);

            var rivers = new List<River>();
            var randomSeeds = new List<int>();
            for (int i = 0; i < chosenLocations.Count; i++)
            {
                randomSeeds.Add(random.Next());
            }
            for (int i = 0; i < chosenLocations.Count; i++)
            {
                var mouth = chosenLocations[i];
                Tuple<int, int> direction = null;

                if (IsWater(GetCoordinate(mouth.X + 1, mouth.Y)))
                {
                    direction = new Tuple<int, int>(-1, 0); ;
                }
                else if (IsWater(GetCoordinate(mouth.X - 1, mouth.Y)))
                {
                    direction = new Tuple<int, int>(1, 0); ;
                }
                if (IsWater(GetCoordinate(mouth.X, mouth.Y + 1)))
                {
                    direction = new Tuple<int, int>(0, -1); ;
                }
                if (IsWater(GetCoordinate(mouth.X, mouth.Y - 1)))
                {
                    direction = new Tuple<int, int>(0, 1); ;
                }

                var riverLength = random.Next(8) + random.Next(8) + 1;

                var currentNode = new RiverNode()
                {
                    Coordinate = mouth,
                    Parent = null,
                    Children = new List<RiverNode>(),
                    depth = 0
                };

                RiverTributary(new Random(randomSeeds[i]), currentNode, direction, riverLength, TileInfo.A);
                if(currentNode.Children.Count > 0)
                {
                    var river = new River()
                    {
                        Tree = currentNode,
                        Direction = direction,
                        RandomSeed = randomSeeds[i]
                    };
                    rivers.Add(river);

                    river.LevelOrderTraversal(n => { 
                        n.Coordinate.SetTile(TileInfo.Shallow_Water);
                        //n.Coordinate.SetClothTile(TileInfo.Shallow_Water);
                    });

                    //currentNode = new RiverNode()
                    //{
                    //    Coordinate = mouth,
                    //    Parent = null,
                    //    Children = new List<RiverNode>(),
                    //    depth = 0
                    //};

                    //RiverTributary(new Random(randomSeeds[i]), currentNode, direction, riverLength*2, TileInfo.A);
                    //var longRiver = new River()
                    //{
                    //    Tree = currentNode,
                    //    Direction = direction,
                    //    RandomSeed = randomSeeds[i]
                    //};
                    //river.LevelOrderTraversal(n => {
                    //    n.Coordinate.SetTile(TileInfo.A);
                    //    //n.Coordinate.SetClothTile(TileInfo.Shallow_Water);
                    //});
                    //longRiver.LevelOrderTraversal(n => {
                    //    n.Coordinate.SetTile(TileInfo.B);
                    //    //n.Coordinate.SetClothTile(TileInfo.Shallow_Water);
                    //});
                }
                else
                {
                    //mouth.SetTile(TileInfo.F);
                    usedLocations.Remove(mouth);
                    chosenLocations[i] = GetEvenlyDistributedValidLocations(random, 1, usedLocations, possible, null, false, 100)[0];
                    i--;
                }
            }

            return rivers;
        }



        private void RiverTributary(Random random, RiverNode currentNode, Tuple<int, int> direction, int riverLength, byte tile, IsNodeValid allowedTileMatcher = null)
        {
            if(allowedTileMatcher == null)
            {
                allowedTileMatcher = IsWalkable;
            }
            for (int i = 0; i < riverLength; i++)
            {
                var advanced = false;
                if (LookAhead(currentNode.Coordinate, direction, allowedTileMatcher, 2, 1, 1))
                {
                    currentNode.Coordinate.SetTile(tile);
                    var nextCoord = GetCoordinate(currentNode.Coordinate.X + direction.Item1, currentNode.Coordinate.Y + direction.Item2);
                    currentNode = AdvanceRiverTile(currentNode, nextCoord);
                    currentNode.Coordinate.SetTile(tile);

                    advanced = true;
                }


                // Do we wiggle?
                Tuple<int, int> wiggleDirection = null;
                if (i != 0 && (!advanced && random.Next(16) < 10 || random.Next(16) < 5))
                {
                    var flipIt = (random.Next(2) == 0 ? -1 : 1);
                    wiggleDirection = new Tuple<int, int>(direction.Item2 * flipIt, direction.Item1 * flipIt);
                    var oppositeWiggleDirection = new Tuple<int, int>(direction.Item2, direction.Item1);

                    // Don't wiggle if there is water in the direction I am wiggling
                    //if (!IsWater(GetCoordinate(currentCoord.X + wiggleDirection.Item1, currentCoord.Y + wiggleDirection.Item2)) &&
                    //   !IsWater(GetCoordinate(currentCoord.X + wiggleDirection.Item1 * 2, currentCoord.Y + wiggleDirection.Item2 * 2)))
                    if (LookAhead(currentNode.Coordinate, wiggleDirection, allowedTileMatcher, 2, 1, 1))
                    {
                        var nextCoord = GetCoordinate(currentNode.Coordinate.X + wiggleDirection.Item1, currentNode.Coordinate.Y + wiggleDirection.Item2);
                        currentNode = AdvanceRiverTile(currentNode, nextCoord);
                        currentNode.Coordinate.SetTile(tile);
                    }
                    else if (LookAhead(currentNode.Coordinate, oppositeWiggleDirection, allowedTileMatcher, 2, 1, 1))
                    {
                        wiggleDirection = oppositeWiggleDirection;
                        var nextCoord = GetCoordinate(currentNode.Coordinate.X + wiggleDirection.Item1, currentNode.Coordinate.Y + wiggleDirection.Item2);
                        currentNode = AdvanceRiverTile(currentNode, nextCoord);
                        currentNode.Coordinate.SetTile(tile);
                    }
                    else
                    {
                        wiggleDirection = null;
                    }
                }

                if(!advanced && wiggleDirection == null)
                {
                    break;
                }

                // Do we split
                if (i != 0 && i != riverLength - 1 && random.Next(8) < 3)
                {

                    Tuple<int, int> splitDirection = null;
                    // If we already wiggled just go the other direction
                    if (wiggleDirection != null)
                    {
                        splitDirection = new Tuple<int, int>(wiggleDirection.Item1 * -1, wiggleDirection.Item2 * -1);
                    }
                    else
                    {
                        var flipIt = (random.Next(2) == 0 ? -1 : 1);
                        splitDirection = new Tuple<int, int>(direction.Item2 * flipIt, direction.Item1 * flipIt);
                    }

                    // Don't split if there is water in the direction I am splitting
                    //if (!IsWater(GetCoordinate(currentCoord.X + splitDirection.Item1, currentCoord.Y + splitDirection.Item2)) &&
                    //   !IsWater(GetCoordinate(currentCoord.X + splitDirection.Item1 * 2, currentCoord.Y + splitDirection.Item2 * 2)) &&
                    //   !IsWater(GetCoordinate(currentCoord.X + splitDirection.Item1 * 3, currentCoord.Y + splitDirection.Item2 * 3)))
                    var newTributaryCoord = GetCoordinate(currentNode.Coordinate.X + splitDirection.Item1, currentNode.Coordinate.Y + splitDirection.Item2);
                    if (LookAhead(newTributaryCoord, splitDirection, allowedTileMatcher, 2, 1, 1))
                    {
                        
                        //_worldMapTiles[newTributaryCoord.X, newTributaryCoord.Y] = tile;
                        //river.Add(newTributaryCoord);
                        //newTributaryCoord = GetCoordinate(currentCoord.X + splitDirection.Item1, currentCoord.Y + splitDirection.Item2);
                        var newTributaryNode = AdvanceRiverTile(currentNode, newTributaryCoord);
                        newTributaryNode.Coordinate.SetTile(tile);
                        newTributaryCoord = GetCoordinate(newTributaryCoord.X + splitDirection.Item1, newTributaryCoord.Y + splitDirection.Item2);
                        newTributaryNode = AdvanceRiverTile(newTributaryNode, newTributaryCoord);
                        newTributaryNode.Coordinate.SetTile(tile);

                        RiverTributary(random, currentNode, direction, riverLength - i, (byte)(tile + 1), allowedTileMatcher);
                        var max = riverLength;
                        var min = i + 2;
                        riverLength = min;
                        if (max >= min)
                        {
                            riverLength = random.FavorLower(min, max);
                        }

                        //riverLength = random.Next(riverLength - i) + 1;
                        currentNode = newTributaryNode;
                    }
                }

            }

            return;
        }

        private RiverNode AdvanceRiverTile(RiverNode currentNode, ITile nextCoord)
        {
            var nextNode = new RiverNode()
            {
                Parent = currentNode,
                Coordinate = nextCoord,
                Children = new List<RiverNode>(),
                depth = currentNode.depth + 1
            };
            currentNode.Children.Add(nextNode);
            currentNode = nextNode;
            return currentNode;
        }

        private bool LookAhead(ITile currentCoord, Tuple<int, int> direction, IsNodeValid tileMatcher, int forwardDist, int leftDist, int rightDist)
        {
            //if (IsWalkable(GetCoordinate(currentCoord.X + direction.Item1, currentCoord.Y + direction.Item2)) &&
            //       IsWalkable(GetCoordinate(currentCoord.X + direction.Item1 * 2, currentCoord.Y + direction.Item2 * 2)))

            var left = new Tuple<int, int>(direction.Item2, direction.Item1);
            var right = new Tuple<int, int>(direction.Item2 * -1, direction.Item1 * -1);
            bool result = true;
            for (int i = 1; i <= forwardDist && result; i++)
            {
                result = result && tileMatcher(GetCoordinate(currentCoord.X + direction.Item1 * i, currentCoord.Y + direction.Item2 * i));

                for(int l = 1; l <= leftDist; l++)
                {
                    result = result && tileMatcher(GetCoordinate(currentCoord.X + direction.Item1 * i + left.Item1 * l, currentCoord.Y + direction.Item2 * i + left.Item2 * l));
                }

                for (int r = 1; r <= rightDist; r++)
                {
                    result = result && tileMatcher(GetCoordinate(currentCoord.X + direction.Item1 * i + right.Item1 * r, currentCoord.Y + direction.Item2 * i + right.Item2 * r));
                }
            }

            return result;
        }

        public List<ITile> GetRiverPath(ITile startTile, IsNodeValid matchesGoal)
        {
            return Search.GetPath(WorldMapGenerateMap.SIZE, WorldMapGenerateMap.SIZE, startTile, matchesGoal, delegate { return true; }, GoDownhillHueristic);
        }

        public List<Tile> FindAllByPattern(int[,] pattern)
        {
            var validCoordinates = new List<Tile>();
            for (int map_x = 0; map_x < SIZE; map_x++)
            {
                for (int map_y = 0; map_y < SIZE; map_y++)
                {
                    var matchesAll = true;
                    for (int pat_x = 0; pat_x < pattern.GetLength(0); pat_x++)
                    {
                        for (int pat_y = 0; pat_y < pattern.GetLength(1); pat_y++)
                        {
                            var tile = _worldMapTiles[Wrap(map_x + pat_x), Wrap(map_y + pat_y)];
                            if (_worldMapTiles[Wrap(map_x + pat_x), Wrap(map_y + pat_y)] != pattern[pat_x, pat_y] && pattern[pat_x, pat_y] != -1)
                            {
                                matchesAll = false;
                            }
                        }
                    }

                    if (matchesAll)
                    {
                        validCoordinates.Add(new Tile(map_x, map_y, _worldMapTiles, v => Wrap(v)));
                    }
                }
            }

            return validCoordinates;
        }

        //public delegate float NodeHuersticValue(Coordinate coord, IsNodeValid matchesGoal);
        public float GoDownhillHueristic(ITile coord, ITile cameFrom, IsNodeValid matchesGoal)
        {
            if (matchesGoal(coord))
            {
                return 1.0f;
            }

            // Map to 0 - 1
            var value = ((0.0 + (((1.0 - 0.0) / (_mountainMax - _mountainMin)) * (_mountHeightMap[coord.X, coord.Y] - _mountainMin))));

            return Convert.ToSingle(value - (randomDownhill.NextDouble()/25));
        }

        public static bool IsWalkableGround(ITile coord)
        {
            return coord.GetTile() >= TileInfo.Swamp && coord.GetTile() <= TileInfo.Hills;
        }

        private static bool IsNotWater(ITile coordinate)
        {
            return !IsWater(coordinate);
        }

        private static bool IsNotWater(byte tile)
        {
            return !IsWater(tile);
        }

        private static bool IsWater(ITile coordinate)
        {
            return IsWater(coordinate.GetTile());
        }

        private static bool IsWater(byte tile)
        {
            return (tile == TileInfo.Deep_Water || tile == TileInfo.Medium_Water || tile == TileInfo.Shallow_Water || (tile >= TileInfo.A && tile <= TileInfo.Z));
        }

        private static bool IsSailableWater(ITile coordinate)
        {
            return coordinate.GetTile() < TileInfo.Shallow_Water;
        }

        private Tile FindRandomPointHigherThan(int tile, Random random)
        {
            Tile result = null;
            while (result == null)
            {
                var x = random.Next(0, 256);
                var y = random.Next(0, 256);

                if (_worldMapTiles[x, y] > tile)
                {
                    result = new Tile(x, y, _worldMapTiles, v => Wrap(v));
                }
            }

            return result;
        }


        private bool AreaIsAll(int tile, int length, ICoordinate coordinate)
        {
            return AreaIsAll(tile, length, length, coordinate, true);
        }

        private bool AreaIsAll(int tile, int width, int height, ICoordinate coordinate, bool center)
        {
            return AreaIsAll(c => c.GetTile() == tile, width, height, coordinate, center);
        }

        private bool AreaIsAll(Func<ITile, bool> p, int width, int height, ICoordinate coordinate, bool center)
        {
            int centerX = width / 2;
            int centerY = height / 2;

            if(!center)
            {
                centerX = 0;
                centerY = 0;
            }

            var result = true;
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    result = result && p(GetCoordinate(coordinate.X - centerX + x, coordinate.Y - centerY + y));
                }
            }

            return result;
        }


        private void ApplyShape(ICoordinate loc, string file, bool center = true)
        {
            //var shape = new System.IO.FileStream($"{file}", System.IO.FileMode.Open);
            object obj = U4DosRandomizer.Resources.Shapes.ResourceManager.GetObject(file, U4DosRandomizer.Resources.Shapes.Culture);
            var shape = ((byte[])(obj));

            var width = shape[0];
            var height = shape[1];
            byte[] shapeBytes = new byte[shape.Count() - 2];
            Array.Copy(shape, 2, shapeBytes, 0, shape.Count() - 2);

            int centerX = width / 2;
            int centerY = height / 2;
            if(!center)
            {
                centerX = 0;
                centerY = 0;
            }
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var idx = x + y * width;
                    var tile = shapeBytes[idx];
                    if (tile != 0xFF)
                    {
                        GetCoordinate(loc.X - centerX + x, loc.Y - centerY + y).SetTile(tile);
                    }
                }
            }
        }

        private List<ITile> GetEvenlyDistributedValidLocations(Random random, int totalResults, List<ITile> usedLocations, List<ITile> possibleLocations, UltimaData ultimaData, bool requirePath, int numCandidates = 10)
        {
            // Using Mitchell's best-candidate algorithm - https://bost.ocks.org/mike/algorithms/
            var results = new List<ITile>();

            var randomIdx = random.Next(0, possibleLocations.Count);
            var original = possibleLocations[randomIdx];

            if(usedLocations == null)
            {
                usedLocations = new List<ITile>();
            }
            results.Add(original);
            usedLocations.Add(original);

            for (int i = 0; i < totalResults; i++)
            {
                ITile bestCandidate = null;
                var bestDistance = 0;
                for (int sample = 0; sample < numCandidates; sample++)
                {
                    randomIdx = random.Next(0, possibleLocations.Count);
                    ITile selection = null; // possibleLocations[randomIdx];
                    if (requirePath)
                    {
                        while (selection == null)
                        {
                            selection = possibleLocations[randomIdx];
                            var path = Search.GetPath(WorldMapGenerateMap.SIZE, WorldMapGenerateMap.SIZE, selection,
                            c => { return IsWalkableOrSailable(c) || selection.Equals(c); },
                            c => { return ultimaData.Towns.Contains(c); });

                            if (path.Count == 0)
                            {
                                selection = null;
                            }
                        }
                    }
                    else
                    {
                        selection = possibleLocations[randomIdx];
                    }

                    var distance = DistanceSquared(FindClosest(selection, usedLocations), selection);

                    if (distance > bestDistance)
                    {
                        bestDistance = distance;
                        bestCandidate = selection;
                    }
                }

                var result = bestCandidate;
                results.Add(result);
                usedLocations.Add(result);
            }

            return results;
        }

        private ICoordinate FindClosest(ITile selection, List<ITile> locations)
        {
            ITile closest = null;
            var closestDistance = int.MaxValue;
            for (int i = 0; i < locations.Count(); i++)
            {
                var distance = DistanceSquared(selection, locations[i]);
                if (distance >= 0 && distance < closestDistance)
                {
                    closest = locations[i];
                    closestDistance = distance;
                }
            }

            return closest;
        }

        private static ITile RandomSelectFromListCheckPathChangeAndRemove(Random random, List<ITile> possibleLocations, byte? tile)
        {
            ITile loc = null;
            while (loc == null && possibleLocations.Count > 0)
            {
                var randomIdx = random.Next(0, possibleLocations.Count);
                loc = possibleLocations[randomIdx];

                if (tile != null)
                {
                    loc.SetTile(tile.Value);
                }

                possibleLocations.RemoveAt(randomIdx);
            }

            if (loc == null)
            {
                if (tile != null)
                {
                    throw new Exception($"Failed to find location for {TileInfo.GetLabel(tile.Value)}.");
                }
                else
                {
                    throw new Exception($"Failed to find location with path to town.");
                }
            }

            return loc;
        }

        private Tile GetRandomCoordinate(Random random)
        {
            var loc = GetCoordinate(random.Next(0, WorldMapGenerateMap.SIZE), random.Next(0, WorldMapGenerateMap.SIZE));
            return loc;
        }

        private Tile GetRandomCoordinate(Random random, Func<Tile, bool> criteria, HashSet<ITile> excludes)
        {
            while (true)
            {
                var loc = GetRandomCoordinate(random);
                if (criteria(loc) && !excludes.Contains(loc))
                {
                    return loc;
                }
            }
        }

        private bool GoodForHumility(ICoordinate coordinate)
        {
            var result = true;
            for (int y = -4; y <= 1; y++)
            {
                for (int x = -1; x <= 1; x++)
                {
                    result = result && IsTile(coordinate.X + x, coordinate.Y + y, TileInfo.Mountains);
                }
            }

            var entrance = GetCoordinate(coordinate.X, coordinate.Y - 5);

            result = result && IsWalkable(entrance);

            if (result)
            {
                // Make sure we can reach it by boat or balloon
                var path = Search.GetPath(256, 256, entrance, IsGrassOrSailable, IsWalkableOrSailable, null);

                result = result && path.Count > 0;
            }

            return result;
        }

        public new SixLabors.ImageSharp.Image ToClothMap(UltimaData data, Random random)
        {
            var image = new SixLabors.ImageSharp.Image<Rgba32>(WorldMapGenerateMap.SIZE * 4, WorldMapGenerateMap.SIZE * 4);

            var rivers = new SixLabors.ImageSharp.Image<Rgba32>(WorldMapGenerateMap.SIZE * 4, WorldMapGenerateMap.SIZE * 4, new Rgba32(0,0,0,0));

            
            //var river = Rivers[0];
            //pathBuilder.SetOrigin(new SixLabors.ImageSharp.PointF(river.Tree.Coordinate.X * 4, river.Tree.Coordinate.Y * 4));
            //Console.WriteLine("------------");
            IPen pen = Pens.Solid(SixLabors.ImageSharp.Color.Red, 2);
            DrawingOptions riverOptions = new DrawingOptions()
            {
                GraphicsOptions = new SixLabors.ImageSharp.GraphicsOptions()
                {
                    ColorBlendingMode = PixelColorBlendingMode.Multiply
                }
            };

            var maxDepth = 0;
            foreach (var river in Rivers)
            {
                river.LevelOrderTraversal(c => maxDepth = Math.Max(c.depth, maxDepth));
            }

            //var river = Rivers[0];
            //foreach (var river in Rivers)
            for(int i = 0; i < Rivers.Count; i++)
            {
                var river = Rivers[i];
                PathBuilder pathBuilder = new PathBuilder();
                var depth = 0;
                river.LevelOrderTraversal(c => depth = Math.Max(c.depth, depth));


                var angle = (float)(Math.PI/32 * (random.NextDouble()*2-1) * (depth/maxDepth * -1 + 1));
                var transform = System.Numerics.Matrix3x2.CreateRotation(angle, new System.Numerics.Vector2(river.Tree.Coordinate.X*4, river.Tree.Coordinate.Y*4));
                pathBuilder.SetTransform(transform);

                var driftRange = random.NextDouble(0.20, 0.30) * (random.Next(2) == 0 ? -1 : 1) * (depth / maxDepth * -1 + 1);
                var driftXdelta = river.Direction.Item2 != 0 ? driftRange : 0;
                var driftYdelta = river.Direction.Item1 != 0 ? driftRange : 0;
                var driftX = 0.0;
                var driftY = 0.0;
                river.LevelOrderTraversal(c =>
                {
                    var driftWithJitterX = driftX + (0.25 * (random.Next(2) - 1));
                    var driftWithJitterY = driftY + (0.25 * (random.Next(2) - 1));

                    driftWithJitterX = driftWithJitterX * (c.depth / maxDepth * -1 + 1);
                    driftWithJitterY = driftWithJitterY * (c.depth / maxDepth * -1 + 1);
                    if (c.Parent != null)
                    {
                        //pathBuilder.AddLine(new SixLabors.ImageSharp.PointF(c.Coordinate.X * 4, c.Coordinate.Y * 4), new SixLabors.ImageSharp.PointF(c.Parent.Coordinate.X * 4, c.Parent.Coordinate.Y * 4));
                        if (c.Parent.Parent == null)
                        {
                            pathBuilder.AddLine(c.Coordinate.X * 4 + (int)driftWithJitterX, c.Coordinate.Y * 4 + (int)driftWithJitterY, c.Parent.Coordinate.X * 4 + (int)driftWithJitterX, c.Parent.Coordinate.Y * 4 + (int)driftWithJitterY);
                        }
                        else
                        {
                            int xStart = c.Parent.Coordinate.X * 4;
                            int yStart = c.Parent.Coordinate.Y * 4;
                            int xControl = c.Parent.Coordinate.X * 4;
                            int yControl = c.Parent.Coordinate.Y * 4;
                            int xEnd = c.Parent.Coordinate.X * 4;
                            int yEnd = c.Parent.Coordinate.Y * 4;
                            if(c.Parent.Coordinate.X > c.Parent.Parent.Coordinate.X)
                            {
                                xEnd = c.Parent.Coordinate.X * 4 - 2;
                            }
                            else if (c.Parent.Coordinate.X < c.Parent.Parent.Coordinate.X)
                            {
                                xEnd = c.Parent.Coordinate.X * 4 + 2;
                            }
                            if (c.Parent.Coordinate.Y > c.Parent.Parent.Coordinate.Y)
                            {
                                yEnd = c.Parent.Coordinate.Y * 4 - 2;
                            }
                            else if (c.Parent.Coordinate.Y < c.Parent.Parent.Coordinate.Y)
                            {
                                yEnd = c.Parent.Coordinate.Y * 4 + 2;
                            }

                            if (c.Parent.Coordinate.X > c.Coordinate.X)
                            {
                                xStart = c.Parent.Coordinate.X * 4 - 2;
                            }
                            else if (c.Parent.Coordinate.X < c.Coordinate.X)
                            {
                                xStart = c.Parent.Coordinate.X * 4 + 2;
                            }
                            if (c.Parent.Coordinate.Y > c.Coordinate.Y)
                            {
                                yStart = c.Parent.Coordinate.Y * 4 - 2;
                            }
                            else if (c.Parent.Coordinate.Y < c.Coordinate.Y)
                            {
                                yStart = c.Parent.Coordinate.Y * 4 + 2;
                            }
                            xStart = (int)(xStart + driftWithJitterX);
                            yStart = (int)(yStart + driftWithJitterY);
                            xEnd = (int)(xEnd + driftWithJitterX);
                            yEnd = (int)(yEnd + driftWithJitterY);
                            xControl = (int)(xControl + driftWithJitterX);
                            yControl = (int)(yControl + driftWithJitterY);

                            pathBuilder.AddQuadraticBezier(new SixLabors.ImageSharp.PointF(xStart, yStart), new SixLabors.ImageSharp.PointF(xControl, yControl), new SixLabors.ImageSharp.PointF(xEnd, yEnd));
                            //pathBuilder.AddBezier(new SixLabors.ImageSharp.PointF(c.Coordinate.X * 4, c.Coordinate.Y * 4), new SixLabors.ImageSharp.PointF(c.Parent.Coordinate.X * 4, c.Parent.Coordinate.Y * 4), new SixLabors.ImageSharp.PointF(c.Coordinate.X * 4, c.Coordinate.Y * 4));
                            //pathBuilder.AddBezier(new SixLabors.ImageSharp.PointF(c.Coordinate.X * 4, c.Coordinate.Y * 4), new SixLabors.ImageSharp.PointF(c.Parent.Coordinate.X * 4, c.Parent.Coordinate.Y * 4), new SixLabors.ImageSharp.PointF(c.Parent.Parent.Coordinate.X * 4, c.Parent.Parent.Coordinate.Y * 4));
                        }

                        pathBuilder.CloseFigure();

                        //Console.WriteLine($"Self: {c.Coordinate.X} {c.Coordinate.Y}");
                        //Console.WriteLine($"Parent: {c.Parent.Coordinate.X} {c.Parent.Coordinate.Y}");
                        //Console.WriteLine("");
                    }
                    else
                    {
                        var xEnd = c.Coordinate.X + (river.Direction.Item1 * -2);
                        var yEnd = c.Coordinate.Y + (river.Direction.Item2 * -2);
                        //pathBuilder.AddLine(new SixLabors.ImageSharp.PointF(c.Coordinate.X * 4, c.Coordinate.Y * 4), new SixLabors.ImageSharp.PointF(parent.X * 4, parent.Y * 4));
                        pathBuilder.AddLine(c.Coordinate.X * 4 + (int)driftWithJitterX, c.Coordinate.Y * 4 + (int)driftWithJitterY, xEnd * 4 + (int)driftWithJitterX, yEnd * 4 + (int)driftWithJitterY);

                        pathBuilder.CloseFigure();
                    }
                    driftX = driftX + driftXdelta + (Math.Log(1+Math.Abs(driftX))/20 * (driftX < 0 ? -1 : 1));
                    driftY = driftY + driftYdelta + (Math.Log(1+Math.Abs(driftY))/20 * (driftY < 0 ? -1 : 1));
                });

                IPath path = pathBuilder.Build();
                //if (Rivers.FindIndex(c => c.Tree == river.Tree) == 0)
                {
                    rivers.Mutate(ctx => ctx.Draw(riverOptions, pen, path));
                }
                pathBuilder.ResetTransform();
            }
            //pathBuilder.AddLine(new SixLabors.ImageSharp.PointF(river.Tree.Coordinate.X * 4, river.Tree.Coordinate.Y *4), new SixLabors.ImageSharp.PointF(river.Tree.Children[0].Coordinate.X * 4, river.Tree.Children[0].Coordinate.Y * 4));

            

            using (SixLabors.ImageSharp.Image<Rgba32> deep_water = SixLabors.ImageSharp.Image.Load<Rgba32>(ClothMap.deep_water))
            {
                using (SixLabors.ImageSharp.Image<Rgba32> medium_water = SixLabors.ImageSharp.Image.Load<Rgba32>(ClothMap.medium_water))
                {
                    using (SixLabors.ImageSharp.Image<Rgba32> grass = SixLabors.ImageSharp.Image.Load<Rgba32>(ClothMap.grass))
                    {
                        using (SixLabors.ImageSharp.Image<Rgba32> scrub = SixLabors.ImageSharp.Image.Load<Rgba32>(ClothMap.scrub))
                        {
                            using (SixLabors.ImageSharp.Image<Rgba32> forest = SixLabors.ImageSharp.Image.Load<Rgba32>(ClothMap.forest))
                            {
                                using (SixLabors.ImageSharp.Image<Rgba32> hills = SixLabors.ImageSharp.Image.Load<Rgba32>(ClothMap.hills))
                                {
                                    using (SixLabors.ImageSharp.Image<Rgba32> hillsOverlay = SixLabors.ImageSharp.Image.Load<Rgba32>(ClothMap.hills_overlay))
                                    {
                                        using (SixLabors.ImageSharp.Image<Rgba32> mountainsOverlay = SixLabors.ImageSharp.Image.Load<Rgba32>(ClothMap.mountains_overlay))
                                        {
                                            using (SixLabors.ImageSharp.Image<Rgba32> riversOverlay = SixLabors.ImageSharp.Image.Load<Rgba32>(ClothMap.medium_water))
                                            {
                                                using (SixLabors.ImageSharp.Image<Rgba32> swamp = SixLabors.ImageSharp.Image.Load<Rgba32>(ClothMap.swamp))
                                                {
                                                    var outlineOverlay = new SixLabors.ImageSharp.Image<Rgba32>(WorldMapGenerateMap.SIZE * 4, WorldMapGenerateMap.SIZE * 4);

                                                    var erosionMap = ErosionMap(_clothMapTiles, new byte[] { TileInfo.Deep_Water, TileInfo.Medium_Water, TileInfo.Shallow_Water }, new byte[] { });
                                                    var erosionMap2 = ErosionMap(_clothMapTiles, new byte[] { }, new byte[] { TileInfo.Deep_Water, TileInfo.Medium_Water, TileInfo.Shallow_Water });

                                                    //Span<Rgba32> deepWaterRowSpan = deep_water.GetPixelRowSpan(y);
                                                    //Span<Rgba32> mediumWaterRowSpan = medium_water.GetPixelRowSpan(y);
                                                    //Span<Rgba32> grassRowSpan = grass.GetPixelRowSpan(y);
                                                    //Span<Rgba32> scrubRowSpan = scrub.GetPixelRowSpan(y);
                                                    //Span<Rgba32> forestRowSpan = forest.GetPixelRowSpan(y);
                                                    //Span<Rgba32> swampRowSpan = swamp.GetPixelRowSpan(y);
                                                    //Span<Rgba32> hillsRowSpan = hills.GetPixelRowSpan(y);
                                                    //Span<Rgba32> hillsOverlayRowSpan = hillsOverlay.GetPixelRowSpan(y);
                                                    //Span<Rgba32> mountainsOverlayRowSpan = mountainsOverlay.GetPixelRowSpan(y);
                                                    //Span<Rgba32> pixelRowSpan = image.GetPixelRowSpan(y);
                                                    //Span<Rgba32> outlineOverlayRowSpan = outlineOverlay.GetPixelRowSpan(y);
                                                    //Span<Rgba32> riversOverlayRowSpan = riversOverlay.GetPixelRowSpan(y);
                                                    image.ProcessPixelRows(grass, (imageAccessor, grassAccessor) =>
                                                    {
                                                        for (int y = 0; y < WorldMapGenerateMap.SIZE * 4; y++)
                                                        {
                                                            Span<Rgba32> pixelRowSpan = imageAccessor.GetRowSpan(y);
                                                            Span<Rgba32> grassRowSpan = grassAccessor.GetRowSpan(y);
                                                            for (int x = 0; x < WorldMapGenerateMap.SIZE * 4; x++)
                                                            {
                                                                pixelRowSpan[x] = grassRowSpan[x];
                                                            }
                                                        }
                                                    });

                                                    image.ProcessPixelRows(swamp, (imageAccessor, swampAccessor) =>
                                                    {
                                                        for (int y = 0; y < WorldMapGenerateMap.SIZE * 4; y++)
                                                        {
                                                            Span<Rgba32> pixelRowSpan = imageAccessor.GetRowSpan(y);
                                                            Span<Rgba32> swampRowSpan = swampAccessor.GetRowSpan(y);
                                                            for (int x = 0; x < WorldMapGenerateMap.SIZE * 4; x++)
                                                            {
                                                                if (_clothMapTiles[x, y] == TileInfo.Swamp)
                                                                {
                                                                    pixelRowSpan[x] = swampRowSpan[x];
                                                                }
                                                            }
                                                        }
                                                    });

                                                    image.ProcessPixelRows(hills, (imageAccessor, hillsAccessor) =>
                                                    {
                                                        for (int y = 0; y < WorldMapGenerateMap.SIZE * 4; y++)
                                                        {
                                                            Span<Rgba32> pixelRowSpan = imageAccessor.GetRowSpan(y);
                                                            Span<Rgba32> hillsRowSpan = hillsAccessor.GetRowSpan(y);
                                                            for (int x = 0; x < WorldMapGenerateMap.SIZE * 4; x++)
                                                            {
                                                                if (_clothMapTiles[x, y] == TileInfo.Hills || _clothMapTiles[x, y] == TileInfo.Mountains)
                                                                {
                                                                    pixelRowSpan[x] = hillsRowSpan[x];
                                                                }
                                                            }
                                                        }
                                                    });

                                                    image.ProcessPixelRows(forest, (imageAccessor, forestAccessor) =>
                                                    {
                                                        for (int y = 0; y < WorldMapGenerateMap.SIZE * 4; y++)
                                                        {
                                                            Span<Rgba32> pixelRowSpan = imageAccessor.GetRowSpan(y);
                                                            Span<Rgba32> forestRowSpan = forestAccessor.GetRowSpan(y);
                                                            for (int x = 0; x < WorldMapGenerateMap.SIZE * 4; x++)
                                                            {
                                                                if (_clothMapTiles[x, y] == TileInfo.Forest)
                                                                {
                                                                    pixelRowSpan[x] = forestRowSpan[x];
                                                                }
                                                            }
                                                        }
                                                    });

                                                    image.ProcessPixelRows(scrub, (imageAccessor, scrubAccessor) =>
                                                    {
                                                        for (int y = 0; y < WorldMapGenerateMap.SIZE * 4; y++)
                                                        {
                                                            Span<Rgba32> pixelRowSpan = imageAccessor.GetRowSpan(y);
                                                            Span<Rgba32> scrubRowSpan = scrubAccessor.GetRowSpan(y);
                                                            for (int x = 0; x < WorldMapGenerateMap.SIZE * 4; x++)
                                                            {
                                                                if (_clothMapTiles[x, y] == TileInfo.Scrubland)
                                                                {
                                                                    pixelRowSpan[x] = scrubRowSpan[x];
                                                                }
                                                            }
                                                        }
                                                    });

                                                    image.ProcessPixelRows(deep_water, medium_water, (imageAccessor, deepWaterAccessor, mediumWaterAccessor) =>
                                                    {
                                                        for (int y = 0; y < WorldMapGenerateMap.SIZE * 4; y++)
                                                        {
                                                            Span<Rgba32> pixelRowSpan = imageAccessor.GetRowSpan(y);
                                                            Span<Rgba32> deepWaterRowSpan = deepWaterAccessor.GetRowSpan(y);
                                                            Span<Rgba32> mediumWaterRowSpan = mediumWaterAccessor.GetRowSpan(y);
                                                            for (int x = 0; x < WorldMapGenerateMap.SIZE * 4; x++)
                                                            {
                                                                if (_clothMapTiles[x, y] == TileInfo.Deep_Water)
                                                                {
                                                                    pixelRowSpan[x] = deepWaterRowSpan[x];
                                                                }
                                                                else if (_clothMapTiles[x, y] == TileInfo.Medium_Water || _clothMapTiles[x, y] == TileInfo.Shallow_Water)
                                                                {
                                                                    pixelRowSpan[x] = mediumWaterRowSpan[x];
                                                                }
                                                            }
                                                        }
                                                    });

                                                    riversOverlay.ProcessPixelRows(riversOverlayAccessor =>
                                                    {
                                                        for (int y = 0; y < WorldMapGenerateMap.SIZE * 4; y++)
                                                        {
                                                            Span<Rgba32> riversOverlayRowSpan = riversOverlayAccessor.GetRowSpan(y);
                                                            for (int x = 0; x < WorldMapGenerateMap.SIZE * 4; x++)
                                                            {
                                                                riversOverlayRowSpan[x] = new Rgba32(riversOverlayRowSpan[x].R, riversOverlayRowSpan[x].G, riversOverlayRowSpan[x].B, rivers[x, y].A);
                                                            }
                                                        }
                                                    });

                                                    hillsOverlay.ProcessPixelRows(mountainsOverlay, (hillsOverlayAccessor, mountainsOverlayAccessor) =>
                                                    {
                                                        for (int y = 0; y < WorldMapGenerateMap.SIZE * 4; y++)
                                                        {
                                                            Span<Rgba32> hillsOverlayRowSpan = hillsOverlayAccessor.GetRowSpan(y);
                                                            Span<Rgba32> mountainsOverlayRowSpan = mountainsOverlayAccessor.GetRowSpan(y);
                                                            for (int x = 0; x < WorldMapGenerateMap.SIZE * 4; x++)
                                                            {
                                                                if (_mountainOverlay[x, y] != TileInfo.Hills || IsWater(_clothMapTiles[x, y]) || erosionMap[x, y] == 1 || erosionMap2[x, y] == 1)
                                                                {
                                                                    hillsOverlayRowSpan[x] = new Rgba32(0, 0, 0, 0);
                                                                }

                                                                if (_mountainOverlay[x, y] != TileInfo.Mountains ||
                                                                    IsWater(_clothMapTiles[x, y]) || erosionMap[x, y] == 1 || erosionMap2[x, y] == 1)
                                                                {
                                                                    mountainsOverlayRowSpan[x] = new Rgba32(0, 0, 0, 0);
                                                                }
                                                            }
                                                        }
                                                    });

                                                    outlineOverlay.ProcessPixelRows(outlineOverlayAccessor =>
                                                    {
                                                        for (int y = 0; y < WorldMapGenerateMap.SIZE * 4; y++)
                                                        {
                                                            Span<Rgba32> outlineOverlayRowSpan = outlineOverlayAccessor.GetRowSpan(y);
                                                            for (int x = 0; x < WorldMapGenerateMap.SIZE * 4; x++)
                                                            {
                                                                if ((erosionMap[x, y] == 1 || erosionMap2[x, y] == 1))
                                                                {
                                                                    outlineOverlayRowSpan[x] = new Rgba32(0, 0, 0);
                                                                }
                                                                else
                                                                {
                                                                    outlineOverlayRowSpan[x] = new Rgba32(0, 0, 0, 0);
                                                                }

                                                            }
                                                        }
                                                    });

                                                    image = image.Clone(ctx => ctx.DrawImage(hillsOverlay, PixelColorBlendingMode.Normal, PixelAlphaCompositionMode.SrcOver, 1));
                                                    image = image.Clone(ctx => ctx.DrawImage(mountainsOverlay, PixelColorBlendingMode.Normal, PixelAlphaCompositionMode.SrcOver, 1));
                                                    outlineOverlay.Mutate(ctx => ctx.GaussianBlur(0.8f));
                                                    image = image.Clone(ctx => ctx.DrawImage(outlineOverlay, PixelColorBlendingMode.Normal, PixelAlphaCompositionMode.SrcOver, 1));

                                                    image = image.Clone(ctx => ctx.DrawImage(riversOverlay, PixelColorBlendingMode.Normal, PixelAlphaCompositionMode.SrcOver, 1));
                                                    //image = image.Clone(ctx => ctx.DrawImage(rivers, PixelColorBlendingMode.Normal, PixelAlphaCompositionMode.SrcOver, 1));

                                                    var usedPixels = new int[SIZE * 4, SIZE * 4];
                                                    for (int x = 0; x < SIZE * 4; x++)
                                                    {
                                                        for (int y = 0; y < SIZE * 4; y++)
                                                        {
                                                            usedPixels[x, y] = 0;
                                                        }
                                                    }

                                                    image = ClothMapPlaceLocations(image, data, usedPixels);
                                                    image = ClothMapPlaceMoons(image, data, usedPixels);

                                                    FontCollection collection = new FontCollection();
                                                    using (var fontStream = new MemoryStream(ClothMap.runes))
                                                    {
                                                        FontFamily family = collection.Add(fontStream);
                                                        Font font = family.CreateFont(22, FontStyle.Regular);

                                                        DrawingOptions drawingOptions = new DrawingOptions()
                                                        {
                                                            GraphicsOptions = new SixLabors.ImageSharp.GraphicsOptions()
                                                            {
                                                            }
                                                        };

                                                        foreach (var region in Regions)
                                                        {
                                                            var x = region.Center.X;
                                                            var y = Math.Max(region.Center.Y - 5, 0);
                                                            TextOptions textOptions = new TextOptions(font)
                                                            {
                                                                KerningMode = KerningMode.Normal,
                                                                TabWidth = 8, // a tab renders as 8 spaces wide
                                                                              //WrapTextWidth = 100, // greater than zero so we will word wrap at 100 pixels wide
                                                                HorizontalAlignment = HorizontalAlignment.Center,
                                                                //Origin = new System.Numerics.Vector2(x * 4, y * 4)
                                                            };
                                                            FontRectangle size = TextMeasurer.Measure(region.RunicName.ToUpper(), textOptions);
                                                            //Console.WriteLine(size.Top.ToString() + " " + size.Left.ToString() + " " + size.Right.ToString() + " " + size.Bottom.ToString());

                                                            var offsetLinear = 0;
                                                            var yOffsetLinear = 0;
                                                            var yOffset = 0;
                                                            var xOffsetLinear = 0;
                                                            var xOffset = 0;


                                                            // Doing a cross pattern search right now. May want to change it to a spiral or the "find all and then get closest to origin" that the Locations do
                                                            while (OverlapsWithUsedPixels(usedPixels, size.Top + ((y + yOffset) * 4), size.Bottom + ((y + yOffset) * 4), (size.Right - size.Left) / -2 + ((x + xOffset) * 4), (size.Right - size.Left) / 2 + ((x + xOffset) * 4)))
                                                            {
                                                                offsetLinear++;
                                                                if (offsetLinear % 2 == 0)
                                                                {
                                                                    yOffsetLinear = yOffsetLinear + 1;
                                                                    yOffset = (yOffsetLinear * ((yOffsetLinear % 2 == 0) ? -1 : 1) / 2);
                                                                }
                                                                else
                                                                {
                                                                    xOffsetLinear = xOffsetLinear + 1;
                                                                    xOffset = (xOffsetLinear * ((xOffsetLinear % 2 == 0) ? -1 : 1) / 2);
                                                                }
                                                            }
                                                            y = WrapInt(y + yOffset, SIZE);
                                                            x = WrapInt(x + xOffset, SIZE);

                                                            MarkUsedPixels(usedPixels, size.Top + (y * 4), size.Bottom + (y * 4), (size.Right - size.Left) / -2 + (x * 4), (size.Right - size.Left) / 2 + (x * 4));
                                                            textOptions.Origin = new System.Numerics.Vector2(x * 4, y * 4);
                                                            image.Mutate(ctx => ctx.DrawText(drawingOptions, textOptions, region.RunicName.ToUpper(), Brushes.Solid(SixLabors.ImageSharp.Color.Black), null));
                                                            //image.Mutate(ctx => ctx.DrawPolygon(SixLabors.ImageSharp.Color.HotPink, 2.0f, new SixLabors.ImageSharp.PointF(region.Center.X * 4, region.Center.Y * 4), new SixLabors.ImageSharp.PointF(region.Center.X * 4 + 2, region.Center.Y * 4 + 2)));
                                                        }
                                                    }

                                                    image = ClothMapPlaceTags(image, random, usedPixels);

                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }            

            return image;
        }

        private bool OverlapsWithUsedPixels(int[,] usedPixels, float top, float bottom, float left, float right)
        {
            for(int x = (int)left; x < right; x++)
            {
                for(int y = (int)top; y < bottom; y++)
                {
                    // Off map is bad pixels
                    if (x < 0 || y < 0 || x >= SIZE * 4 || y >= SIZE * 4)
                    {
                        return true;
                    }
                    if (usedPixels[x,y] != 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private SixLabors.ImageSharp.Image<Rgba32> ClothMapPlaceTags(SixLabors.ImageSharp.Image<Rgba32> image, Random random, int[,] usedPixels)
        {
            var tagImages = new List<Tuple<SixLabors.ImageSharp.Image<Rgba32>, int, int>>();
            //FontRectangle size = TextMeasurer.Measure(region.RunicName.ToUpper(), new RendererOptions(font));
            
            //Console.WriteLine(size.Top.ToString() + " " + size.Left.ToString() + " " + size.Right.ToString() + " " + size.Bottom.ToString());
            tagImages.Add(new Tuple<SixLabors.ImageSharp.Image<Rgba32>, int, int>(SixLabors.ImageSharp.Image.Load<Rgba32>(ClothMap.banner), 0, 0));

            // Try to place the banner first. It is huge but most important.
            var possibleLocations = GetAllMatchingTiles(c => IsWater(c));
            List<ITile> evenlyDistributedLocations = GetEvenlyDistributedValidLocations(random, 32, usedLocations, possibleLocations, null, false);

            foreach(var tile in evenlyDistributedLocations)
            {
                if (ImageOnlyOverlapsWater(tagImages[0].Item1, tile.Y * 4, tile.X * 4, usedPixels))
                {
                    var point = new SixLabors.ImageSharp.Point(tile.X * 4, tile.Y * 4);
                    image = image.Clone(ctx => ctx.DrawImage(tagImages[0].Item1, point, PixelColorBlendingMode.Normal, PixelAlphaCompositionMode.SrcOver, 1));
                    MarkUsedPixels(point, usedPixels, tagImages[0].Item1);

                    evenlyDistributedLocations.Remove(tile);
                    break;
                }
            }

            tagImages = new List<Tuple<SixLabors.ImageSharp.Image<Rgba32>, int, int>>();
            tagImages.Add(new Tuple<SixLabors.ImageSharp.Image<Rgba32>,int,int>(SixLabors.ImageSharp.Image.Load<Rgba32>(ClothMap.nw_wind),0,0));
            var tagImage = SixLabors.ImageSharp.Image.Load<Rgba32>(ClothMap.ne_wind);
            tagImages.Add(new Tuple<SixLabors.ImageSharp.Image<Rgba32>, int, int>(tagImage, (256*4 - 1) - tagImage.Width, 0));
            tagImage = SixLabors.ImageSharp.Image.Load<Rgba32>(ClothMap.sw_wind);
            tagImages.Add(new Tuple<SixLabors.ImageSharp.Image<Rgba32>, int, int>(tagImage, 0, (256 * 4 - 1) - tagImage.Height));
            tagImage = SixLabors.ImageSharp.Image.Load<Rgba32>(ClothMap.se_wind);
            tagImages.Add(new Tuple<SixLabors.ImageSharp.Image<Rgba32>, int, int>(tagImage, (256 * 4 - 1) - tagImage.Width, (256 * 4 - 1) - tagImage.Height));
            tagImage = SixLabors.ImageSharp.Image.Load<Rgba32>(ClothMap.sw_serpent);
            tagImages.Add(new Tuple<SixLabors.ImageSharp.Image<Rgba32>, int, int>(tagImage, 16*4, (256 * 4 - 1) - tagImage.Height));
            tagImage = SixLabors.ImageSharp.Image.Load<Rgba32>(ClothMap.se_serpent);
            tagImages.Add(new Tuple<SixLabors.ImageSharp.Image<Rgba32>, int, int>(tagImage, (256 * 4 - 1) - tagImage.Width - (10*4), (256 * 4 - 1) - tagImage.Height));

            foreach (var tag in tagImages)
            {
                var imageToPlace = tag.Item1;
                var yOffset = tag.Item3;
                var xOffset = tag.Item2;
                
                if (ImageOnlyOverlapsWater(tag.Item1, tag.Item3, tag.Item2, usedPixels))
                {
                    var point = new SixLabors.ImageSharp.Point(xOffset, yOffset);
                    image = image.Clone(ctx => ctx.DrawImage(tag.Item1, point, PixelColorBlendingMode.Normal, PixelAlphaCompositionMode.SrcOver, 1));
                    MarkUsedPixels(point, usedPixels, tag.Item1);
                }
            }

            tagImages = new List<Tuple<SixLabors.ImageSharp.Image<Rgba32>, int, int>>();
            tagImages.Add(new Tuple<SixLabors.ImageSharp.Image<Rgba32>, int, int>(SixLabors.ImageSharp.Image.Load<Rgba32>(ClothMap.small_boat), 0, 0));
            tagImages.Add(new Tuple<SixLabors.ImageSharp.Image<Rgba32>, int, int>(SixLabors.ImageSharp.Image.Load<Rgba32>(ClothMap.big_boat), 0, 0));

            foreach (var tag in tagImages)
            {
                var evenlyDistributedLocationsCopy = evenlyDistributedLocations.ToList();
                foreach (var tile in evenlyDistributedLocationsCopy)
                {
                    if (ImageOnlyOverlapsWater(tag.Item1, tile.Y * 4, tile.X * 4, usedPixels))
                    {
                        var point = new SixLabors.ImageSharp.Point(tile.X * 4, tile.Y * 4);
                        image = image.Clone(ctx => ctx.DrawImage(tag.Item1, point, PixelColorBlendingMode.Normal, PixelAlphaCompositionMode.SrcOver, 1));
                        MarkUsedPixels(point, usedPixels, tag.Item1);

                        evenlyDistributedLocations.Remove(tile);
                        evenlyDistributedLocations = evenlyDistributedLocations.OrderByDescending(t => DistanceSquared(tile, t)).ToList();
                        break;
                    }
                    else
                    {
                        evenlyDistributedLocations.Remove(tile);
                    }
                }
            }

            return image;
        }

        private static void MarkUsedPixels(SixLabors.ImageSharp.Point point, int[,] usedPixels, SixLabors.ImageSharp.Image<Rgba32> image)
        {
            for (int x = point.X; x < image.Width + point.X; x++)
            {
                for (int y = point.Y; y < image.Height + point.Y; y++)
                {
                    if (image[x - point.X, y - point.Y].A != 0)
                    {
                        usedPixels[WrapInt(x, SIZE * 4), WrapInt(y, SIZE * 4)] = 1;
                    }
                }
            }
        }

        private void MarkUsedPixels(int[,] usedPixels, float top, float bottom, float left, float right)
        {
            for (int x = (int)left; x < right; x++)
            {
                for (int y = (int)top; y < bottom; y++)
                {
                    usedPixels[WrapInt(x, SIZE*4), WrapInt(y, SIZE*4)] = 1;
                }
            }
        }

        private bool ImageOnlyOverlapsWater(SixLabors.ImageSharp.Image<Rgba32> imageToPlace, int yOffset, int xOffset, int[,] usedPixels)
        {
            var allWater = true;

            imageToPlace.ProcessPixelRows(imageToPlaceAccessor =>
            {
                for (int y = yOffset; y < yOffset + imageToPlace.Height && allWater; y++)
                {
                    Span<Rgba32> rowSpan = imageToPlaceAccessor.GetRowSpan(y - yOffset);
                    for (int x = xOffset; x < xOffset + imageToPlace.Width && allWater; x++)
                    {
                        if (x >= SIZE * 4 || y >= SIZE * 4 || (rowSpan[x - xOffset].A != 0 && !IsWater(_clothMapTiles[x, y])) || (rowSpan[x - xOffset].A != 0 && usedPixels[x, y] == 1))
                        {
                            allWater = false;
                        }
                    }
                }
            });
            return allWater;
        }

        private SixLabors.ImageSharp.Image<Rgba32> ClothMapPlaceLocations(SixLabors.ImageSharp.Image<Rgba32> img2, UltimaData data, int[,] usedPixels)
        {
            var locImages = new List<SixLabors.ImageSharp.Image<Rgba32>>();
            locImages.Add(SixLabors.ImageSharp.Image.Load<Rgba32>(ClothMap.loc_1_castle_britannia));
            locImages.Add(SixLabors.ImageSharp.Image.Load<Rgba32>(ClothMap.loc_2_lycaeum));
            locImages.Add(SixLabors.ImageSharp.Image.Load<Rgba32>(ClothMap.loc_3_empath_abbey));
            locImages.Add(SixLabors.ImageSharp.Image.Load<Rgba32>(ClothMap.loc_4_serpents_hold));
            locImages.Add(SixLabors.ImageSharp.Image.Load<Rgba32>(ClothMap.loc_5_moonglow));
            locImages.Add(SixLabors.ImageSharp.Image.Load<Rgba32>(ClothMap.loc_6_britain));
            locImages.Add(SixLabors.ImageSharp.Image.Load<Rgba32>(ClothMap.loc_7_jhelom));
            locImages.Add(SixLabors.ImageSharp.Image.Load<Rgba32>(ClothMap.loc_8_yew));
            locImages.Add(SixLabors.ImageSharp.Image.Load<Rgba32>(ClothMap.loc_9_minoc));
            locImages.Add(SixLabors.ImageSharp.Image.Load<Rgba32>(ClothMap.loc_10_trinsic));
            locImages.Add(SixLabors.ImageSharp.Image.Load<Rgba32>(ClothMap.loc_11_skara_brae));


            SixLabors.ImageSharp.Point result = FindPointThatOverlapsTheLeast(data, locImages[0], data.LCB[0], usedPixels);
            img2 = img2.Clone(ctx => ctx.DrawImage(locImages[0], result, PixelColorBlendingMode.Normal, PixelAlphaCompositionMode.SrcOver, 1));
            MarkUsedPixels(result, usedPixels, locImages[0]);
            for (int i = 0; i < 3; i++)
            {
                result = FindPointThatOverlapsTheLeast(data, locImages[i+1], data.Castles[i], usedPixels);
                img2 = img2.Clone(ctx => ctx.DrawImage(locImages[i + 1], result, PixelColorBlendingMode.Normal, PixelAlphaCompositionMode.SrcOver, 1));
                MarkUsedPixels(result, usedPixels, locImages[i + 1]);
            }
            for (int i = 0; i < 7; i++)
            {
                result = FindPointThatOverlapsTheLeast(data, locImages[i + 4], data.Towns[i], usedPixels);
                img2 = img2.Clone(ctx => ctx.DrawImage(locImages[i+4], result, PixelColorBlendingMode.Normal, PixelAlphaCompositionMode.SrcOver, 1));
                MarkUsedPixels(result, usedPixels, locImages[i + 4]);
            }

            return img2;
        }

        private SixLabors.ImageSharp.Point FindPointThatOverlapsTheLeast(UltimaData data, SixLabors.ImageSharp.Image<Rgba32> image, ITile tile, int[,] usedPixels)
        {
            var minWater = Int32.MaxValue;
            var bestPoints = new List<SixLabors.ImageSharp.Point>();
            bestPoints.Add(new SixLabors.ImageSharp.Point(tile.X * 4 - (image.Width / 2), tile.Y * 4 - (image.Height / 2)));

            for (int xOffsetLinear = 0; xOffsetLinear < image.Width * 2; xOffsetLinear++)
            {
                for (int yOffsetLinear = 0; yOffsetLinear < image.Height * 2; yOffsetLinear++)
                {
                    var waterCount = 0;
                    var xOffset = (xOffsetLinear * ((xOffsetLinear % 2 == 0) ? -1 : 1) / 2);
                    var yOffset = (yOffsetLinear * ((yOffsetLinear % 2 == 0) ? -1 : 1) / 2);
                    for (int x = tile.X * 4 + xOffset - (image.Width/2); x < (tile.X * 4) + xOffset + (image.Width/2); x++)
                    {
                        for (int y = tile.Y * 4 + yOffset - (image.Height / 2); y < (tile.Y * 4) + yOffset + (image.Height/2); y++)
                        {
                            // Bad if outside of map
                            if (x < 0 || y < 0 || x >= SIZE * 4 || y >= SIZE * 4)
                            {
                                waterCount = waterCount + 3;
                            }
                            else
                            {
                                // Bad if over water
                                if (IsWater(_clothMapTiles[x, y]))
                                {
                                    waterCount = waterCount + 1;
                                }
                                // Bad if pixel already used
                                if (usedPixels[x, y] != 0)
                                {
                                    waterCount = waterCount + 1;
                                }
                            }
                        }
                    }

                    if (waterCount < minWater)
                    {
                        minWater = waterCount;
                        bestPoints = new List<SixLabors.ImageSharp.Point>();
                    }
                    if(waterCount == minWater)
                    {
                        bestPoints.Add(new SixLabors.ImageSharp.Point(tile.X * 4 + xOffset - (image.Width / 2), tile.Y * 4 + yOffset - (image.Height / 2)));
                    }
                }
            }
            var result = bestPoints.OrderBy(r => DistanceSquared(r.X, r.Y, tile.X * 4 - (image.Width / 2), tile.Y * 4 - (image.Height / 2))).First();
            return result;
        }

        private SixLabors.ImageSharp.Image<Rgba32> ClothMapPlaceMoons(SixLabors.ImageSharp.Image<Rgba32> img2, UltimaData data, int[,] usedPixels)
        {
            var moonImages = new List<SixLabors.ImageSharp.Image<Rgba32>>();
            moonImages.Add(SixLabors.ImageSharp.Image.Load<Rgba32>(ClothMap._1_new_moon));
            moonImages.Add(SixLabors.ImageSharp.Image.Load<Rgba32>(ClothMap._2_crescent_waxing));
            moonImages.Add(SixLabors.ImageSharp.Image.Load<Rgba32>(ClothMap._3_first_quarter));
            moonImages.Add(SixLabors.ImageSharp.Image.Load<Rgba32>(ClothMap._4_gibbous_waxing));
            moonImages.Add(SixLabors.ImageSharp.Image.Load<Rgba32>(ClothMap._5_full_moon));
            moonImages.Add(SixLabors.ImageSharp.Image.Load<Rgba32>(ClothMap._6_gibbous_waning));
            moonImages.Add(SixLabors.ImageSharp.Image.Load<Rgba32>(ClothMap._7_last_quarter));
            for(int i = 0; i < 7; i++)
            {
                var result = FindPointThatOverlapsTheLeast(data, moonImages[i], data.Moongates[i], usedPixels);
                img2 = img2.Clone(ctx => ctx.DrawImage(moonImages[i], result, PixelColorBlendingMode.Normal, PixelAlphaCompositionMode.SrcOver, 1));
                MarkUsedPixels(result, usedPixels, moonImages[i]);
            }

            return img2;
        }

        //https://stackoverflow.com/questions/14340083/image-processing-task-erosion-c-sharp
        public byte[,] ErosionMap(byte[,] map, byte[] tilesToNotToErode, byte[] tilesToErode)
        {
            int height = SIZE * 4;
            int width = SIZE * 4;
            byte[,] result = new byte[height,width];
            byte[,] src = new byte[height, width];
            for(int y = 0; y < height; y++)
            {
                for(int x = 0; x < width; x++)
                {
                    if(tilesToNotToErode.Contains(map[x,y]))
                    {
                        src[x, y] = 0;
                    }
                    else
                    {
                        src[x, y] = 1; 
                    }

                    if(tilesToErode.Contains(map[x,y]))
                    {
                        src[x, y] = 1;
                    }
                    else
                    {
                        src[x, y] = 0;
                    }
                }
            }

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var val = src[x, y];
                    //Erosion
                    for (int a = -1; a < 3; a++)
                    {
                        for (int b = -2; b < 2; b++)
                        {
                            try
                            {
                                var val2 = src[WrapInt(x + a,width), WrapInt(y + b,height)];
                                val = Math.Min(val, val2);
                            }
                            catch
                            {
                            }
                        }
                    }
                    if (src[x, y] != val)
                    {
                        result[x, y] = 1;
                    }
                    else
                    {
                        result[x, y] = 0;
                    }
                }
            }

            return result;
        }

        public new SixLabors.ImageSharp.Image ToHeightMapImage()
        {
            var image = new SixLabors.ImageSharp.Image<Rgba32>(WorldMapGenerateMap.SIZE*2, WorldMapGenerateMap.SIZE*2);
            image.ProcessPixelRows(imageAccessor =>
            {
                for (int y = 0; y < WorldMapGenerateMap.SIZE; y++)
                {
                    Span<Rgba32> pixelRowSpan = imageAccessor.GetRowSpan(y);
                    for (int x = 0; x < WorldMapGenerateMap.SIZE; x++)
                    {
                        var val = (byte)((0 + (((Byte.MaxValue - 0) / (_generatedMax - _generatedMin)) * (_worldMapGenerated[x, y] - _generatedMin))));

                        //SixLabors.ImageSharp.Color.FromRgb(0, 0, 112)
                        pixelRowSpan[x] = SixLabors.ImageSharp.Color.FromRgb(val, val, val);
                    }

                    for (int x = 0; x < WorldMapGenerateMap.SIZE; x++)
                    {
                        var val = (byte)((0 + (((Byte.MaxValue - 0) / (_mountainMax - _mountainMin)) * (_mountHeightMap[x, y] - _mountainMin))));

                        //SixLabors.ImageSharp.Color.FromRgb(0, 0, 112)
                        pixelRowSpan[SIZE + x] = SixLabors.ImageSharp.Color.FromRgb(val, val, val);
                    }
                }

                for (int y = 0; y < WorldMapGenerateMap.SIZE; y++)
                {
                    Span<Rgba32> pixelRowSpan = imageAccessor.GetRowSpan(y + SIZE);
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
            });

            return image;
        }

        public static void PrintWorldMapInfo(string path)
        {
            var world = new byte[256 * 256];

            var file = System.IO.Path.Combine(path, filename);
            using (FileStream stream = new FileStream(file, FileMode.Open))
            {
                stream.Read(world, 0, 256 * 256);
            }
            var worldList = world.ToList();

            worldList.Sort();

            var worldTileCount = new Dictionary<byte, int>();
            for (int i = 0; i < worldList.Count(); i++)
            {
                if (worldTileCount.ContainsKey(worldList[i]))
                {
                    worldTileCount[worldList[i]] = worldTileCount[worldList[i]] + 1;
                }
                else
                {
                    worldTileCount[worldList[i]] = 1;
                }
            }

            foreach (var key in worldTileCount.Keys)
            {
                //var output = $"{shapes[key]}:".PadRight(31) + $" {worldTileCount[key]/(256.0*256.0)}";
                var output = $"{{{key},{worldTileCount[key] / (256.0 * 256.0)}}}";
                Console.WriteLine(output);
            }
        }

        static private Dictionary<byte, double> _percentInMap = new Dictionary<byte, double>()
        {
            // With Avatar Isle
            //{TileInfo.Deep_Water,   0.519012451171875},
            //{TileInfo.Medium_Water, 0.15771484375},
            //{TileInfo.Shallow_Water,0.0294952392578125},
            //{TileInfo.Swamp,        0.010162353515625},
            //{TileInfo.Grasslands,   0.1092376708984375},
            //{TileInfo.Scrubland,    0.07513427734375},
            //{TileInfo.Forest,       0.03515625},
            //{TileInfo.Hills,        0.0355224609375},
            //{TileInfo.Mountains,    0.0266265869140625},
            ////{9,0.0001068115234375},
            ////{10,0.0001068115234375},
            ////{11,4.57763671875E-05},
            ////{12,6.103515625E-05},
            ////{13,1.52587890625E-05},
            ////{14,1.52587890625E-05},
            ////{15,1.52587890625E-05},
            ////{23,0.0002593994140625},
            ////{29,1.52587890625E-05},
            ////{30,0.0001068115234375},
            ////{61,1.52587890625E-05},
            ////{70,0.0001068115234375},
            ////Lava{76,0.001068115234375}

            // Without Avatar Isle
            {TileInfo.Deep_Water,   0.54730224609375},
            {TileInfo.Medium_Water, 0.146514892578125},
            {TileInfo.Shallow_Water,0.026275634765625},
            {TileInfo.Swamp,        0.0097198486328125},
            {TileInfo.Grasslands,   0.1090240478515625},
            {TileInfo.Scrubland,    0.07513427734375},
            {TileInfo.Forest,       0.03515625},
            {TileInfo.Hills,        0.03271484375},
            {TileInfo.Mountains,    0.0171966552734375},
            //{9,9.1552734375E-05},
            //{10,0.0001068115234375},
            //{11,4.57763671875E-05},
            //{12,6.103515625E-05},
            //{13,1.52587890625E-05},
            //{14,1.52587890625E-05},
            //{15,1.52587890625E-05},
            //{23,0.0002593994140625},
            //{29,1.52587890625E-05},
            //{30,9.1552734375E-05},
            //{61,1.52587890625E-05},
            //{70,4.57763671875E-05},
            //{76,0.00018310546875}
        };

    }
}
