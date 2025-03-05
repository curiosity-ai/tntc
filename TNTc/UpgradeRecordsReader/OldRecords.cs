using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;
using J = System.Text.Json.Serialization.JsonPropertyNameAttribute;
using N = System.Text.Json.Serialization.JsonIgnoreCondition;


namespace TNTc;

public partial class OldRecords
{
    [J("language")] public string     Language { get; set; }
    [J("records")]  public Record[][] Records  { get; set; }
}
public partial struct Record
{
    public string   String;
    public string[] StringArray;

    public static implicit operator Record(string   String)      => new Record { String      = String };
    public static implicit operator Record(string[] StringArray) => new Record { StringArray = StringArray };
}
internal static class OldRecordsConverter
{
    public static readonly JsonSerializerOptions Settings = new(JsonSerializerDefaults.General)
    {
        Converters =
        {
            RecordConverter.Singleton,
            new DateOnlyConverter(),
            new TimeOnlyConverter(),
            IsoDateTimeOffsetConverter.Singleton
        },
    };
}
internal class RecordConverter : JsonConverter<Record>
{
    public override bool CanConvert(Type t) => t == typeof(Record);

    public override Record Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                var stringValue = reader.GetString();
                return new Record { String = stringValue };
            case JsonTokenType.StartArray:
                var arrayValue = JsonSerializer.Deserialize<string[]>(ref reader, options);
                return new Record { StringArray = arrayValue };
        }
        throw new Exception("Cannot unmarshal type Record");
    }

    public override void Write(Utf8JsonWriter writer, Record value, JsonSerializerOptions options)
    {
        if (value.String != null)
        {
            JsonSerializer.Serialize(writer, value.String, options);
            return;
        }

        if (value.StringArray != null)
        {
            JsonSerializer.Serialize(writer, value.StringArray, options);
            return;
        }
        throw new Exception("Cannot marshal type Record");
    }

    public static readonly RecordConverter Singleton = new RecordConverter();

}