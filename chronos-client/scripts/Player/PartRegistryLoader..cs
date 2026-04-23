using Godot;
using System;
using System.Collections.Generic;
using Map;
using Chronos.Core.Domain.Character;
namespace Player
{
    // ─────────────────────────────────────────────────────────────────────────
    // 4. PART REGISTRY LOADER — reads from DB JSON / server packet
    //    Converts legacy SQL INSERT rows into EquipmentPart objects
    // ─────────────────────────────────────────────────────────────────────────
 
    /// <summary>
    /// Populates InMemoryPartRegistry from the legacy DB format:
    ///   INSERT INTO parts (id, type, pi) VALUES (N, T, '[{"dx":0,"dy":0,"id":17},...]')
    /// </summary>
    public static class PartRegistryLoader
    {
        // Call once at startup after loading part data from server
        public static InMemoryPartRegistry LoadFromJson(string json)
        {
            var reg = new InMemoryPartRegistry();
 
            // Parse the JSON array of part records
            // Each record: { "id": int, "type": int, "pi": [ { "dx":, "dy":, "id": } ] }
            var partRecords = SimpleJsonParser.ParsePartArray(json);
            foreach (var record in partRecords)
            {
                var images = new PartImageEntry[record.Pi.Length];
                for (int i = 0; i < record.Pi.Length; i++)
                    images[i] = new PartImageEntry(record.Pi[i].Id,
                                                   record.Pi[i].Dx,
                                                   record.Pi[i].Dy);
 
                reg.Register(new EquipmentPart(record.Id, record.Type, images));
            }
 
            return reg;
        }
    }
}