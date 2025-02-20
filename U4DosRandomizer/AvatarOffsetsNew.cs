﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace U4DosRandomizer
{
    public class AvatarOffsetsNew : IAvatarOffset
    {
        public AvatarOffsetsNew(byte[] avatarBytes, string originalFile)
        {
            // Check offsets
            byte[] originalAvatarBytes = null;
            using (var avatarStream = new System.IO.FileStream(originalFile, System.IO.FileMode.Open))
            {
                originalAvatarBytes = avatarStream.ReadAllBytes();
            }

            var originalOffsets = new AvatarOffsetsOriginal();

            PropertyInfo[] properties = this.GetType().GetInterface("IAvatarOffset").GetProperties();
            foreach (PropertyInfo pi in properties)
            {
                if(!pi.Name.ToLower().Contains("blink") && !pi.Name.ToLower().Contains("enable") && !pi.Name.ToLower().Contains("encoded") && !pi.Name.ToLower().Contains("seed") && !pi.Name.ToLower().Contains("pointers") && !pi.Name.Contains("BELL_REQUIREMENT_OFFSET"))
                {
                    var newValue = avatarBytes[(int)pi.GetValue(this, null)];
                    var oldValue = originalAvatarBytes[(int)pi.GetValue(originalOffsets, null)];
                    if (newValue != oldValue)
                    {
                        throw new Exception($"Offset {pi.Name} appears to be wrong.");
                    }
                }
                else if (pi.Name.ToLower().Contains("blink_cast"))
                {
                    var newValue = avatarBytes[(int)pi.GetValue(this, null)];
                    if(!(newValue == 0xc0 || newValue == 0xff))
                    {
                        throw new Exception($"Offset {pi.Name} appears to be wrong.");
                    }
                }
                else if (pi.Name.ToLower().Contains("blink_destination"))
                {
                    var newValue = avatarBytes[(int)pi.GetValue(this, null)];
                    if (!(newValue == 0x01 || newValue == 0x02))
                    {
                        throw new Exception($"Offset {pi.Name} appears to be wrong.");
                    }
                }
                else if (pi.Name.ToLower().Contains("enable"))
                {
                    var newValue = avatarBytes[(int)pi.GetValue(this, null)];
                    if (newValue != 0x08 && newValue != 0x09)
                    {
                        throw new Exception($"Offset {pi.Name} appears to be wrong.");
                    }
                }
                else if (pi.Name.Contains("BELL_REQUIREMENT_OFFSET"))
                {
                    var newValue = avatarBytes[(int)pi.GetValue(this, null)];
                    if (newValue != 0x04)
                    {
                        throw new Exception($"Offset {pi.Name} appears to be wrong.");
                    }
                }
                else if(pi.Name.Contains("MANTRA_POINTERS_OFFSET"))
                {
                    var newValue = avatarBytes[(int)pi.GetValue(this, null)-1];
                    if (newValue != 0x00)
                    {
                        throw new Exception($"Offset {pi.Name} appears to be wrong.");
                    }
                }                
                else if (pi.Name.Contains("ENCODED_FLAGS_OFFSET") || pi.Name.Contains("SEED_OFFSET"))
                {
                    var newValue = avatarBytes[(int)pi.GetValue(this, null)];
                    if (newValue != 0x20)
                    {
                        throw new Exception($"Offset {pi.Name} appears to be wrong.");
                    }
                }
                else if (pi.Name.Contains("TAVERN_TEXT_OFFSET"))
                {
                    var newValue = avatarBytes[(int)pi.GetValue(this, null)];
                    if (newValue != 0x41)
                    {
                        throw new Exception($"Offset {pi.Name} appears to be wrong.");
                    }
                }
                else if (pi.Name.Contains("POINTERS_OFFSET") && pi.Name.Contains("PRINCIPLE_ITEM"))
                {
                    // Not sure how to test these
                }
                else
                {
                    throw new NotImplementedException($"Offset {pi.Name} not being tested.");
                }
            }
        }

        public int ABYSS_PARTY_COMPARISON { get; } = 0x34BB; //  34AB
        public int LB_PARTY_COMPARISON { get; } = 0xE8D2; // E449
        public int BELL_REQUIREMENT_OFFSET { get; } = 0x4E0; // New
        public int BOOK_REQUIREMENT_OFFSET { get; } = 0x52A; // 6DB
        public int CANDLE_REQUIREMENT_OFFSET { get; } = 0x56F; // 720
        public int MOONGATE_X_OFFSET { get; } = 0x0fffa; //0fad1
        public int MOONGATE_Y_OFFSET { get; } = 0x10002; //fad9
        public int AREA_X_OFFSET { get; } = 0x1002a; //fb01 // towns, cities, castles, dungeons, shrines
        public int AREA_Y_OFFSET { get; } = 0x1004a; //fb21

        public int DEATH_EXIT_X_OFFSET { get; } = 0x0ffb; //11ac
        public int DEATH_EXIT_Y_OFFSET { get; } = 0x1000; //11b1

        public int PIRATE_COVE_X_OFFSET { get; } = 0x100d2; //fba9 // length 8
        public int PIRATE_COVE_Y_OFFSET { get; } = 0x100da; //fbb1 // length 8
        public int PIRATE_COVE_SHIP_TILES { get; } = 0x100e2; //fbb9 // length 8 (Direction pirates are facing)
        public int PIRATE_COVE_SPAWN_TRIGGER_Y_OFFSET1 { get; } = 0x03094; //3084
        public int PIRATE_COVE_SPAWN_TRIGGER_X_OFFSET1 { get; } = 0x0309B; //308B
        public int PIRATE_COVE_SPAWN_TRIGGER_Y_OFFSET2 { get; } = 0x03133; //3123
        public int PIRATE_COVE_SPAWN_TRIGGER_X_OFFSET2 { get; } = 0x0313A; //312A
        public int WORD_OF_PASSAGE { get; } = 0x10A19; // 104F0
        public int MONSTER_HP_OFFSET { get; } = 0x11C1C; //11685 // length 52
        public int MONSTER_LEADER_TYPES_OFFSET { get; } = 0x11C50; //116b9 // length 36
        public int MONSTER_ENCOUNTER_SIZE_OFFSET { get; } = 0x11C74; //116dd // length 36
        public int ALTAR_EXIT_DESTINATION { get; } = 0x11E64; //118c5 // length 12 : altar room exit destinations 
        /*
         *     0-3 { get; } = truth (north, east, south, west)
         *     4-7 { get; } = love
         *     8-11 { get; } = courage
         */
        public int AMBUSH_MONSTER_TYPES { get; } = 0x11F02; //11963 //length 8 : ambush monster types
        public int CITY_RUNE_MASK_PAIRS_OFFSET { get; } = 0x1215C; //11baf // length 16 : city/runemask pairs (city id, corresponding rune bitmask)
        public int ITEM_LOCATIONS_OFFSET { get; } = 0x12178; //11bcb // length 120 : 24 five-byte item location records (see below)
        /*
         * Each item location record has the following structure:

            Offset	Length (in bytes)	Purpose
            0x0	1	Item Location (same encoding as party location in PARTY.SAV, e.g. 0 for surface)
            0x1	1	X Coordinate of Item
            0x2	1	Y Coordinate of Item
            0x3	2	 ??? (a pointer?)
         */

        public int MONSTER_DAMAGE_BITSHIFT_OFFSET { get; } = 0x9D04; // 98E6
        public int WEAPON_DAMAGE_OFFSET { get; } = 0x11C9A; // 11703
        public int MONSTER_SPAWN_TIER_ONE { get; } = 0x5CAF; // 5B68
        public int MONSTER_SPAWN_TIER_TWO { get; } = 0x5CCA; // 5B83
        public int MONSTER_SPAWN_TIER_THREE { get; } = 0x5D02; // 5BBB
        //https://github.com/ergonomy-joe/u4-decompiled/blob/1964651295232b0ca39afafef254541a406eb66b/SRC/U4_COMBC.C#L210
        public int MONSTER_QTY_ONE { get; } = 0x84D9; // 80EF
        public int MONSTER_QTY_TWO { get; } = 0x84EA; // 8100
        public int HERB_PRICES { get; } = 0x139E8; // 1340B
        public int HERB_PRICE_INPUT { get; } = 0xD258; // CDFD
        public int TAVERN_TEXT_OFFSET { get; } = 0x154C4; // 14EA7
        public int LB_TEXT_OFFSET { get; } = 0x15CE7; // 156ca
        public int LB_HELP_TEXT_OFFSET { get; } = 0x168F2; // 162D4
        public int MANTRA_OFFSET { get; } = 0x173F2; //16DD4
        public int MANTRA_POINTERS_OFFSET { get; } = 0x17BB2; // 17594
        public int SHRINE_TEXT_OFFSET { get; } = 0x17410; //16df2

        public int WHITE_STONE_LOCATION_TEXT { get; } = 0x17A52; //17434
        public int BLACK_STONE_LOCATION_TEXT { get; } = 0x17B17; //174F9

        public int SHOP_LOCATION_OFFSET { get; } = 0x1252C; //11F7F

        public int DEMON_SPAWN_TRIGGER_X1_OFFSET { get; } = 0x2F1C; //2F17 !!! e5
        public int DEMON_SPAWN_TRIGGER_X2_OFFSET { get; } = 0x2F20; //2F1E !!! ea
        public int DEMON_SPAWN_TRIGGER_Y1_OFFSET { get; } = 0x2F31; //2F25 !!! d4
        public int DEMON_SPAWN_TRIGGER_Y2_OFFSET { get; } = 0x2F35; //2F2C !!! d9
        public int DEMON_SPAWN_LOCATION_X_OFFSET { get; } = 0x29EC; //29EA

        public int BALLOON_SPAWN_TRIGGER_X_OFFSET { get; } = 0x29AA; //29A8
        public int BALLOON_SPAWN_TRIGGER_Y_OFFSET { get; } = 0x29B1; //29AF

        public int BALLOON_SPAWN_LOCATION_X_OFFSET { get; } = 0x29C0; //29BE
        public int BALLOON_SPAWN_LOCATION_Y_OFFSET { get; } = 0x29C5; //29C3

        public int LBC_DUNGEON_EXIT_X_OFFSET { get; } = 0x47F0; //4766
        public int LBC_DUNGEON_EXIT_Y_OFFSET { get; } = 0x47F5; //476B

        public int ITEM_USE_TRIGGER_BELL_X_OFFSET { get; } = 0x04D1; //693
        public int ITEM_USE_TRIGGER_BELL_Y_OFFSET { get; } = 0x04D8; //69A
        public int ITEM_USE_TRIGGER_BOOK_X_OFFSET { get; } = 0x051B; //6CC
        public int ITEM_USE_TRIGGER_BOOK_Y_OFFSET { get; } = 0x0522; //6D3
        public int ITEM_USE_TRIGGER_CANDLE_X_OFFSET { get; } = 0x0560; //711
        public int ITEM_USE_TRIGGER_CANDLE_Y_OFFSET { get; } = 0x0567; //718
        public int ITEM_USE_TRIGGER_SKULL_X_OFFSET { get; } = 0x0632; //7E3
        public int ITEM_USE_TRIGGER_SKULL_Y_OFFSET { get; } = 0x0639; //7EA

        public int WEAPON_REQUIRED_FOR_ABYSS { get; } = 0x0645A; // 6223

        public int WHIRLPOOL_EXIT_X_OFFSET { get; } = 0x7E16; //7A92
        public int WHIRLPOOL_EXIT_Y_OFFSET { get; } = 0x7E1B; //7A97

        public int USE_PRINCIPLE_ITEM_TEXT { get; } = 0xF9FB; //F568
        public int USE_PRINCIPLE_ITEM_BELL_TEXT_POINTERS_OFFSET { get; } = 0x04F7; // 6AB
        public int USE_PRINCIPLE_ITEM_BOOK_TEXT_POINTERS_OFFSET { get; } = 0x0538; // 6E9
        public int USE_PRINCIPLE_ITEM_CANDLE_TEXT_POINTERS_OFFSET { get; } = 0x057D; // 72E

        public int ABYSS_EJECTION_LOCATIONS_X { get; } = 0x103D6; //FEAD  // Length 13 - Exit coords for when you fail tests in the Abyss https://github.com/ergonomy-joe/u4-decompiled/blob/c2c2108fa3bb346bcd1d8c207c526f33a4c8f5ef/SRC/U4_END.C#L37
        public int ABYSS_EJECTION_LOCATIONS_Y { get; } = 0x103E4; //FEBB

        public int SPELL_RECIPE_OFFSET { get; } = 0x11FD6; //11A29

        public static int RUNE_IMAGE_INDEX2 { get; } = 0x100AE; // FB85
        public static int RUNE_IMAGE_INDEX { get; } = 0x17B6F; // 17551

        public int BLINK_CAST_EXCLUSION_X1_OFFSET { get; } = 0x6A9E; // New : C0

        public int BLINK_CAST_EXCLUSION_X2_OFFSET { get { return BLINK_CAST_EXCLUSION_X1_OFFSET + 4; } } // New

        public int BLINK_CAST_EXCLUSION_Y1_OFFSET { get; } = 0x6AB3; // New : C0

        public int BLINK_CAST_EXCLUSION_Y2_OFFSET { get { return BLINK_CAST_EXCLUSION_Y1_OFFSET + 4; } } // New


        public int BLINK_DESTINATION_EXCLUSION_X1_OFFSET { get; } = 0x6B38; // New : 01

        public int BLINK_DESTINATION_EXCLUSION_X2_OFFSET { get { return BLINK_DESTINATION_EXCLUSION_X1_OFFSET + 4; } }  // New

        public int BLINK_DESTINATION_EXCLUSION_Y1_OFFSET { get; } = 0x6B57; // New : 01

        public int BLINK_DESTINATION_EXCLUSION_Y2_OFFSET { get { return BLINK_DESTINATION_EXCLUSION_Y1_OFFSET + 4; } } // New

        public int BLINK_DESTINATION2_EXCLUSION_X1_OFFSET { get; } = 0x6B7A; // New : 01

        public int BLINK_DESTINATION2_EXCLUSION_X2_OFFSET { get { return BLINK_DESTINATION2_EXCLUSION_X1_OFFSET + 4; } } // New

        public int BLINK_DESTINATION2_EXCLUSION_Y1_OFFSET { get; } = 0x6B9D; // New : 01

        public int BLINK_DESTINATION2_EXCLUSION_Y2_OFFSET { get { return BLINK_DESTINATION2_EXCLUSION_Y1_OFFSET + 4; } }  // New

        public int ENABLE_TOWN_SAVE1 { get; } = 0x405E; // New : 08
        public int ENABLE_TOWN_SAVE2 { get; } = 0x471C; // New : 08
        public int ENABLE_TOWN_SAVE3 { get; } = 0x4816; // New : 08
        public int ENABLE_TOWN_SAVE4 { get; } = 0x74A1; // New : 08
        public int ENABLE_MIX_QUANTITY_OFFSET { get; } = 0x922A; // New : 08

        public int ENABLE_SLEEP_BACKOFF_OFFSET { get; } = 0xA38D; // New : 08
        public int ENABLE_DAEMON_TRIGGER_FIX { get; } = 0x8007; // New : 08
        public int ENABLE_MAP_EDGE_FIX1 { get; } = 0x56AF; // New : 08
        public int ENABLE_MAP_EDGE_FIX2 { get; } = 0x59D2; // New : 08
        public int ENABLE_MAP_EDGE_FIX3 { get; } = 0x83C1; // New : 08
        public int ENABLE_AWAKEN_ALL { get; } = 0x69C6; // New : 08

        public int ENABLE_ACTIVE_PLAYER_1_OFFSET { get; } = 0x5F73; // New : 08

        public int ENABLE_HIT_CHANCE_OFFSET { get; } = 0x647D; // New : 08

        public int ENABLE_DIAGONAL_ATTACK_OFFSET { get; } = 0x6631; // New : 08

        public int ENABLE_SACRIFICE_FIX_OFFSET { get; } = 0xAA5E; // New : 08

        public int ENABLE_PRINCIPLE_ITEM_REORDER_OFFSET { get; } = 0x4E9; // New : E8

        public int ENABLE_WEAPON_OVERFLOW_FIX { get; } = 0xD378; // New 08

        public int ENCODED_FLAGS_OFFSET { get; } = 0xFE43; // New : 20

        public int SEED_OFFSET { get; } = 0xFE23; // New : 20

        public const byte Reagent_ash = (0x80 >> 0);
        public const byte Reagent_ginseng = (0x80 >> 1);
        public const byte Reagent_garlic = (0x80 >> 2);
        public const byte Reagent_spiderSilk = (0x80 >> 3);
        public const byte Reagent_bloodMoss = (0x80 >> 4);
        public const byte Reagent_blackPearl = (0x80 >> 5);
        public const byte Reagent_nightshade = (0x80 >> 6);
        public const byte Reagent_mandrake = (0x80 >> 7);
    }
}
