# 构建与发布

## 环境

- Windows 10/11 x64；
- .NET 8 SDK；
- 可选：Visual Studio 2022 或 Rider。

## 构建

```powershell
dotnet restore DesktopPet.csproj
dotnet build DesktopPet.csproj -c Release
```

输出位于：

```text
bin/Release/net8.0-windows/win-x64/
```

## 自包含单文件发布

```powershell
dotnet publish DesktopPet.csproj -c Release -o publish/win-x64
```

项目文件已设置：

- `PublishSingleFile=true`
- `SelfContained=true`
- `RuntimeIdentifier=win-x64`
- `PublishReadyToRun=true`

发布目录会包含可执行文件、许可证、第三方声明、商业使用说明和宠物资源。

## 验证清单

1. 构建为 0 error；
2. 启动后能定位 Minecraft 窗口或给出可读诊断；
3. 三套 UI 均可打开和切换；
4. 右键界面模式菜单可重复打开；
5. 关闭程序后无残留输入按键；
6. `bin/`、`obj/`、`publish/` 不提交仓库；
7. 发布包包含 `LICENSE`、`THIRD_PARTY_NOTICES.md` 和 `COMMERCIAL_USE.md`。

## GitHub Actions

`.github/workflows/build.yml` 在 Windows runner 上执行 restore 和 Release build。由于项目是 Windows Forms，不在 Linux runner 上构建。
