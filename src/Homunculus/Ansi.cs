namespace Homunculus;

/// <summary>The escape codes the console output uses, in the banner's colours.</summary>
internal static class Ansi
{
    public const string Reset = "\e[0m";
    public const string Bold = "\e[1m";
    public const string Italic = "\e[3m";
    public const string Underline = "\e[4m";
    public const string Green = "\e[38;2;95;207;159m";
    public const string Mint = "\e[38;2;155;232;193m";
    public const string Grey = "\e[38;2;138;131;153m";
    public const string Red = "\e[38;2;239;108;108m";
    public const string Gold = "\e[38;2;255;212;94m";
    public const string CodeBackground = "\e[48;2;45;36;56m";
}
