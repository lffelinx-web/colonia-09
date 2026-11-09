using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Colonia09.Presentation
{
    [Serializable]
    public sealed class SectorProgress
    {
        public string id;
        public int stars;
        public int wins, attempts;
    }

    /// <summary>Campaign progress, kept as JSON in the app's private storage on the phone.</summary>
    [Serializable]
    public sealed class SaveData
    {
        public int version = 1;
        public List<SectorProgress> sectors = new List<SectorProgress>();
        /// <summary>Enemy kinds already introduced with their card, so each card shows only once.</summary>
        public List<string> seenEnemies = new List<string>();
        /// <summary>Sectors whose guided tutorial was completed.</summary>
        public List<string> tutorials = new List<string>();
        /// <summary>One-time tips already shown.</summary>
        public List<string> tips = new List<string>();

        // Colony.
        public double materials, data, water, food, energy, population;
        public List<BuildingSave> buildings = new List<BuildingSave>();
        /// <summary>Zones whose energy link to the colony has been built.</summary>
        public List<string> connected = new List<string>();
        /// <summary>UTC ticks of the last time production was added; 0 before the first building.</summary>
        public long productionTicks;
    }

    [Serializable]
    public sealed class BuildingSave
    {
        public string sector, type;
        /// <summary>Tile on the isometric colony grid. Older saves used a lot number in slot instead.</summary>
        public int slot, level, x, y;
    }

    public static class SaveGame
    {
        static SaveData data;
        static string PathOnDisk => Path.Combine(Application.persistentDataPath, "colonia09_save.json");

        public static SaveData Data => data ?? (data = Load());

        static SaveData Load()
        {
            try
            {
                if (File.Exists(PathOnDisk))
                {
                    var loaded = JsonUtility.FromJson<SaveData>(File.ReadAllText(PathOnDisk));
                    if (loaded != null) return loaded;
                }
            }
            catch (Exception e)
            {
                // A damaged file must never block the game: keep a copy and start over.
                Debug.LogWarning("Save file unreadable, starting fresh: " + e.Message);
                try { File.Copy(PathOnDisk, PathOnDisk + ".bad", true); } catch (Exception) { }
            }
            return new SaveData();
        }

        public static void Save()
        {
            try
            {
                string temp = PathOnDisk + ".tmp";  // Write then swap, so a crash never leaves half a file.
                File.WriteAllText(temp, JsonUtility.ToJson(Data, true));
                if (File.Exists(PathOnDisk)) File.Delete(PathOnDisk);
                File.Move(temp, PathOnDisk);
            }
            catch (Exception e)
            {
                Debug.LogWarning("Could not save progress: " + e.Message);
            }
        }

        public static SectorProgress Sector(string id)
        {
            var found = Data.sectors.Find(s => s.id == id);
            if (found == null)
            {
                found = new SectorProgress { id = id };
                Data.sectors.Add(found);
            }
            return found;
        }

        public static int Stars(string id) => Data.sectors.Find(s => s.id == id)?.stars ?? 0;
        public static bool Completed(string id) => Stars(id) > 0;

        public static int TotalStars()
        {
            int total = 0;
            foreach (var s in Data.sectors) total += s.stars;
            return total;
        }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        /// <summary>Test helper: forget all progress, in memory and on disk.</summary>
        public static void ResetForTests()
        {
            data = new SaveData();
            Colony.ResetForTests();
            try { if (File.Exists(PathOnDisk)) File.Delete(PathOnDisk); } catch (Exception) { }
        }
#endif
    }
}
