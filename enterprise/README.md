# DataCollector Enterprise

这套 `enterprise` 工程是正式生产版骨架，和现有 Python 演示版并行存在，互不影响。

## 工程结构

- `src/DataCollector.Server.Api`
  中央服务端 API。负责设备主数据、公式、日报、时间线、权限等统一服务。
- `src/DataCollector.Agent.Worker`
  车间边缘采集 Agent。正式部署时以 Windows Service 方式运行，负责本地采集、本地缓存、补传、心跳。
- `src/DataCollector.Desktop.Wpf`
  正式客户端。首页是设备管理，另外包含统计报表、设备运行情况、用户角色权限页面。
- `src/DataCollector.Core`
  正式版共享核心，包括时间统计聚合、公式引擎。
- `src/DataCollector.Contracts`
  服务端、客户端、Agent 共用契约。
- `tests/DataCollector.Core.Tests`
  关键口径单元测试。

## 启动方式

1. 启动中央服务端

```powershell
dotnet run --project D:\Project\Codex\DataCollector\enterprise\src\DataCollector.Server.Api
```

2. 启动桌面客户端

```powershell
dotnet run --project D:\Project\Codex\DataCollector\enterprise\src\DataCollector.Desktop.Wpf
```

3. 启动 Agent 骨架

```powershell
dotnet run --project D:\Project\Codex\DataCollector\enterprise\src\DataCollector.Agent.Worker
```

## 当前状态

- 已有正式版工程骨架
- 已有设备管理、统计报表、设备运行情况、用户角色权限四个客户端页面
- 已有服务端 API 骨架和公式配置接口
- 已有 Agent Windows Service 骨架
- 当前数据源是内存种子数据，用于评审架构和界面
- 现有 Python 版本完全保留，继续作为现场 FOCAS 演示工具

## 下一步建议

1. 用当前 Python 版继续把 FANUC 原生点位口径打准
2. 将正式版 Agent 接 FANUC FOCAS 原生采集驱动
3. 将服务端仓储从内存实现替换为 PostgreSQL 或 SQL Server 2022
4. 接入认证、审计、幂等上传、本地缓存补传

详细部署和准确性策略见：

[production-architecture-zh.md](D:/Project/Codex/DataCollector/enterprise/docs/production-architecture-zh.md)
