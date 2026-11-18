using System;
using System.Text.Json;
using Godot;

namespace Hammerdeep;

/// <summary>
/// Settings (<c>user://hammerdeep.json</c>) and the three ironman fortress
/// slots (<c>user://fort1.sav</c> .. <c>fort3.sav</c>). The self-check and the
/// walkthrough use a scratch profile and never touch either; the agent has its
/// own slot.
/// </summary>
public sealed class Profile
{
    public static bool Scratch, Agent;
    public static Profile Current = new();
    public static int Slot = 1;

    public bool Fullscreen;
    public float Volume = 0.6f;
    public bool Sound = true;
    public bool PauseOnAlerts = true;
    public bool MusicOn = true;
    public float MusicVolume = 0.5f;
    public int LastSlot = 1;

    private static string SettingsPath => Scratch ? "user://scratch_settings.json" : "user://hammerdeep.json";
    public static string SlotPath(int slot) => Agent ? "user://agent_fort.sav" : Scratch ? $"user://scratch_fort{slot}.sav" : $"user://fort{slot}.sav";

    public static void Load()
    {
        try
        {
            if (FileAccess.FileExists(SettingsPath))
                Current = JsonSerializer.Deserialize<Profile>(FileAccess.GetFileAsString(SettingsPath), new JsonSerializerOptions { IncludeFields = true }) ?? new Profile();
        }
        catch (Exception e) { GD.PrintErr("settings unreadable, using defaults: " + e.Message); Current = new Profile(); }
        Slot = Math.Clamp(Current.LastSlot, 1, 3);
    }

    public void Save()
    {
        using var f = FileAccess.Open(SettingsPath, FileAccess.ModeFlags.Write);
        f?.StoreString(JsonSerializer.Serialize(this, new JsonSerializerOptions { IncludeFields = true }));
    }

    public void Apply()
    {
        if (DisplayServer.GetName() == "headless") return;
        DisplayServer.WindowSetMode(Fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed);
        AudioServer.SetBusVolumeDb(0, Sound ? Mathf.LinearToDb(Mathf.Max(0.001f, Volume)) : -80);
    }

    public static void SaveFortress(Fortress f, int slot = 0)
    {
        if (slot <= 0) slot = Slot;
        byte[] data = Hammerdeep.Save.Write(f);
        string path = SlotPath(slot), tmp = path + ".tmp";
        using (var file = FileAccess.Open(tmp, FileAccess.ModeFlags.Write))
        {
            if (file == null) { GD.PrintErr("cannot write " + tmp); return; }
            file.StoreBuffer(data);
        }
        DirAccess.RenameAbsolute(ProjectSettings.GlobalizePath(tmp), ProjectSettings.GlobalizePath(path));
    }

    public static Fortress LoadFortress(int slot)
    {
        string path = SlotPath(slot);
        if (!FileAccess.FileExists(path)) return null;
        try { return Hammerdeep.Save.Read(FileAccess.GetFileAsBytes(path)); }
        catch (Exception e) { GD.PrintErr($"slot {slot} unreadable: {e.Message}"); return null; }
    }

    /// <summary>A one-line summary of a slot for the title screen, or null when empty.</summary>
    public static string Peek(int slot, out bool fallen)
    {
        fallen = false;
        Fortress f = LoadFortress(slot);
        if (f == null) return null;
        fallen = f.Fallen;
        return $"{f.Name} -- {f.Date}, {f.Alive} dwarves{(f.Fallen ? " (fallen)" : "")}";
    }

    public static void DeleteSlot(int slot)
    {
        string path = SlotPath(slot);
        if (FileAccess.FileExists(path)) DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path));
    }
}
