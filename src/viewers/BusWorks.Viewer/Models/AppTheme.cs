using MudBlazor;

namespace BusWorks.Viewer.Models;

/// <summary>
/// Centralised MudBlazor theme definition.
/// PaletteLight keeps the default purple primary that looks great in light mode.
/// PaletteDark uses a lighter lavender so primary-coloured text/icons are
/// readable against dark backgrounds.
/// </summary>
public static class AppTheme
{
    public static readonly MudTheme Theme = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#594AE2",          // default MudBlazor purple – looks great on white
            Secondary = "#FF4081",
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#C5BDFF",          // soft lavender – readable on dark backgrounds
            Secondary = "#FF80AB",
        }
    };
}

