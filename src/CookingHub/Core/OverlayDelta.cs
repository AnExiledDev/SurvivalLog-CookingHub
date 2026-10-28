using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace CookingHub.Core;

/// <summary>
/// The overlay as it goes to the page: nothing when it is the one the page already has, and otherwise a recipe book row
/// the page already has goes as its recipe id alone, so the page keeps that row and redraws only the rows that changed.
/// A patch counts as the page's only once it was sent; after the page loads afresh, Forget makes the next one whole.
/// The overlay is read forward once and copied as it stands, with only the kept rows cut out, so a patch costs about a
/// copy of the text rather than a parse and a rewrite of all of it.
/// </summary>
public sealed class OverlayDelta
{
    private Dictionary<int, string> _rows = new();
    private string _overlay = "";

    public void Forget()
    {
        _rows = new Dictionary<int, string>();
        _overlay = "";
    }

    /// <summary>What to send for this overlay, or null when the page already shows it.</summary>
    public OverlayPatch? Next(string overlay)
    {
        if (overlay == _overlay) return null;

        var rows = new Dictionary<int, string>();
        var bytes = Encoding.UTF8.GetBytes(overlay);
        var reader = new Utf8JsonReader(bytes);
        var output = new MemoryStream(bytes.Length);
        var copied = 0;

        if (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
        {
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var isBook = reader.ValueTextEquals("book");

                reader.Read();

                if (!isBook || reader.TokenType != JsonTokenType.StartArray)
                {
                    reader.Skip();

                    continue;
                }

                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    var start = (int)reader.TokenStartIndex;
                    var id = RowId(ref reader);
                    var end = (int)reader.BytesConsumed;

                    if (id is not { } key || rows.ContainsKey(key)) continue;

                    var text = Encoding.UTF8.GetString(bytes, start, end - start);

                    rows[key] = text;

                    if (!_rows.TryGetValue(key, out var sent) || sent != text) continue;

                    output.Write(bytes, copied, start - copied);
                    output.Write(Encoding.UTF8.GetBytes(key.ToString(CultureInfo.InvariantCulture)));
                    copied = end;
                }
            }
        }

        output.Write(bytes, copied, bytes.Length - copied);

        return new OverlayPatch(Encoding.UTF8.GetString(output.GetBuffer(), 0, (int)output.Length), overlay, rows);
    }

    /// <summary>The page took the patch, so its rows are what the next one is measured against.</summary>
    public void Sent(OverlayPatch patch)
    {
        _rows = patch.Rows;
        _overlay = patch.Overlay;
    }

    /// <summary>
    /// Reads past one book row, left on its last token. A row is an object with a whole-number id; anything else has none.
    /// </summary>
    private static int? RowId(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            reader.Skip();

            return null;
        }

        int? id = null;

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var isId = reader.ValueTextEquals("id");

            reader.Read();

            if (isId) id = reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var value) ? value : null;

            reader.Skip();
        }

        return id;
    }
}

/// <summary>A patch to send (Json), the whole overlay it stands for, and that overlay's book rows by recipe id.</summary>
public sealed record OverlayPatch(string Json, string Overlay, Dictionary<int, string> Rows);
