using System.Collections.Generic;
using System.Text.Json;

namespace CookingHub.Core;

/// <summary>
/// The overlay as it goes to the page: nothing when it is the one the page already has, and otherwise a recipe book row
/// the page already has goes as its recipe id alone, so the page keeps that row and redraws only the rows that changed.
/// A patch counts as the page's only once it was sent; after the page loads afresh, Forget makes the next one whole.
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

        using var document = JsonDocument.Parse(overlay);

        var json = Json.Object(writer =>
        {
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!property.NameEquals("book") || property.Value.ValueKind != JsonValueKind.Array)
                {
                    property.WriteTo(writer);

                    continue;
                }

                writer.WriteStartArray("book");

                foreach (var row in property.Value.EnumerateArray())
                {
                    if (row.ValueKind == JsonValueKind.Object && row.TryGetProperty("id", out var idValue) && idValue.TryGetInt32(out var id) && !rows.ContainsKey(id))
                    {
                        var text = row.GetRawText();

                        rows[id] = text;

                        if (_rows.TryGetValue(id, out var sent) && sent == text)
                        {
                            writer.WriteNumberValue(id);

                            continue;
                        }
                    }

                    row.WriteTo(writer);
                }

                writer.WriteEndArray();
            }
        });

        return new OverlayPatch(json, overlay, rows);
    }

    /// <summary>The page took the patch, so its rows are what the next one is measured against.</summary>
    public void Sent(OverlayPatch patch)
    {
        _rows = patch.Rows;
        _overlay = patch.Overlay;
    }
}

/// <summary>A patch to send (Json), the whole overlay it stands for, and that overlay's book rows by recipe id.</summary>
public sealed record OverlayPatch(string Json, string Overlay, Dictionary<int, string> Rows);
