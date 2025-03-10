using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Text.Unicode;
using CodeScanner;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TNTc;
using OpenAI.Chat;
using J = System.Text.Json.Serialization.JsonPropertyNameAttribute;
using N = System.Text.Json.Serialization.JsonIgnoreCondition;

namespace TNT.CLI;

public partial class Program
{

    private static void JsonText()
    {
        var testString = "👋 🧐  💡 {0:n0} Monate \\ hello\nworld\"";

        var optionsWrite = new JsonSerializerOptions()
        {
            Encoder = new PassThroughJavaScriptEncoder()
        };

        var result = JsonSerializer.Serialize(new
        {
            STRING = testString,
        }, optionsWrite);

        var expected = $"{{\"STRING\":\"👋 🧐  💡 {{0:n0}} Monate \\\\ hello\\nworld\\\"\"}}";

        Console.WriteLine(expected);
        Console.WriteLine(result);
        Console.WriteLine(expected == result);
    }
}