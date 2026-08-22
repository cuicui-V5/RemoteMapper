using System;
using System.Collections.Generic;

public enum RemoteKeyMode { Editable, Native, Voice }

public sealed class RemoteKeyDef {
    public string Id;
    public string Name;
    public string Title;
    public ushort SourceVk;
    public RemoteKeyMode Mode;

    public RemoteKeyDef(string id, string name, string title, ushort vk, RemoteKeyMode mode) {
        Id = id;
        Name = name;
        Title = title;
        SourceVk = vk;
        Mode = mode;
    }
}

public static class RemoteCatalog {
    public static readonly RemoteKeyDef[] All = {
        new RemoteKeyDef("power", "电源键", "电源键", 0xFF, RemoteKeyMode.Editable),
        new RemoteKeyDef("home",  "主页键", "主页键", 0x24, RemoteKeyMode.Editable),
        new RemoteKeyDef("menu",  "菜单键", "菜单键", 0x5D, RemoteKeyMode.Editable),
        new RemoteKeyDef("tv",    "直播键", "TV 键",  0xC0, RemoteKeyMode.Editable),
        new RemoteKeyDef("voice", "语音键", "语音键", 0x74, RemoteKeyMode.Voice)
    };

    public static readonly RemoteKeyDef[] FileOrder = {
        All[0], All[1], All[2], All[3]
    };

    public static List<KeyBinding> DefaultBindings() {
        return new List<KeyBinding> {
            Parse("电源键    = 0xFF -> TAP LALT+TAB | HOLD 600 -> TASKVIEW"),
            Parse("主页键    = 0x24 -> TAP LALT+X | HOLD 600 -> TAP LALT+F4"),
            Parse("菜单键    = 0x5D ->"),
            Parse("直播键    = 0xC0 -> ESC")
        };
    }

    public static RemoteKeyDef FindById(string id) {
        if (id == null) return null;
        foreach (RemoteKeyDef def in All)
            if (String.Equals(def.Id, id, System.StringComparison.OrdinalIgnoreCase))
                return def;
        return null;
    }

    public static RemoteKeyDef FindByVk(ushort vk) {
        foreach (RemoteKeyDef def in All)
            if (def.SourceVk == vk) return def;
        return null;
    }

    public static List<KeyBinding> Merge(IList<KeyBinding> loaded) {
        var byVk = new Dictionary<ushort, KeyBinding>();
        if (loaded != null) {
            foreach (KeyBinding b in loaded) byVk[b.SourceVk] = b;
        }
        var result = new List<KeyBinding>();
        foreach (RemoteKeyDef def in FileOrder) {
            KeyBinding b;
            if (byVk.TryGetValue(def.SourceVk, out b)) {
                if (string.IsNullOrEmpty(b.Name)) b.Name = def.Name;
                result.Add(b);
            } else {
                result.Add(new KeyBinding(def.Name, def.SourceVk, new ushort[0]));
            }
        }
        return result;
    }

    static KeyBinding Parse(string line) {
        return KeyMapConfig.ParseLines(new[] { line })[0];
    }
}
