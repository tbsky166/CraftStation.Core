# CraftStation.Core

> CraftStation 启动器的核心业务库，纯 .NET 类库，不依赖任何 UI 框架。

## 包含什么

- **版本与启动引擎**：基于 CmlLib 的版本清单、安装、修复、删除与游戏进程管理
- **账户**：微软正版登录（MSAL + WebView2 / 设备码）、离线账户、DPAPI 加密存储
- **下载镜像**：BMCLAPI / 官方 / 自定义源，失败自动回退，URL 重写与 SHA1 校验
- **实例管理**：实例元数据、版本隔离、游戏目录解析
- **加载器安装**：Forge、NeoForge、Fabric、Quilt、OptiFine、LiteLoader
- **资源与整合包**：Modrinth 搜索下载、CurseForge 支持、`.mrpack` / `.zip` 导入导出
- **模组体检**：模组元数据解析、缺失前置 / 冲突 / 重复 modId 检测、依赖树
- **服务器**：Minecraft 1.7+ 协议 Ping
- **基础设施**：设置持久化、日志、Java 扫描、更新检查

## 如何引入

推荐以 Git Submodule 方式引入并添加项目引用：

```powershell
git submodule add https://github.com/tbsky166/CraftStation.Core.git CraftStation.Core
```

```xml
<ProjectReference Include="CraftStation.Core\CraftStation.Core.csproj" />
```

## 配置

复制 `Config.cs.example` 为 `Config.cs`（已被 gitignore），填入你自己的 Azure Client ID 与回调地址：

```powershell
Copy-Item Config.cs.example Config.cs
```

## 构建与测试

```powershell
dotnet build CraftStation.Core.csproj
dotnet test ..\CraftStation.Tests
```

## 许可证

待定（开源发布前补充）。
