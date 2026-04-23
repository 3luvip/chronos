using Godot;
namespace Player
{
    // ─────────────────────────────────────────────────────────────────────────
    // 6. SIMPLE JSON PARSER STUB — replace with Newtonsoft.Json or System.Text.Json
    // ─────────────────────────────────────────────────────────────────────────
 
    // These types match the DB schema: INSERT INTO parts (id, type, pi)
    public record PartRecord(int Id, int Type, PiEntry[] Pi);
    public record PiEntry(int Id, int Dx, int Dy);
 
    public static class SimpleJsonParser
    {
        // Stub — in production use JsonSerializer.Deserialize<PartRecord[]>(json)
        public static PartRecord[] ParsePartArray(string json)
        {
            GD.Print("[SimpleJsonParser] Parsing part JSON — replace with real JSON parser.");
            return System.Array.Empty<PartRecord>();
        }
    }
}