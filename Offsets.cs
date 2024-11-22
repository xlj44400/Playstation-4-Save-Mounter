using System;

namespace PS4Saves;

public static class Offsets
{
    public static readonly string[] Firmwares = ["4.03", "5.xx"];
    public static string SelectedFirmware { get; set; } = string.Empty; // updated by fwVersionComboBox

    public static ulong sceUserServiceGetInitialUser => SelectedFirmware switch
    {
        "4.03" => 0x3290,
        "5.xx" => 0x33B0,
        _ => throw new Exception("Unsupported firmware (did you select an item from the dropdown?)")
    };

    public static ulong sceUserServiceGetLoginUserIdList => SelectedFirmware switch
    {
        "4.03" => 0x2A50,
        "5.xx" => 0x2B00,
        _ => throw new Exception("Unsupported firmware (did you select an item from the dropdown?)")
    };

    public static ulong sceUserServiceGetUserName => SelectedFirmware switch
    {
        "4.03" => 0x46E0,
        "5.xx" => 0x4830,
        _ => throw new Exception("Unsupported firmware (did you select an item from the dropdown?)")
    };

    public static ulong sceSaveDataMount2 => SelectedFirmware switch
    {
        "4.03" => 0x31470,
        "5.xx" => 0x321B0,
        _ => throw new Exception("Unsupported firmware (did you select an item from the dropdown?)")
    };

    public static ulong sceSaveDataUmount => SelectedFirmware switch
    {
        "4.03" => 0x31940,
        "5.xx" => 0x32680,
        _ => throw new Exception("Unsupported firmware (did you select an item from the dropdown?)")
    };

    public static ulong sceSaveDataDirNameSearch => SelectedFirmware switch
    {
        "4.03" => 0x32720,
        "5.xx" => 0x33460,
        _ => throw new Exception("Unsupported firmware (did you select an item from the dropdown?)")
    };

    public static ulong sceSaveDataTransferringMount => SelectedFirmware switch
    {
        "4.03" => 0x317F0,
        "5.xx" => 0x32530,
        _ => throw new Exception("Unsupported firmware (did you select an item from the dropdown?)")
    };

    public static ulong sceSaveDataInitialize3 => SelectedFirmware switch
    {
        "4.03" => 0x30FE0,
        "5.xx" => 0x31D20,
        _ => throw new Exception("Unsupported firmware (did you select an item from the dropdown?)")
    };
}
