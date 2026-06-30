# DavPilot

DavPilot 是一款原生 Windows WebDAV 磁盘工具，界面为中文，底层使用 rclone 挂载 WebDAV，并通过 WinFsp 显示为 Windows 盘符。当前版本是 Windows 原生 WinForms / .NET Framework 单 exe，不使用 Electron。

## 当前功能

- 新建、编辑、保存、删除 WebDAV 连接
- 支持用户名/密码和匿名访问
- 保存连接时使用 rclone obscured password，不保存明文密码
- 测试连接、挂载、卸载、打开盘符
- 支持网络驱动器模式、只读模式、系统服务启动后自动挂载
- 遇到不受信任的 HTTPS 证书时弹窗确认
- rclone 内置在 DavPilot.exe 中，首次运行时自动释放到本地数据目录
- 可打开 rclone.conf、缓存目录和日志目录
- UI 关闭后可留在托盘后台运行
- 可注册为 Windows 系统服务，注册时通过 UAC 索要管理员权限

## 发行包

打包后生成：

```text
release\DavPilot-windows-x64.zip
```

压缩包只包含三个文件：

```text
DavPilot.exe
winfsp-*.msi
使用说明.txt
```

DavPilot 本体免安装。WinFsp 是系统驱动组件，首次使用挂载功能前仍需要运行压缩包里的 `winfsp-*.msi` 安装。

配置、rclone、缓存和日志统一保存在：

```text
%LOCALAPPDATA%\DavPilot
```

## 使用前准备

1. 解压 `release\DavPilot-windows-x64.zip`。
2. 如果本机没有安装 WinFsp，运行压缩包里的 `winfsp-*.msi`。
3. 双击 `DavPilot.exe`。
4. 添加 WebDAV 连接，点击“测试连接”，通过后点击“挂载”。

## 后台和系统服务

- “UI 后台”：关闭窗口后，程序留在右下角托盘运行。
- “系统服务”：点击“安装系统服务”后会弹出 UAC 管理员权限确认。安装成功后，服务会随系统启动。

系统服务只会挂载已勾选“系统服务启动后自动挂载”的连接。建议先在 UI 中保存并测试连接，再安装或启动系统服务。

如果要更新 `DavPilot.exe`，请先在 UI 里卸载盘符并停止系统服务，或用管理员权限执行：

```powershell
sc stop DavPilotWebDavService
```

## 打包

打包前需要准备两个第三方文件：

```text
release\deps\rclone.exe
release\deps\winfsp-*.msi
```

如果 `release\deps\rclone.exe` 不存在，脚本会尝试使用 `%LOCALAPPDATA%\DavPilot\tools\rclone.exe`。

运行：

```powershell
pnpm dist
```

脚本使用 Windows 自带的 .NET Framework C# 编译器生成 WinForms 程序，不需要 Electron，也不需要 .NET SDK。

## 开源协议

DavPilot 使用 GNU General Public License v3.0 or later（GPL-3.0-or-later）开源，详见 [LICENSE](LICENSE)。

第三方组件：

- rclone 使用 MIT License，见 <https://rclone.org/licence/>。
- WinFsp Copyright (C) Bill Zissimopoulos，WinFsp 安装包保持官方未修改形态，见 <https://github.com/winfsp/winfsp/blob/master/License.txt>。

分发或销售 DavPilot 时，需要同步提供对应版本的完整源代码、GPLv3 协议文本和第三方组件许可说明。

## 项目结构

```text
native   原生 WinForms 程序源码
scripts  打包与检查脚本
assets   应用图标
release  打包输出目录
```
