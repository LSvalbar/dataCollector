# DataCollector Enterprise

`enterprise` 是正式生产版工程，和现有 Python 演示采集工具并行存在，互不影响。

## 工程结构

- `src/DataCollector.Server.Api`
  中央服务端 API。负责设备档案、日报、时间线、公式、权限和实时接入。
- `src/DataCollector.Agent.Worker`
  车间边缘采集 Agent。正式部署时以 Windows Service 方式运行，负责直连机床、采集、缓存、补传、心跳上报。
- `src/DataCollector.Desktop.Wpf`
  正式客户端。首页是设备管理，另外包含统计报表、设备运行情况、用户角色权限、部署说明。
- `src/DataCollector.Core`
  正式版共享核心，负责时间统计和公式计算。
- `src/DataCollector.Contracts`
  服务端、客户端、Agent 共用的数据契约。
- `tests/DataCollector.Core.Tests`
  关键口径单元测试。

## 三端怎么理解

### 客户端

客户端是给管理者、IT 运维、车间主管使用的操作界面。

它负责：

- 看设备树和设备状态
- 维护部门、车间、设备信息
- 看每天的开机率、利用率、开机/加工/待机/关机统计
- 查看单台设备当天或历史某天的时间线
- 维护公式
- 管理用户、角色和权限

### 服务端

服务端是机房里的中心系统。

它负责：

- 接收 Agent 上传的实时数据
- 存设备主数据
- 存时间线和日报
- 存公式和权限
- 给客户端提供统一 API

### Agent

Agent 是车间里的采集程序。

它负责：

- 直接连接机床网络
- 调用 FANUC FOCAS 采集
- 本地缓存
- 断线重连
- 恢复后补传
- 上报心跳和实时快照

## 你要做什么

1. 在机房准备一台中央服务主机，部署服务端和数据库。
2. 每个车间至少准备一台 Agent 采集机，最好双网卡。
3. 在客户端录入部门、车间、设备、IP、端口、Agent 节点。
4. 先用一台样机做点位和口径确认，再逐步扩到整车间。
5. 管理口径确定后，在统计报表页维护开机率和利用率公式。

## 启动方式

### 开发机直接运行

1. 启动服务端：

```powershell
dotnet run --project D:\Project\Codex\DataCollector\enterprise\src\DataCollector.Server.Api
```

2. 启动客户端：

```powershell
dotnet run --project D:\Project\Codex\DataCollector\enterprise\src\DataCollector.Desktop.Wpf
```

3. 启动 Agent：

```powershell
dotnet run --project D:\Project\Codex\DataCollector\enterprise\src\DataCollector.Agent.Worker
```

### Win10 测试机运行

如果测试机没有开发环境，不要在测试机上跑源码。正确做法是先在开发机执行：

```powershell
powershell -ExecutionPolicy Bypass -File D:\Project\Codex\DataCollector\enterprise\scripts\Publish-Win10Portable.ps1
```

打包产物在：

`D:\Project\Codex\DataCollector\enterprise\dist\win-x64`

把整个 `win-x64` 目录复制到测试机，例如：

`D:\source\dataCollector\enterprise\dist\win-x64`

然后在测试机直接双击：

`Start-Enterprise-All.bat`

当前发布包是自包含 EXE，不依赖测试机预装 .NET Runtime。

## 当前状态

- 已有正式版客户端、服务端、Agent 工程骨架
- 已有设备树菜单、设备详情弹窗、1 秒自动刷新
- 已有统计报表、设备运行时间线、用户角色权限页面
- 已有服务端实时快照接入和 Agent 实时采集入口
- 当前数据库仍是内存演示仓储，后续要切正式数据库
- Python 版本保留，继续作为现场 PoC 和点位验证工具

## 下一步建议

1. 把服务端仓储切到正式数据库
2. 把 Agent 的断线补传、本地缓存和历史沉淀补齐
3. 把更多 FANUC 原生点位接进正式版日报和时间线
4. 形成机型模板和口径模板，再扩到全车间
