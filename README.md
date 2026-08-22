# RemoteMapper · 小米蓝牙语音遥控器 Windows 增强助手（免驱动版）

> 💡 **无需开启 Windows 测试模式，无需安装内核驱动，开箱即用，绝不影响游戏反作弊与系统安全性！**

**RemoteMapper** 是一款专为 Windows 10/11 打造的小米蓝牙遥控器功能增强工具。它可以把小米电视/盒子蓝牙遥控器变成强大的 **电脑远距离控制器** 和 **无线语音输入麦克风**。

---

## ✨ 核心特性

* 🎙️ **无线语音输入（支持任意输入法）**：
  * 按住遥控器语音键即可说话，松开即结束，实时流式采集蓝牙音频并推送至虚拟声卡（VB-Cable）；
  * 自动注入唤醒热键，支持自由配置微信输入法（`右Alt + ,`）、讯飞、搜狗、自定义输入法（如 `F8`、`Ctrl+Shift+V` 等）。
* 🛡️ **底层智能吞除 F5 污染**：
  * 遥控器硬件发出的 `F5` 键盘连发会被程序全局钩子精准捕获并丢弃，彻底告别浏览器疯狂刷新和输入法热键被冲烂的问题。
* 🎮 **独立按键自定义（手势增强）**：
  * **电源键**、**主页键**、**菜单键**、**直播键（TV键）** 4 大按键全面支持自定义 **单击**、**双击**、**长按**、**连发**；
  * 可配置快捷键、运行程序（`LAUNCH`）、执行命令（`CMD` / `PowerShell`）、任务视图（`TASKVIEW`）或动态 C# 表达式（`CODE`）。
* 📺 **影音控制原生友好**：
  * **方向键（上/下/左/右）** 与 **确定键（OK）** 保持 Windows 原生行为，看视频快进/快退/调播放器音量极其顺手，且绝不波及物理键盘。
* 🌐 **全新现代化控制面板**：
  * **图形化卡片视图**：左右对称、防溢出排版，直观配置每个按键的点击动作；
  * **JSON 纯文本在线编辑器**：内置暗黑代码编辑器，支持格式化、语法校验、一键保存并热重载；
  * **语音热键可视化设置**：点击即可一键键盘录制输入法唤醒快捷键。
* ⚡ **极低资源占用**：
  * 采用全事件驱动架构，日常待机 **0.0% CPU**，物理内存仅占用约 **38 MB**。

---

## 🚀 快速上手

### 1. 前置准备（只需一次）

1. **配对遥控器**：
   * 打开 Windows「设置」➔「蓝牙和其他设备」➔「添加设备」；
   * 按住遥控器主页键+菜单键进行配对，连接「小米蓝牙语音遥控器（MI RC）」。
2. **安装虚拟声卡 [VB-Cable](https://vb-audio.com/Cable/)（免费）**：
   * 下载并解压 VB-Cable，右键以管理员身份运行 `VBCABLE_Setup_x64.exe` 安装；
   * 安装后系统会生成 `CABLE Input`（播放端）与 `CABLE Output`（录音端）。
3. **配置你的语音输入法**：
   * 打开你的语音输入法设置（如微信输入法、讯飞、搜狗等）；
   * 将输入法的麦克风输入源指定为 **`CABLE Output (VB-Audio Virtual Cable)`**；
   * 查看并记下输入法的语音唤醒热键（例如微信输入法默认为 `右Alt + 逗号`）。

---

### 2. 启动与常驻

| 操作方式 | 说明 |
| :--- | :--- |
| **日常使用（后台静默常驻）** | 双击运行 **`start.vbs`**（无黑框弹窗，托盘常驻，日志写入 `RemoteMic.log`） |
| **前台调试（查看实时日志）** | 双击运行 **`debug.bat`**（实时输出按键与音频推流信息，按 `Ctrl+C` 退出） |
| **打开可视化配置面板** | **双击桌面右下角任务栏托盘图标**，或浏览器访问 `http://127.0.0.1:27170/` |
| **设置开机自启** | 双击运行 **`install-autostart.bat`**（卸载自启运行 `uninstall-autostart.bat`） |
| **停止程序** | 右键托盘图标点击「退出」，或双击运行 **`stop.bat`** |

---

## 🛠️ 按键配置说明 (`keymap.json`)

程序支持直接在 Web 面板中修改，也可直接编辑根目录下的 **`keymap.json`**（修改保存后自动热加载）：

```json
{
  "enabled": true,
  "voice": {
    "hotkey": "RALT+OEM_COMMA"
  },
  "keys": [
    {
      "id": "power",
      "name": "电源键",
      "vk": "0xFF",
      "click": { "kind": "combo", "tap": true, "keys": "LALT+TAB" },
      "hold": { "kind": "cmd", "ms": 600, "command": "powershell (Add-Type '[DllImport(\"user32.dll\")]public static extern int SendMessage(int hWnd,int hMsg,int wParam,int lParam);' -Name a -PassThru)::SendMessage(-1,0x0112,0xF170,2)" }
    },
    {
      "id": "home",
      "name": "主页键",
      "vk": "0x24",
      "click": { "kind": "combo", "tap": true, "keys": "LWIN+D" }
    },
    {
      "id": "menu",
      "name": "菜单键",
      "vk": "0x5D",
      "click": { "kind": "combo", "tap": false, "keys": "SPACE" }
    },
    {
      "id": "tv",
      "name": "直播键",
      "vk": "0xC0",
      "click": { "kind": "cmd", "command": "start https://www.douyin.com" },
      "hold": { "kind": "cmd", "ms": 600, "command": "C:\\Users\\Desktop\\切到ps5.bat" }
    }
  ]
}
```

### 动作类型 (`kind`) 说明：
* **`combo`**：组合键/单键点按（如 `LWIN+D`、`SPACE`、`LALT+F4`、`VOLUME_MUTE`）；
* **`cmd`**：执行终端命令（如 `start https://...`、打开批处理或 PowerShell）；
* **`launch`**：运行指定应用程序（`"command": "C:\\Path\\App.exe"`）；
* **`taskview`**：直接唤起 Windows 任务视图；
* **`code`**：执行一段 C# 表达式并输入结果（如 `DateTime.Now.ToString("HH:mm")`）。

---

## 📂 项目结构

```text
RemoteMapper/
├── RemoteMic.exe              # 核心主程序（编译产物）
├── start.vbs                  # 后台静默启动脚本
├── stop.bat                   # 停止进程脚本
├── debug.bat                  # 前台调试启动脚本
├── build.bat                  # 一键编译脚本（基于系统自带 csc.exe）
├── install-autostart.bat      # 安装开机自启
├── uninstall-autostart.bat    # 卸载开机自启
├── keymap.json                # 核心按键与语音热键配置文件
├── src/                       # 核心 C# 源码
│   ├── RemoteMic.cs           # BLE 蓝牙连接、ATVV 音频解码、F5 拦截与推流管道
│   ├── KeyMapConfig.cs        # 按键映射解析、序列化与 JSON 驱动
│   ├── KeyMapper.cs           # 全局按键生命周期与分发调度
│   ├── KeyMapPanel.cs         # 本地 HTTP API 与托盘图标服务
│   ├── KeyMapEngine.cs        # 手势状态机（单击/双击/长按/连发）
│   ├── KeyComboSender.cs      # Win32 SendInput 模拟输入底层
│   └── RemoteCatalog.cs       # 遥控器键位元数据目录
├── ui/                        # Web 控制面板前端
│   ├── keymap.html            # 现代化前端界面（图形化映射 + JSON 编辑器）
│   ├── remote.png             # 遥控器高清交互图
│   └── app.ico                # 托盘与窗口图标
└── tests/                     # 自动化测试套件
```

---

## 🔧 开发者编译与测试

本项目无需安装庞大的 Visual Studio，直接使用 Windows 系统自带的 .NET Framework 4.8 编译器即可一键编译：

```bat
# 运行单元测试
tests\_run_keymap_tests.bat

# 重新编译 RemoteMic.exe
build.bat
```

---

## ❓ 常见问题

1. **遥控器按了没反应？**
   * 遥控器长时间不用会进入低功耗蓝牙深度休眠，按任意键（如方向键）唤醒 1 秒后即可正常工作。
2. **为什么没有返回键和音量加减键的映射？**
   * 小米遥控器的返回和音量键走的是 Android 专属 HID 编码，Windows 官方内核驱动 `kbdhid.sys` 在内核层直接将其丢弃。为保证 100% 免驱动与系统反作弊兼容，本工具不对其强行修改，建议使用菜单键（空格）、主页键、电源键等进行手势替代。
3. **语音输入录不到声音？**
   * 请确认输入法设置中的麦克风已选中 **`CABLE Output`**，且程序中的「语音热键」与输入法设置的快捷键一致。
