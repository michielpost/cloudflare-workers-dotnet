namespace Shared;

// D1 sample: the database and table from wrangler.toml plus the field limits the
// API enforces and the UI shows.
public static partial class SampleConfig
{
    /// <summary>D1 database from wrangler.toml, holding the items and telemetry tables.</summary>
    public const string D1DatabaseName = "dotnet";

    /// <summary>The one D1 table the CRUD sample edits.</summary>
    public const string D1ItemsTable = "items";

    /// <summary>Longest title the API accepts for an item.</summary>
    public const int ItemMaxTitleLength = 80;

    /// <summary>Longest notes value the API accepts for an item.</summary>
    public const int ItemMaxNotesLength = 240;

    /// <summary>How many items GET /api/items returns at most.</summary>
    public const int ItemListLimit = 50;
}
