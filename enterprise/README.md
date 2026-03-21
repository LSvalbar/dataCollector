# DataCollector Enterprise

`enterprise` 是正式生产版工程，和现有 Python 演示采集工具并行存在，互不影响。

## 工程结构

- `src/DataCollector.Server.Api`
  中央服务端 API。负责设备档案、日报、时间线、公式、权限、SQLite 持久化和实时接入。
- `src/DataCollector.Agent.Worker`
  车间边缘采集 Agent。正式部署时以 Windows Service 方式运行，负责直连机床、采集、缓存、补传、心跳上报。
- `src/DataCollector.Desktop.Wpf`
  正式客户端。首页是设备管理，另外包含统计报表、设备运行情况、用户角色权限、部署说明。
- `src/DataCollector.Launcher.Wpf`
  Win10 启动器。双击一个 EXE 就能启动服务端、客户端、Agent，并保存本机的服务端地址与 Agent 节点。
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
- 把实时状态和历史时间线写入数据库
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

## 数据库持久化

正式版服务端已经切到 `SQLite` 持久化。

- 默认数据库文件：`server\data\enterprise.db`
- 服务端首次启动会自动建库和初始化基础数据
- 当前已经持久化：设备、公式、用户、角色、实时状态、时间线
- 这样即使服务端重启，设备档案和历史时间线也不会丢

这一步的目的，是先把试点跑稳。后面如果你要切 `SQL Server` 或其它数据库，再在当前持久层边界上加 provider，不是继续回到内存仓储。

## 你要做什么

1. 在机房准备一台中央服务主机，部署服务端和数据库。
2. 每个车间至少准备一台 Agent 采集机，最好双网卡。
3. 在客户端录入部门、车间、设备、IP、端口、Agent 节点。
4. 先用一台样机做点位和口径确认，再逐步扩到整车间。
5. 管理口径确定后，在统计报表页维护开机率和利用率公式。

## Agent 怎么用

Agent 现在优先按 `Agent 节点` 从服务端自动拉取设备配置。

配置文件在：

- `src/DataCollector.Agent.Worker/appsettings.json`
- 发布包里是：`agent\appsettings.json`

关键字段如下：

- `ServerBaseUrl`
  服务端地址，正常填：`http://localhost:5180`
- `AgentNodeName`
  必须和客户端里设备档案的 `Agent 节点` 完全一致。
- `PollIntervalMilliseconds`
  轮询周期。
- `ConfigurationRefreshSeconds`
  Agent 从服务端拉取设备配置的刷新周期。

正常使用时，`Machines[]` 不需要再手工维护。只要客户端设备档案里的设备绑定到了同一个 `Agent 节点`，Agent 就会自动拉下来。

## 为什么机床能 ping 通，但界面还是显示离线

`Enterprise` 版显示在线，不是只看 `ping`，而是看：

1. Agent 是否真的启动了
2. Agent 是否成功读到了 FOCAS 数据
3. Agent 是否把快照上传到了服务端
4. 这台设备是否真的绑定到了当前 `Agent 节点`
5. `AgentNodeName` 是否和设备档案里一致
6. 该设备是否被启用

所以你会遇到这种情况：

- `ping` 通
- Python 版也能采
- 但 Enterprise 里还是离线

这通常说明：

- 设备没有绑定到当前 Agent 节点
- `AgentNodeName` 不一致
- Agent 没启动
- 服务端地址不对

当前 Agent 已经补了更明确的日志。如果服务端没匹配到设备，会直接提示：

- 未找到设备编码
- Agent 节点不匹配
- 设备已被禁用

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

4. 启动统一启动器：

```powershell
dotnet run --project D:\Project\Codex\DataCollector\enterprise\src\DataCollector.Launcher.Wpf
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

`DataCollectorLauncher.exe`

启动器会让你直接填写：

- 服务端地址
- Agent 节点

保存后就可以分别启动服务端、客户端、Agent，或者一键全部启动。

当前发布包是自包含 EXE，不依赖测试机预装 .NET Runtime。

## 当前状态

- 已有正式版客户端、服务端、Agent 工程骨架
- 已有设备树菜单、设备详情弹窗、1 秒自动刷新
- 已有统计报表、设备运行时间线、用户角色权限页面
- 已有服务端实时快照接入和 Agent 实时采集入口
- Agent 已改成按节点自动从服务端拉取设备配置
- 已有统一启动器 EXE，替代四个 bat 的主入口
- 服务端已切到 SQLite 持久化
- Python 版本保留，继续作为现场 PoC 和点位验证工具

## 下一步建议

1. 把 Agent 的断线补传、本地缓存和历史沉淀补齐
2. 做服务端数据库 provider 扩展，再评估 SQL Server / MySQL 接入
3. 把更多 FANUC 原生点位接进正式版日报和时间线
4. 形成机型模板和口径模板，再扩到全车间
