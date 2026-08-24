namespace PowerPlanTray.Core;

public static class WellKnownSchemes
{
    public static readonly (Guid Guid, string Name)[] DefaultSchemes =
    [
        (new Guid("381b4222-f694-41f0-9685-ff5bb260df2e"), "Balanced"),
        (new Guid("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"), "High performance"),
        (new Guid("a1841308-3541-4fab-bc81-f71556f20b4a"), "Power saver"),
        (new Guid("e9a42b02-d5df-448d-aa00-03f14749eb61"), "Ultimate Performance"),
    ];
}
