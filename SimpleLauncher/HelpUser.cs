using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Documents;

namespace SimpleLauncher;

public static class HelpUser
{
    private static readonly HelpUserConfig Config = new();

    static HelpUser()
    {
        try
        {
            Config.Load(); // Load helpuser.xml
        }
        catch (Exception ex)
        {
            string formattedException = $"Failed to load helpuser.xml.\n\n" +
                                        $"Exception type: {ex.GetType().Name}\n" +
                                        $"Exception details: {ex.Message}";
            Task logTask = LogErrors.LogErrorAsync(ex, formattedException);
            logTask.Wait(TimeSpan.FromSeconds(2));
        }
    }
    
    public static void UpdateHelpUserTextBlock(TextBlock helpUserTextBlock, TextBox systemNameTextBox)
    {
        // Retrieve the system name from the TextBox
        string systemName = systemNameTextBox?.Text.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(systemName))
        {
            // Clear the TextBlock and display a default message if no system name is provided
            helpUserTextBlock.Inlines.Clear();
            helpUserTextBlock.Inlines.Add(new Run("No system name provided."));
            return;
        }

        // Define the emulator configurations based on system names
        var responses = new Dictionary<string, Func<string>>(StringComparer.OrdinalIgnoreCase)
        {
            { "Amstrad CPC", AmstradCpcDetails },
            { "Amstrad CPC GX4000", AmstradCpcgx4000Details },
            { "Arcade", ArcadeDetails },
            { "Atari 2600", Atari2600Details },
            { "Atari 5200", Atari5200Details },
            { "Atari 7800", Atari7800Details },
            { "Atari 8-Bit", Atari8BitDetails },
            { "Atari Jaguar", AtariJaguarDetails },
            { "Atari Jaguar CD", AtariJaguarCdDetails },
            { "Atari Lynx", AtariLynxDetails },
            { "Atari ST", AtariStDetails },
            { "Bandai WonderSwan", BandaiWonderSwanDetails },
            { "Bandai WonderSwan Color", BandaiWonderSwanColorDetails },
            { "Casio PV-1000", CasioPv1000Details },
            { "Colecovision", ColecovisionDetails },
            { "Commodore 64", Commodore64Details },
            { "Commodore Amiga CD32", CommodoreAmigaCd32Details },
            { "LaserDisk", LaserDiskDetails },
            { "Magnavox Odyssey 2", MagnavoxOdyssey2Details },
            { "Mattel Aquarius", MattelAquariusDetails },
            { "Mattel Intellivision", MattelIntellivisionDetails },
            { "Microsoft DOS", MicrosoftDosDetails },
            { "Microsoft MSX", MicrosoftMsxDetails },
            { "Microsoft MSX2", MicrosoftMsx2Details },
            { "Microsoft Windows", MicrosoftWindowsDetails },
            { "Microsoft Xbox", MicrosoftXboxDetails },
            { "Microsoft Xbox 360", MicrosoftXbox360Details },
            { "Microsoft Xbox 360 XBLA Using Compressed Folders", MicrosoftXbox360XblaUsingCompressedFoldersDetails },
            { "Microsoft Xbox 360 XBLA Using BAT files", MicrosoftXbox360XblaUsingBaTfilesDetails },
            { "NEC PC Engine", NecpcEngineDetails },
            { "NEC TurboGrafx-16", NecTurboGrafx16Details },
            { "NEC PC Engine CD", NecpcEngineCdDetails },
            { "NEC PC-FX", NecpcfxDetails },
            { "NEC SuperGrafx", NecSuperGrafxDetails },
            { "Nintendo 3DS", Nintendo3DsDetails },
            { "Nintendo 64", Nintendo64Details },
            { "Nintendo 64DD", Nintendo64DdDetails },
            { "Nintendo DS", NintendoDsDetails },
            { "Nintendo Family Computer Disk System", NintendoFamilyComputerDiskSystemDetails },
            { "Nintendo Game Boy", NintendoGameBoyDetails },
            { "Nintendo Game Boy Advance", NintendoGameBoyAdvanceDetails },
            { "Nintendo Game Boy Color", NintendoGameBoyColorDetails },
            { "Nintendo GameCube", NintendoGameCubeDetails },
            { "Nintendo NES", NintendoNesDetails },
            { "Nintendo Satellaview", NintendoSatellaviewDetails },
            { "Nintendo SNES", NintendoSnesDetails },
            { "Nintendo SNES MSU1", NintendoSnesmsu1Details },
            { "Nintendo Switch", NintendoSwitchDetails },
            { "Nintendo Wii", NintendoWiiDetails },
            { "Nintendo WiiU", NintendoWiiUDetails },
            { "Nintendo WiiWare", NintendoWiiWareDetails },
            { "Panasonic 3DO", Panasonic3DoDetails },
            { "Philips CD-i", PhilipsCDiDetails },
            { "ScummVM", ScummVmDetails },
            { "Sega Dreamcast", SegaDreamcastDetails },
            { "Sega Game Gear", SegaGameGearDetails },
            { "Sega Genesis", SegaGenesisDetails },
            { "Sega Genesis 32X", SegaGenesis32XDetails },
            { "Sega Genesis CD", SegaGenesisCdDetails },
            { "Sega Master System", SegaMasterSystemDetails },
            { "Sega Model 3", SegaModel3Details },
            { "Sega Saturn", SegaSaturnDetails },
            { "Sega SC-3000", SegaSc3000Details },
            { "Sega SG-1000", SegaSg1000Details },
            { "Sharp x68000", Sharpx68000Details },
            { "Sinclair ZX Spectrum", SinclairZxSpectrumDetails },
            { "SNK Neo Geo CD", SnkNeoGeoCdDetails },
            { "SNK Neo Geo Pocket", SnkNeoGeoPocketDetails },
            { "SNK Neo Geo Pocket Color", SnkNeoGeoPocketColorDetails },
            { "Sony PlayStation 1", SonyPlayStation1Details },
            { "Sony PlayStation 2", SonyPlayStation2Details },
            { "Sony PlayStation 3", SonyPlayStation3Details },
            { "Sony PlayStation Vita", SonyPlayStationVitaDetails },
            { "Sony PSP", SonyPspDetails }
        };
        
        helpUserTextBlock.Inlines.Clear();

        // Check if a response exists for the given system name
        if (responses.TryGetValue(systemName, out var responseGenerator))
        {
            var text = responseGenerator();
            SetTextWithLinks(helpUserTextBlock, text);
        }
        else
        {
            // Display a message if the system name is not recognized
            helpUserTextBlock.Inlines.Add(new Run($"No information available for system: {systemName}"));
        }
    }
    
    private static string AmstradCpcDetails() => GetSystemDetails("Amstrad CPC");
    private static string AmstradCpcgx4000Details() => GetSystemDetails("Amstrad CPC GX4000");
    private static string ArcadeDetails() => GetSystemDetails("Arcade");
    private static string Atari2600Details() => GetSystemDetails("Atari 2600");
    private static string Atari5200Details() => GetSystemDetails("Atari 5200");
    private static string Atari7800Details() => GetSystemDetails("Atari 7800");
    private static string Atari8BitDetails() => GetSystemDetails("Atari 8-Bit");
    private static string AtariJaguarDetails() => GetSystemDetails("Atari Jaguar");
    private static string AtariJaguarCdDetails() => GetSystemDetails("Atari Jaguar CD");
    private static string AtariLynxDetails() => GetSystemDetails("Atari Lynx");
    private static string AtariStDetails() => GetSystemDetails("Atari ST");
    private static string BandaiWonderSwanDetails() => GetSystemDetails("Bandai WonderSwan");
    private static string BandaiWonderSwanColorDetails() => GetSystemDetails("Bandai WonderSwan Color");
    private static string CasioPv1000Details() => GetSystemDetails("Casio PV-1000");
    private static string ColecovisionDetails() => GetSystemDetails("Colecovision");
    private static string Commodore64Details() => GetSystemDetails("Commodore 64");
    private static string CommodoreAmigaCd32Details() => GetSystemDetails("Commodore Amiga CD32");
    private static string LaserDiskDetails() => GetSystemDetails("LaserDisk");
    private static string MagnavoxOdyssey2Details() => GetSystemDetails("Magnavox Odyssey 2");
    private static string MattelAquariusDetails() => GetSystemDetails("Mattel Aquarius");
    private static string MattelIntellivisionDetails() => GetSystemDetails("Mattel Intellivision");
    private static string MicrosoftDosDetails() => GetSystemDetails("Microsoft DOS");
    private static string MicrosoftMsxDetails() => GetSystemDetails("Microsoft MSX");
    private static string MicrosoftMsx2Details() => GetSystemDetails("Microsoft MSX2");
    private static string MicrosoftWindowsDetails() => GetSystemDetails("Microsoft Windows");
    private static string MicrosoftXboxDetails() => GetSystemDetails("Microsoft Xbox");
    private static string MicrosoftXbox360Details() => GetSystemDetails("Microsoft Xbox 360");
    private static string MicrosoftXbox360XblaUsingCompressedFoldersDetails() => GetSystemDetails("Microsoft Xbox 360 XBLA Using Compressed Folders");
    private static string MicrosoftXbox360XblaUsingBaTfilesDetails() => GetSystemDetails("Microsoft Xbox 360 XBLA Using BAT files");
    private static string NecpcEngineDetails() => GetSystemDetails("NEC PC Engine");
    private static string NecTurboGrafx16Details() => GetSystemDetails("NEC TurboGrafx-16");
    private static string NecpcEngineCdDetails() => GetSystemDetails("NEC PC Engine CD");
    private static string NecpcfxDetails() => GetSystemDetails("NEC PC-FX");
    private static string NecSuperGrafxDetails() => GetSystemDetails("NEC SuperGrafx");
    private static string Nintendo3DsDetails() => GetSystemDetails("Nintendo 3DS");
    private static string Nintendo64Details() => GetSystemDetails("Nintendo 64");
    private static string Nintendo64DdDetails() => GetSystemDetails("Nintendo 64DD");
    private static string NintendoDsDetails() => GetSystemDetails("Nintendo DS");
    private static string NintendoFamilyComputerDiskSystemDetails() => GetSystemDetails("Nintendo Family Computer Disk System");
    private static string NintendoGameBoyDetails() => GetSystemDetails("Nintendo Game Boy");
    private static string NintendoGameBoyAdvanceDetails() => GetSystemDetails("Nintendo Game Boy Advance");
    private static string NintendoGameBoyColorDetails() => GetSystemDetails("Nintendo Game Boy Color");
    private static string NintendoGameCubeDetails() => GetSystemDetails("Nintendo GameCube");
    private static string NintendoNesDetails() => GetSystemDetails("Nintendo NES");
    private static string NintendoSatellaviewDetails() => GetSystemDetails("Nintendo Satellaview");
    private static string NintendoSnesDetails() => GetSystemDetails("Nintendo SNES");
    private static string NintendoSnesmsu1Details() => GetSystemDetails("Nintendo SNES MSU1");
    private static string NintendoSwitchDetails() => GetSystemDetails("Nintendo Switch");
    private static string NintendoWiiDetails() => GetSystemDetails("Nintendo Wii");
    private static string NintendoWiiUDetails() => GetSystemDetails("Nintendo WiiU");
    private static string NintendoWiiWareDetails() => GetSystemDetails("Nintendo WiiWare");
    private static string Panasonic3DoDetails() => GetSystemDetails("Panasonic 3DO");
    private static string PhilipsCDiDetails() => GetSystemDetails("Philips CD-i");
    private static string ScummVmDetails() => GetSystemDetails("ScummVM");
    private static string SegaDreamcastDetails() => GetSystemDetails("Sega Dreamcast");
    private static string SegaGameGearDetails() => GetSystemDetails("Sega Game Gear");
    private static string SegaGenesisDetails() => GetSystemDetails("Sega Genesis");
    private static string SegaGenesis32XDetails() => GetSystemDetails("Sega Genesis 32X");
    private static string SegaGenesisCdDetails() => GetSystemDetails("Sega Genesis CD");
    private static string SegaMasterSystemDetails() => GetSystemDetails("Sega Master System");
    private static string SegaModel3Details() => GetSystemDetails("Sega Model 3");
    private static string SegaSaturnDetails() => GetSystemDetails("Sega Saturn");
    private static string SegaSc3000Details() => GetSystemDetails("Sega SC-3000");
    private static string SegaSg1000Details() => GetSystemDetails("Sega SG-1000");
    private static string Sharpx68000Details() => GetSystemDetails("Sharp x68000");
    private static string SinclairZxSpectrumDetails() => GetSystemDetails("Sinclair ZX Spectrum");
    private static string SnkNeoGeoCdDetails() => GetSystemDetails("SNK Neo Geo CD");
    private static string SnkNeoGeoPocketDetails() => GetSystemDetails("SNK Neo Geo Pocket");
    private static string SnkNeoGeoPocketColorDetails() => GetSystemDetails("SNK Neo Geo Pocket Color");
    private static string SonyPlayStation1Details() => GetSystemDetails("Sony PlayStation 1");
    private static string SonyPlayStation2Details() => GetSystemDetails("Sony PlayStation 2");
    private static string SonyPlayStation3Details() => GetSystemDetails("Sony PlayStation 3");
    private static string SonyPlayStationVitaDetails() => GetSystemDetails("Sony PlayStation Vita");
    private static string SonyPspDetails() => GetSystemDetails("Sony PSP");
    
    private static void SetTextWithLinks(TextBlock textBlock, string text)
    {
        var regex = new Regex(@"\b(?:https?://|www\.)\S+\b", RegexOptions.Compiled);
        var parts = regex.Split(text);
        var matches = regex.Matches(text);

        int index = 0;
        foreach (var part in parts)
        {
            textBlock.Inlines.Add(new Run(part));

            if (index < matches.Count)
            {
                var hyperlink = new Hyperlink(new Run(matches[index].Value))
                {
                    NavigateUri = new Uri(matches[index].Value.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                        ? matches[index].Value
                        : "http://" + matches[index].Value)
                };
                hyperlink.RequestNavigate += (_, e) =>
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = e.Uri.AbsoluteUri,
                        UseShellExecute = true
                    });
                };
                textBlock.Inlines.Add(hyperlink);
                index++;
            }
        }
    }
    
    private static string GetSystemDetails(string systemName)
    {
        // Fetch the system details from the configuration
        var system = Config.Systems.FirstOrDefault(s => s.SystemName.Equals(systemName, StringComparison.OrdinalIgnoreCase));
        return system?.SystemHelperText ?? $"No details available for {systemName}.";
    }

}