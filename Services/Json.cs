using System.Text.Json;
using System.Text.Json.Serialization;
using LiftX.Models;

namespace LiftX.Services;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true,
    UseStringEnumConverter = true,
    NumberHandling = JsonNumberHandling.AllowReadingFromString)]
[JsonSerializable(typeof(SaveFile))]
[JsonSerializable(typeof(Workout))]
[JsonSerializable(typeof(StoredMeta))]
[JsonSerializable(typeof(ThemeColors))]
internal partial class AppJson : JsonSerializerContext
{
}

public record ThemeColors(string Bg, string Accent);

public static class Json
{
    public static string Serialize(SaveFile data) => JsonSerializer.Serialize(data, AppJson.Default.SaveFile);
    public static string Serialize(Workout w) => JsonSerializer.Serialize(w, AppJson.Default.Workout);
    public static string Serialize(StoredMeta m) => JsonSerializer.Serialize(m, AppJson.Default.StoredMeta);
    public static string Serialize(ThemeColors t) => JsonSerializer.Serialize(t, AppJson.Default.ThemeColors);

    public static SaveFile? ReadSave(string json) => JsonSerializer.Deserialize(json, AppJson.Default.SaveFile);
    public static Workout? ReadWorkout(string json) => JsonSerializer.Deserialize(json, AppJson.Default.Workout);
    public static StoredMeta? ReadMeta(string json) => JsonSerializer.Deserialize(json, AppJson.Default.StoredMeta);

    public static Workout Clone(Workout w) => ReadWorkout(Serialize(w))!;
}
