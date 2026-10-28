# SPlayer

SPlayer 是 SinE 系列里的 Windows 本地影音播放器。它播放本机、映射网络盘、UNC 路径和用户输入的普通 HTTP/HTTPS 地址，不提供片库、海报、在线字幕、账号或遥测。

界面沿用 SinE 已有的 Windows 10 后期风格：浅色、白色内容区、浅灰 Acrylic 侧栏、方形控件、Segoe UI 与微软雅黑。视频画面和控制条可以是深色，应用外壳不是深色主题。

## 运行

- Windows 10 版本 1903（内部版本 18362）或更高，x64。也在 Windows 11 上使用。
- 需要显卡驱动提供的 Vulkan 运行库。发布包已带 `vulkan-1.dll`。没有 Vulkan 驱动时，硬件解码可能失败并回退到软件解码；加载器本身仍要在程序旁边。
- 关闭窗口即退出，不驻留后台。安装程序不会悄悄抢走默认播放器。
- 设置里的「打开默认应用设置」会先登记 SPlayer，再跳到 Windows 的「默认应用」页面。请在「视频播放器」里选择 SPlayer。

安装包是 `SPlayer-Setup-0.1.2.1.exe`。它把程序装到 `%LOCALAPPDATA%\SinE\SPlayer`，并在开始菜单放快捷方式。也可以解压便携包后直接运行 `SPlayer.exe`。

- 直接打开：主界面，左侧是最近播放、播放列表和设置。
- 把文件拖进窗口，或用「打开文件 / 打开文件夹 / 打开地址」。
- 从资源管理器「打开方式」或命令行传入媒体路径时，只打开播放窗口并自动播放，不经过主界面。
- 播放中按 Esc，或点左上角返回，回到主界面。用命令行打开时没有返回按钮，关闭窗口就退出。

```text
SPlayer.exe
SPlayer.exe "D:\影片\示例.mkv"
SPlayer.exe "\\nas\media\影片.mp4"
```

播放记录、设置和上次的播放列表写在 `%LOCALAPPDATA%\SinE\SPlayer`。不会上传文件名或路径。

## 播放

支持播放、暂停、停止、跳转、快进快退、上一个、下一个、音量、静音、0.25x 到 4x 倍速、单曲循环、列表循环、随机播放、旋转、画面比例、逐帧、截图和播放信息。

进度条拖动时按关键帧预览，松开后精确跳转，避免连续精确 Seek 把解码器堵住。

字幕使用 libmpv 渲染。同目录里文件名相近的外挂字幕会自动加载，也可以手动导入。可切换轨道、开关、延迟、字号和位置。内嵌字幕和外挂 SRT、ASS、WebVTT 按内核能力解码。

纯音频使用白色的简单布局，显示标题、能读到的艺术家和专辑封面。没有音乐库。

未看完的文件默认从上次位置继续。可在设置里关闭记录或清空历史。

## 快捷键

文字输入时这些键不会抢走输入。

| 键 | 作用 |
| --- | --- |
| 空格 | 播放 / 暂停 |
| 左 / 右 | 快退 / 快进 |
| 上 / 下 | 音量 |
| F 或双击画面 | 全屏 |
| Esc | 退出全屏；窗口播放时返回主界面 |
| M | 静音 |
| Ctrl+O | 打开文件 |
| 滚轮 | 调节音量（可在设置里关闭） |

其余操作在画面右键菜单里。

全屏会占满当前显示器，隐藏窗口边框和任务栏。鼠标停住后控制条隐藏。退出全屏恢复进入前的位置和大小。

## 从源码构建

需要 .NET 10 SDK（目标运行时仍为 .NET 8）、Windows App SDK 的 NuGet 包（工程会自己还原）和 7-Zip。

```powershell
powershell -File tools\fetch-deps.ps1
dotnet build SPlayer.slnx -c Release -p:Platform=x64
dotnet publish src\SPlayer.App\SPlayer.App.csproj -c Release -r win-x64 --self-contained true -p:Platform=x64 -o artifacts\publish
```

`fetch-deps.ps1` 下载并核对固定 SHA-256 后解压放置：

- shinchiro `mpv-winbuild-cmake` 标签 `20261021` 的 `libmpv-2.dll`
- 同一标签的 `ffmpeg.exe`，只给测试样本使用
- 若旁边还没有加载器，则从 `C:\Windows\System32\vulkan-1.dll` 复制

测试样本和自动化测试：

```powershell
powershell -File tools\generate-samples.ps1
dotnet test SPlayer.slnx -c Release -p:Platform=x64
```

持续集成在 `.github/workflows/windows.yml`。它安装 Vulkan Runtime 1.3.296.0，再构建、测试并上传便携 zip。大体积二进制不进 Git。

## 许可证

GPL-2.0-or-later。完整说明见 `THIRD_PARTY_NOTICES.txt` 和 `LICENSE`。

实测记录、格式清单和未完成项：

- [测试记录](docs/测试记录.md)
- [格式兼容](docs/格式兼容.md)
- [已知问题](docs/已知问题.md)

## 0.1.2.1 窗口比例与升级

设置中的“跟随视频比例”默认开启：从“打开方式”打开视频时，窗口自动适配当前视频比例，让完整画面铺满内容区。关闭后保留上次窗口比例；不会拉伸画面，必要时会出现留白。主界面打开视频保留主窗口尺寸。

安装包沿用既有 AppId，自动识别旧安装目录并覆盖升级，保留设置与播放历史，无需先卸载。安装时会提示正常关闭占用程序。

应用图标包含多尺寸 ICO，并随程序分发 Windows 10 小/中磁贴清单。旧磁贴若显示缓存图标，可取消固定后重新固定。Win10 磁贴面板尚未实机验收。

发布页：https://github.com/SinEAI/SPlayer-with-SinE/releases
云端构建生成安装包、便携 zip 和 SHA256SUMS.txt。公开附件来自经过检查的构建结果。
