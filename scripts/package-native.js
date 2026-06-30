const fs = require("fs");
const path = require("path");
const { execFileSync } = require("child_process");

const root = path.resolve(__dirname, "..");
const releaseDir = path.join(root, "release");
const output = path.join(releaseDir, "DavPilot-portable");
const packageDir = path.join(releaseDir, "DavPilot-package");
const depsDir = path.join(releaseDir, "deps");
const zipPath = path.join(releaseDir, "DavPilot-windows-x64.zip");
const nativeSource = path.join(root, "native", "DavPilotNative.cs");
const nativeManifest = path.join(root, "native", "DavPilotNative.manifest");
const assetsDir = path.join(root, "assets");

function assertExists(target, message) {
  if (!target || !fs.existsSync(target)) {
    throw new Error(message);
  }
}

function firstExisting(candidates) {
  return candidates.find((candidate) => candidate && fs.existsSync(candidate)) || null;
}

function findCsc() {
  const windir = process.env.WINDIR || "C:\\Windows";
  const candidates = [
    path.join(windir, "Microsoft.NET", "Framework64", "v4.0.30319", "csc.exe"),
    path.join(windir, "Microsoft.NET", "Framework", "v4.0.30319", "csc.exe")
  ];
  return firstExisting(candidates);
}

function findWinFspMsi() {
  if (!fs.existsSync(depsDir)) return null;
  return fs.readdirSync(depsDir)
    .filter((entry) => /^winfsp-.*\.msi$/i.test(entry))
    .map((entry) => path.join(depsDir, entry))
    .sort()
    .pop() || null;
}

function emptyDir(dir) {
  fs.mkdirSync(dir, { recursive: true });
  for (const entry of fs.readdirSync(dir)) {
    fs.rmSync(path.join(dir, entry), { recursive: true, force: true, maxRetries: 3, retryDelay: 300 });
  }
}

assertExists(nativeSource, "缺少原生程序源码 native\\DavPilotNative.cs。");
assertExists(nativeManifest, "缺少原生程序清单 native\\DavPilotNative.manifest。");
assertExists(path.join(assetsDir, "davpilot-icon.ico"), "缺少应用图标。");

const csc = findCsc();
assertExists(csc, "找不到 .NET Framework C# 编译器 csc.exe。");

const rcloneSource = firstExisting([
  path.join(depsDir, "rclone.exe"),
  path.join(process.env.LOCALAPPDATA || "", "DavPilot", "tools", "rclone.exe"),
  path.join(root, "tools", "rclone.exe")
]);
assertExists(rcloneSource, "缺少 rclone.exe。请先在 DavPilot 中下载 rclone，或放到 release\\deps\\rclone.exe。");

const winFspMsi = findWinFspMsi();
assertExists(winFspMsi, "缺少 WinFsp 安装包。请把官方 winfsp-*.msi 放到 release\\deps。");

emptyDir(output);
emptyDir(packageDir);

execFileSync(
  csc,
  [
    "/nologo",
    "/target:winexe",
    "/platform:x64",
    "/codepage:65001",
    `/out:${path.join(output, "DavPilot.exe")}`,
    `/win32icon:${path.join(assetsDir, "davpilot-icon.ico")}`,
    `/win32manifest:${nativeManifest}`,
    `/resource:${rcloneSource},DavPilot.rclone.exe`,
    "/reference:System.Windows.Forms.dll",
    "/reference:System.Drawing.dll",
    "/reference:System.Web.Extensions.dll",
    "/reference:System.ServiceProcess.dll",
    "/reference:System.IO.Compression.dll",
    "/reference:System.IO.Compression.FileSystem.dll",
    nativeSource
  ],
  { stdio: "inherit" }
);

const instructionPath = path.join(packageDir, "使用说明.txt");
const msiName = path.basename(winFspMsi);

fs.copyFileSync(path.join(output, "DavPilot.exe"), path.join(packageDir, "DavPilot.exe"));
fs.copyFileSync(winFspMsi, path.join(packageDir, msiName));
fs.writeFileSync(instructionPath, `DavPilot 使用说明

本压缩包只包含三个文件：
1. DavPilot.exe
2. ${msiName}
3. 使用说明.txt

使用方法：
1. 解压整个压缩包。
2. 如果本机没有安装 WinFsp，先运行 ${msiName} 并完成安装。
3. 双击 DavPilot.exe，添加 WebDAV 地址、账号、密码和盘符。
4. 点击“测试连接”，确认通过后点击“挂载”。

说明：
- DavPilot 是免安装程序，但 Windows 盘符挂载依赖系统组件 WinFsp，所以 WinFsp 仍需要安装。
- rclone 已经内置在 DavPilot.exe 中。首次运行时，程序会自动释放到 %LOCALAPPDATA%\\DavPilot\\tools\\rclone.exe。
- 连接配置、缓存和日志保存在 %LOCALAPPDATA%\\DavPilot。
- 升级 DavPilot.exe 前，请先在软件中卸载盘符并停止系统服务。

开源协议：
- DavPilot 使用 GNU GPLv3 or later（GPL-3.0-or-later）发布。
- rclone 使用 MIT License；本版本内置未经修改的 rclone.exe。
- WinFsp Copyright (C) Bill Zissimopoulos；安装包为官方未修改 MSI，遵循 WinFsp 项目的 GPLv3/LGPLv3 及 FLOSS exception 等许可条款。
- 分发或销售本软件时，需要同步提供 DavPilot 对应版本的完整源代码、GPLv3 协议文本和第三方组件许可说明。
- 第三方项目源码和协议请查看：
  rclone: https://rclone.org/licence/
  WinFsp: https://github.com/winfsp/winfsp/blob/master/License.txt
`, "utf8");

fs.rmSync(zipPath, { force: true });
execFileSync(
  "powershell.exe",
  [
    "-NoProfile",
    "-ExecutionPolicy",
    "Bypass",
    "-Command",
    `Compress-Archive -Path '${path.join(packageDir, "*").replace(/'/g, "''")}' -DestinationPath '${zipPath.replace(/'/g, "''")}' -Force`
  ],
  { stdio: "inherit" }
);

console.log(`原生便携版已生成：${path.join(output, "DavPilot.exe")}`);
console.log(`发行压缩包已生成：${zipPath}`);
