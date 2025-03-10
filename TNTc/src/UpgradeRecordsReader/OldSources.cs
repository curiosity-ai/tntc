using System.Text.Json;
using System.Text.Json.Serialization;
using J = System.Text.Json.Serialization.JsonPropertyNameAttribute;

namespace TNTc;

public partial class OldSources
{
    [J("language")] public string            Language { get; set; }
    [J("sources")]  public SourceElement[][] Sources  { get; set; }
}

public partial class SourceClass
{
    [J("path")] public string Path { get; set; }
}

public partial struct SourceElement
{
    public SourceClass SourceClass;
    public string      String;

    public static implicit operator SourceElement(SourceClass SourceClass) => new SourceElement { SourceClass = SourceClass };
    public static implicit operator SourceElement(string      String)      => new SourceElement { String      = String };
}

internal static class OldSourcesConverter
{
    public static readonly JsonSerializerOptions Settings = new(JsonSerializerDefaults.General)
    {
        Converters =
        {
            SourceElementConverter.Singleton,
            new DateOnlyConverter(),
            new TimeOnlyConverter(),
            IsoDateTimeOffsetConverter.Singleton
        },
    };
}

internal class SourceElementConverter : JsonConverter<SourceElement>
{
    public override bool CanConvert(Type t) => t == typeof(SourceElement);

    public override SourceElement Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                var stringValue = reader.GetString();
                return new SourceElement { String = stringValue };
            case JsonTokenType.StartObject:
                var objectValue = JsonSerializer.Deserialize<SourceClass>(ref reader, options);
                return new SourceElement { SourceClass = objectValue };
        }
        throw new Exception("Cannot unmarshal type SourceElement");
    }

    public override void Write(Utf8JsonWriter writer, SourceElement value, JsonSerializerOptions options)
    {
        if (value.String != null)
        {
            JsonSerializer.Serialize(writer, value.String, options);
            return;
        }

        if (value.SourceClass != null)
        {
            JsonSerializer.Serialize(writer, value.SourceClass, options);
            return;
        }
        throw new Exception("Cannot marshal type SourceElement");
    }

    public static readonly SourceElementConverter Singleton = new SourceElementConverter();
}