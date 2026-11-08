using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MeuEditorDeVideo.Domain.Time;

namespace MeuEditorDeVideo.ProjectEngine.Format;

/// <summary>Opções de serialização do project.json/manifest.json.</summary>
public static class MedJson
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var o = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters =
            {
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
                new MediaTimeConverter(),
                new FrameRateConverter(),
                new RationalConverter(),
            },
        };
        o.MakeReadOnly(populateMissingResolver: true);
        return o;
    }

    /// <summary>Tempo gravado como inteiro em flicks (1/705.600.000 s).</summary>
    private sealed class MediaTimeConverter : JsonConverter<MediaTime>
    {
        public override MediaTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            new(reader.GetInt64());

        public override void Write(Utf8JsonWriter writer, MediaTime value, JsonSerializerOptions options) =>
            writer.WriteNumberValue(value.Ticks);
    }

    /// <summary>FPS gravado como objeto { numerator, denominator } (formato do exemplo §9.4).</summary>
    private sealed class FrameRateConverter : JsonConverter<FrameRate>
    {
        public override FrameRate Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("FPS inválido.");
            int? num = null, den = null;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                var name = reader.GetString();
                reader.Read();
                switch (name)
                {
                    case "numerator": num = reader.GetInt32(); break;
                    case "denominator": den = reader.GetInt32(); break;
                    default: reader.Skip(); break;
                }
            }

            if (num is null || den is null || num <= 0 || den <= 0) throw new JsonException("FPS inválido.");
            return new FrameRate(num.Value, den.Value);
        }

        public override void Write(Utf8JsonWriter writer, FrameRate value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("numerator", value.Numerator);
            writer.WriteNumber("denominator", value.Denominator);
            writer.WriteEndObject();
        }
    }

    /// <summary>Velocidade gravada como texto "n/d".</summary>
    private sealed class RationalConverter : JsonConverter<Rational>
    {
        public override Rational Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            try
            {
                return Rational.Parse(reader.GetString() ?? throw new JsonException("Velocidade inválida."));
            }
            catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException or OverflowException)
            {
                throw new JsonException("Velocidade inválida.", ex);
            }
        }

        public override void Write(Utf8JsonWriter writer, Rational value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }

    public static string FormatVersion(int major, int minor) =>
        string.Create(CultureInfo.InvariantCulture, $"{major}.{minor}");
}
