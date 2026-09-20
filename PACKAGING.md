# Codex Beacon 打包与发布规范

本文档是 Codex Beacon Windows 发布包的权威规范。发布构建、自动化脚本和后续维护必须遵循这里定义的产物形式、版本规则和验证流程。

## 1. 发布产物

每个版本必须同时提供两个 **Windows x64 单文件 EXE**：

| 版本 | 文件名 | 运行时策略 | 目标用户 |
| --- | --- | --- | --- |
| Portable（含运行时完整版） | `CodexBeacon-<version>.exe` | 内置 .NET 10 运行时和 Windows App SDK | 下载后直接运行，推荐给普通用户 |
| Slim（不含 .NET 运行时精简版） | `CodexBeacon-<version>-slim.exe` | 依赖本机 .NET 10 Desktop Runtime x64；Windows App SDK 仍随应用携带 | 已安装对应 .NET 运行时、希望减小下载体积的用户 |

同时生成：

- `publish\CodexBeacon.exe`：Portable 的无版本号便捷副本。
- `artifacts\SHA256SUMS.txt`：两个版本化 EXE 的 SHA-256 校验值。

禁止发布 ZIP、松散 DLL 目录或 `dotnet publish` 的原始输出目录。构建脚本会临时创建 `payload.zip` 并嵌入启动器，但该文件仅用于构建，脚本结束时必须删除，不能作为发布产物。

## 2. 单文件结构

两个版本都由外层单文件启动器和内嵌应用载荷组成：

1. 用户运行版本化 EXE。
2. 启动器停止旧的 Codex Beacon 实例。
3. 启动器按版本和内容签名解压内嵌载荷。
4. Portable 解压到 `%LOCALAPPDATA%\CodexBeacon\app-portable`。
5. Slim 解压到 `%LOCALAPPDATA%\CodexBeacon\app-slim`。
6. 启动器通过 `--launcher` 把自身路径交给应用，供应用原地更新。
7. 开机自启时，启动器继续传递 `--background`，应用初始化后隐藏到系统托盘。

Portable 与 Slim 必须使用不同的解压目录，防止两种载荷互相覆盖、复用错误运行时或产生文件锁冲突。

## 3. 版本规则

`build\version.txt` 是唯一版本源。

- 常规项目构建默认通过 `build\AutoVersion.targets` 增加补丁版本。
- 打包脚本读取当前版本，并为应用发布和启动器构建传入 `AutoVersionIncrement=false`，确保同一轮的 Portable、Slim、程序集元数据和文件名完全一致。
- 不得分别手工修改两个包的版本，也不得在两个变体之间再次增加版本。
- 发布前确认 `build\version.txt`、应用“关于”页、两个 EXE 文件名和启动器信息版本一致。

## 4. 标准打包命令

在仓库根目录使用 Windows PowerShell 运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Scripts\Build-SingleFile.ps1 -Target all
```

`-Target all` 是正式发布的唯一标准目标。`portable` 或 `slim` 仅用于单独诊断，不得代替正式双包构建。

成功后必须存在：

```text
publish\CodexBeacon-<version>.exe
publish\CodexBeacon-<version>-slim.exe
publish\CodexBeacon.exe
artifacts\CodexBeacon-<version>.exe
artifacts\CodexBeacon-<version>-slim.exe
artifacts\SHA256SUMS.txt
```

## 5. 发布前验证

### 5.1 编译和脚本检查

```powershell
dotnet build .\CodexBeacon.csproj -c Debug
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Scripts\Collect-CodexStatus.ps1
```

验收要求：

- Debug 构建为 0 warnings、0 errors。
- `Scripts\*.ps1` 可由 Windows PowerShell 5.1 正确解析，并保持 UTF-8 BOM。
- 状态采集返回有效 JSON，顶层 `Error` 为空。
- `%LOCALAPPDATA%\CodexBeacon\crash.log` 不存在或本轮运行没有新增错误。

### 5.2 应用行为检查

至少验证：

- 普通启动可以显示主窗口。
- 再次启动时只保留一个 Codex Beacon 实例。
- 点击窗口关闭按钮后，窗口隐藏但进程和托盘图标继续运行。
- 托盘图标可以恢复窗口；托盘菜单“退出”可以真正结束进程。
- 使用 `--background` 启动时不显示主窗口，只显示托盘图标。
- 语言切换、应用更新和内部窗口重载能够关闭旧窗口，不被“关闭到托盘”逻辑拦截。

### 5.3 双包冒烟测试

Portable 和 Slim 都必须直接运行版本化 EXE 进行测试，不能只测试 `dotnet publish` 输出目录：

- Portable 在没有外部 .NET 运行时依赖的前提下启动。
- Slim 在安装了 .NET 10 Desktop Runtime x64 的机器上启动。
- 两者均不应弹出缺少 Windows App Runtime 的提示。
- 两者均能接收并透传 `--background`。
- 外层启动器、解压缓存和应用进程均使用预期变体。

## 6. 发布流程

1. 完成功能修改和代码自检。
2. 更新 `CHANGELOG.md` 当前版本说明。
3. 完成编译、诊断和桌面运行验证。
4. 运行双包打包命令。
5. 对两个版本化 EXE 做冒烟测试。
6. 核对 `SHA256SUMS.txt`、文件名、版本和产物数量。
7. 提交清晰的 Conventional Commit。
8. 推送 GitHub，并使用 `artifacts` 中的两个 EXE 与校验文件创建 Release。

任何一项失败都不能把当前产物标记为可发布。

## 7. 常见错误

- **两个包都提示缺少 Windows App Runtime**：确认两个应用发布命令都包含 `WindowsAppSDKSelfContained=true`。
- **Portable 与 Slim 启动了错误载荷**：确认启动器分别使用 `app-portable` 和 `app-slim`，并且内容签名包含变体。
- **版本号不一致**：打包期间必须使用 `AutoVersionIncrement=false`，且整轮打包只读取一次 `build\version.txt`。
- **只得到一个大 EXE**：正式发布必须同时生成 Portable 和 Slim，不能只运行单一 Target。
- **Slim 仍然较大**：Slim 只移除 .NET 运行时；WinUI 3、Windows App SDK 和应用依赖仍需携带，这是预期行为。
- **发布目录中出现 ZIP**：ZIP 只能是构建过程的临时嵌入载荷，必须由脚本清理，不能上传。
