namespace PowerPlanTray.Core.Models;

/// <summary>
/// Represents a single Windows power scheme (power plan).
/// </summary>
/// <param name="Guid">The GUID that uniquely identifies the power scheme.</param>
/// <param name="Name">The user-friendly display name of the power scheme.</param>
public record PowerScheme(Guid Guid, string Name);
