using System;
using System.Collections.Generic;

public enum RemoteKeyMode { Editable, Voice }

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

// Driverless VK values (no kernel filter installed).
// The remote arrives as a standard HID keyboard; Windows generates these VKs.
// NOTE: 返回键/音量加/音量减 are dropped by kbdhid.sys and never reach userspace
//       — they are intentionally absent from this catalog.
public static class RemoteCatalog {
    public static readonly RemoteKeyDef[] All = {
        new RemoteKeyDef("power", "电源键", "电源键", 0xFF, RemoteKeyMode.Editable),
        new RemoteKeyDef("ok",    "确定键", "确定键", 0x0D, RemoteKeyMode.Editable),
        new RemoteKeyDef("up",    "方向上", "上键",   0x26, RemoteKeyMode.Editable),
        new RemoteKeyDef("down",  "方向下", "下键",   0x28, RemoteKeyMode.Editable),
        new RemoteKeyDef("left",  "方向左", "左键",   0x25, RemoteKeyMode.Editable),
        new RemoteKeyDef("right", "方向右", "右键",   0x27, RemoteKeyMode.Editable),
        new RemoteKeyDef("home",  "主页键", "主页键", 0x24, RemoteKeyMode.Editable),
        new RemoteKeyDef("menu",  "菜单键", "菜单键", 0x5D, RemoteKeyMode.Editable),
        new RemoteKeyDef("tv",    "直播键", "TV 键",  0xC0, RemoteKeyMode.Editable),
        new RemoteKeyDef("voice", "语音键", "语音键", 0x74, RemoteKeyMode.Voice)
    };

    // Order shown in generated keymap.json (voice excluded — it has its own config section).
    public static readonly RemoteKeyDef[] FileOrder = {
        All[0], All[1], All[2], All[3], All[4], All[5],
        All[6], All[7], All[8]
    };

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

    public static List<KeyBinding> DefaultBindings() {
        var result = new List<KeyBinding>();
        foreach (RemoteKeyDef def in FileOrder)
            result.Add(new KeyBinding(def.Name, def.SourceVk, new ushort[0]));
        return result;
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
}
