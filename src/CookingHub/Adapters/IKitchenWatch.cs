namespace CookingHub.Adapters;

/// <summary>What the session asks of the game every frame, kept cheap: the cooking screen, and the cookbook read in slices.</summary>
internal interface IKitchenWatch
{
    /// <summary>True while the game's cooking screen is up.</summary>
    bool IsCookingScreenOpen();

    /// <summary>A print of what the open cooking screen shows; a new one means the kitchen is worth reading again.</summary>
    ulong ScreenStamp();

    /// <summary>Starts reading the cookbook, if a game is loaded and it is not read yet.</summary>
    void WarmCatalog();

    /// <summary>One frame's slice of the cookbook read, if one is under way.</summary>
    void AdvanceCatalog();
}
