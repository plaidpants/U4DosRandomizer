﻿using Octodiff.Core;
using Octodiff.Diagnostics;
using System;
using System.IO;
using U4DosRandomizer.Helpers;
using U4DosRandomizer.Resources;

namespace U4DosRandomizer
{
    public class Title
    {
        private byte[] titleBytes;
        private const string filename = "TITLE.EXE";

        public void Load(string path, UltimaData data)
        {
            var file = Path.Combine(path, filename);

            FileHelper.TryBackupOriginalFile(file);

            // Apply delta file to create new file
            var newFilePath2 = file;
            var newFileOutputDirectory = Path.GetDirectoryName(newFilePath2);
            if (!Directory.Exists(newFileOutputDirectory))
                Directory.CreateDirectory(newFileOutputDirectory);
            var deltaApplier = new DeltaApplier { SkipHashCheck = false };
            using (var basisStream = new FileStream($"{file}.orig", FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                using (var deltaStream = new MemoryStream(Patches.TITLE_EXE))
                {
                    using (var newFileStream = new FileStream(newFilePath2, FileMode.Create, FileAccess.ReadWrite, FileShare.Read))
                    {
                        deltaApplier.Apply(basisStream, new BinaryDeltaReader(deltaStream, new ConsoleProgressReporter()), newFileStream);
                    }
                }
            }

            using (var titleStream = new System.IO.FileStream(file, System.IO.FileMode.Open))
            {
                titleBytes = titleStream.ReadAllBytes();
            }

            for (int offset = 0; offset < 8; offset++)
            {
                data.StartingPositions.Add(new Coordinate(titleBytes[START_X_OFFSET + offset], titleBytes[START_Y_OFFSET + offset]));
            }

            for (int offset = 0; offset < 8; offset++)
            {
                data.StartingKarma.Add(titleBytes[KARMA_OVERRIDE_VALUES_OFFSET + offset]);
            }
        }

        //public Dictionary<string, string> ReadHashes()
        //{
        //    var file = Path.Combine("hashes", "title_hash.json");
        //    var hashJson = System.IO.File.ReadAllText(file);

        //    var hashes = JsonConvert.DeserializeObject<Dictionary<string, string>>(hashJson);

        //    return hashes;
        //}

        //public void WriteHashes(string path)
        //{
        //    var file = Path.Combine(path, filename);

        //    var townTalkHash = new Dictionary<string, string>();

        //    var hash = HashHelper.GetHashSha256(file);
        //    Console.WriteLine($"{file}: {HashHelper.BytesToString(hash)}");
        //    townTalkHash.Add(Path.GetFileName(file), HashHelper.BytesToString(hash));

        //    string json = JsonConvert.SerializeObject(townTalkHash); // the dictionary is inside client object
        //                                                             //write string to file
        //    System.IO.File.WriteAllText(@"title_hash.json", json);
        //}

        public void Update(UltimaData data, Flags flags)
        {
            for (int offset = 0; offset < 8; offset++)
            {
                titleBytes[START_X_OFFSET + offset] = data.StartingPositions[offset].X;
                titleBytes[START_Y_OFFSET + offset] = data.StartingPositions[offset].Y;
            }

            titleBytes[ENABLE_KARMA_OVERRIDE_OFFSET] = flags.KarmaSetPercentage > 0 ? (byte)0x0 : (byte)0x9;

            for (int offset = 0; offset < 8; offset++)
            {
                titleBytes[KARMA_OVERRIDE_VALUES_OFFSET + offset] = (data.StartingKarma[offset] == 100 ? (byte)0 : data.StartingKarma[offset]);
            }
        }

        public void Save(string path)
        {
            var file = Path.Combine(path, filename);
            using (var titleOut = new System.IO.BinaryWriter(new System.IO.FileStream(file, System.IO.FileMode.Truncate)))
            {
                titleOut.Write(titleBytes);
            }
        }

        public static int START_X_OFFSET = 0x710C; //0x70dc;
        public static int START_Y_OFFSET = 0x7114; //0x70e4;

        public static int ENABLE_KARMA_OVERRIDE_OFFSET = 0x2E99;
        public static int KARMA_OVERRIDE_VALUES_OFFSET = 0x711C;

        internal static void Restore(string path)
        {
            var file = Path.Combine(path, filename);
            FileHelper.Restore(file);
        }
    }
}
