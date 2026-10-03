using System.Text.Json.Serialization;

namespace AirsoftPlanner.Core.Gps;

/// <summary>
/// Format JSON du protocole téléphone ↔ PC de l'OP (noms en camelCase, réglages en texte), généré à la compilation :
/// utilisable par l'application Android dont le code est élagué.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(EnrollRequest))]
[JsonSerializable(typeof(EnrollResponse))]
[JsonSerializable(typeof(TrackRequest))]
[JsonSerializable(typeof(TrackResponse))]
public partial class ProtocolJson : JsonSerializerContext;
