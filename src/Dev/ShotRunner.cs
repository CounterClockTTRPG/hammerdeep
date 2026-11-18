using System;
using System.Linq;
using STask = System.Threading.Tasks.Task;
using Godot;

namespace Hammerdeep;

/// <summary>
/// <c>./play.sh --shots [NAME]</c>: plays a fixed-seed fortress with the
/// overseer and photographs the screens into <c>shots/*.png</c> -- the title,
/// each level of the hold, the panels and the overlays. With a name, only the
/// shots whose names contain it.
/// </summary>
public partial class ShotRunner : Node
{
    public Root Root;
    private string _only;
    private string _dir;
    private int _count;

    public override void _Ready()
    {
        string[] args = OS.GetCmdlineUserArgs();
        int at = Array.IndexOf(args, "--shots");
        if (at >= 0 && at + 1 < args.Length && !args[at + 1].StartsWith("--")) _only = args[at + 1];
        _dir = ProjectSettings.GlobalizePath("res://shots");
        DirAccess.MakeDirRecursiveAbsolute(_dir);
        _ = Walk();
    }

    private async STask Frames(int n = 3)
    {
        for (int i = 0; i < n; i++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
    }

    private bool Want(string name) => _only == null || name.Contains(_only, StringComparison.OrdinalIgnoreCase);

    private async STask Shot(string name)
    {
        if (!Want(name)) return;
        await Frames(4);
        Image img = GetViewport().GetTexture().GetImage();
        img.SavePng($"{_dir}/{name}.png");
        _count++;
        GD.Print($"shot {name}");
    }

    private async STask Walk()
    {
        try
        {
            Root.ShowTitle();
            await Frames(5);
            await Shot("01-title");
            Root.NewFortress("Bolkdeep", 1, 4242);
            FortressScreen s = Root.Screen;
            Fortress f = Root.Fortress;
            s.SetSpeed(0);
            await Frames(3);
            await Shot("02-embark");
            var o = new Overseer(f);
            // A season and a half of the overseer's work.
            for (int k = 0; k < 75; k++) { o.Think(); f.Advance(240); }
            MapView m = Root.Map;
            m.CenterOn(f.Embark);
            m.Cam.Zoom = new Vector2(2, 2);
            await Shot("03-surface");
            m.Cam.Zoom = new Vector2(1, 1);
            await Shot("04-surface-wide");
            m.Cam.Zoom = new Vector2(2.2f, 2.2f);
            m.CenterOn(o.HallCenter);
            await Shot("05-hall");
            m.CenterOn(new P3(o.Shaft.X - 7, o.Shaft.Y, o.BedZ));
            await Shot("06-bedrooms");
            m.CenterOn(new P3(o.Shaft.X - 5, o.Shaft.Y, o.FarmZ));
            await Shot("07-farm");
            m.CenterOn(new P3(o.Shaft.X, o.Shaft.Y, o.MineZ));
            await Shot("08-mines");
            // Looking down the shaft from the sky.
            m.CenterOn(new P3(f.Embark.X, f.Embark.Y, f.World.SurfaceZ + 2));
            await Shot("09-from-above");
            // A dwarf and their panel.
            Dwarf d = f.Living.OrderByDescending(x => x.Thoughts.Count).First();
            m.Cam.Zoom = new Vector2(3, 3);
            s.Jump(f.World.Pos(d.Cell));
            s.SelectThing(d);
            await Shot("10-dwarf");
            Building shop = f.Buildings.FirstOrDefault(b => b.Built && b.Orders.Count > 0) ?? f.Buildings.First(b => b.D.Kind == BuildKind.Workshop);
            m.Cam.Zoom = new Vector2(2.5f, 2.5f);
            s.Jump(shop.Center);
            s.SelectThing(shop);
            await Shot("11-workshop");
            Zone pile = f.Zones.First(z => z.Kind == ZoneKind.Stockpile);
            s.Jump(new P3((pile.X0 + pile.X1) / 2, (pile.Y0 + pile.Y1) / 2, pile.Z));
            s.SelectThing(pile);
            await Shot("12-stockpile");
            s.SelectThing(null);
            s.Pick(Tool.Build);
            await Shot("13-build-menu");
            s.Pick(Tool.Select);
            s.Open(new DwarfList(s));
            await Shot("14-dwarves");
            s.Open(new LaborGrid(s));
            await Shot("15-labors");
            s.Open(new Stocks(s));
            await Shot("16-stocks");
            s.Open(new HelpPanel(s));
            await Shot("17-help");
            s.CloseOverlay();
            s.OpenMenu();
            await Shot("18-menu");
            s.CloseMenu();
            // A year on: winter, migrants, a busy hall.
            for (int k = 0; k < 75; k++) { o.Think(); f.Advance(240); }
            m.Cam.Zoom = new Vector2(1.6f, 1.6f);
            m.CenterOn(f.Embark);
            await Shot("19-winter-surface");
            m.CenterOn(o.HallCenter);
            m.ShowNames = true;
            await Shot("20-hall-later");
            m.ShowNames = false;
            s.ShowAlert("A sample alert: something needs your attention.", f.World.Idx(f.Embark));
            await Shot("21-alert");
            // A shrine in the hall.
            Building shrine = f.Buildings.FirstOrDefault(b => b.Def == "shrine" && b.Built) ?? f.Buildings.FirstOrDefault(b => b.Def == "shrine");
            if (shrine != null)
            {
                m.Cam.Zoom = new Vector2(3, 3);
                s.Jump(shrine.Center);
                s.SelectThing(shrine);
                await Shot("30-shrine");
                s.SelectThing(null);
            }
            // A strange mood, and the artifact it makes.
            Dwarf moody = f.Living.Where(x => x.Squad < 0 && !x.HadMood).OrderByDescending(x => x.Xp[(int)Labor.Masonry] + x.Xp[(int)Labor.Carpentry]).FirstOrDefault();
            if (moody != null)
            {
                f.StartMood(moody, "fey");
                for (int k = 0; k < 30 && moody.MoodShop < 0; k++) f.Advance(50);
                for (int k = 0; k < 6; k++) f.Advance(50);
                Building ms = moody.MoodShop >= 0 ? f.BuildingById(moody.MoodShop) : null;
                m.Cam.Zoom = new Vector2(2.6f, 2.6f);
                s.Jump(f.World.Pos(moody.Cell));
                s.SelectThing(moody);
                await Shot("28-mood");
                foreach (string need in moody.MoodNeeds.ToList()) f.Spawn(need switch { "gem" => ItemKind.RoughGem, "shiny" => ItemKind.CutGem, "bone" => ItemKind.Bone, "leather" => ItemKind.Leather, "log" => ItemKind.Log, "bar" => ItemKind.Bar, _ => ItemKind.Boulder }, Tables.Mi(need switch { "gem" or "shiny" => "emerald", "bone" => "bone", "leather" => "leather", "log" => "oak", "bar" => "iron", _ => "granite" }), moody.Cell);
                for (int k = 0; k < 120 && moody.Strange != null; k++) f.Advance(100);
                s.SelectThing(null);
                s.Open(new Chronicle(s));
                await Shot("29-chronicle");
                s.CloseOverlay();
            }
            // A raid at the gates.
            f.LaunchRaid(8);
            for (int k = 0; k < 40 && !f.Creatures.Any(c => c.Raider && f.World.Pos(c.Cell).Dist(f.Embark) < 14); k++) { o.Think(); f.Advance(50); }
            for (int k = 0; k < 4; k++) { o.Think(); f.Advance(40); }
            Creature r = f.Creatures.Where(c => c.Raider).OrderBy(c => f.World.Pos(c.Cell).Dist(f.Embark)).FirstOrDefault();
            m.Cam.Zoom = new Vector2(2.6f, 2.6f);
            s.SetSpeed(0);
            if (r != null) { s.Jump(f.World.Pos(r.Cell)); s.SelectThing(r); }
            await Shot("22-raid");
            s.SelectThing(null);
            s.Open(new MilitaryPanel(s));
            await Shot("23-military");
            s.CloseOverlay();
            Zone pasture = f.Zones.FirstOrDefault(z => z.Kind == ZoneKind.Pasture);
            Creature goat = f.Creatures.FirstOrDefault(c => c.Tame && c.Def == "goat") ?? f.Creatures.FirstOrDefault(c => c.Tame);
            m.Cam.Zoom = new Vector2(3, 3);
            if (goat != null) { s.Jump(f.World.Pos(goat.Cell)); s.SelectThing(goat); }
            else if (pasture != null) s.Jump(new P3((pasture.X0 + pasture.X1) / 2, (pasture.Y0 + pasture.Y1) / 2, pasture.Z));
            await Shot("24-pasture");
            s.SelectThing(null);
            s.Open(new AnimalsPanel(s));
            await Shot("25-animals");
            s.CloseOverlay();
            // A caravan at the depot, and the barter.
            Caravan cv = f.SendCaravan(Fortress.FactionDefs.First(x => x.Key == "lowlands"));
            for (int k = 0; k < 80 && cv.State == CaravanState.Coming; k++) f.Advance(50);
            Building dep = f.Buildings.FirstOrDefault(b => b.Def == "depot");
            if (dep != null) { m.Cam.Zoom = new Vector2(2.6f, 2.6f); s.Jump(dep.Center); s.SelectThing(dep); }
            s.SetSpeed(0);
            await Shot("26-depot");
            if (cv.State == CaravanState.Trading)
            {
                s.Open(new TradePanel(s, cv));
                await Shot("27-trade");
                s.CloseOverlay();
            }
            GD.Print($"{_count} shots in {_dir}");
        }
        catch (Exception e) { GD.PrintErr("shots failed: " + e); }
        GetTree().Quit();
    }
}
