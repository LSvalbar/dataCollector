# dataCollector

用于笔记本或采集主机通过以太网直连 FANUC 机床进行单机数采的第一版程序。

当前这版先聚焦一件事：

`先稳定采到一台机床的数据，并把数据可靠落到本地 SQLite。`

## 当前能力

1. 通过 `FOCAS over Ethernet` 连接 FANUC 机床。
2. 固定周期轮询机床状态。
3. 自动重连，网络抖动后继续采集。
4. 本地落库到 SQLite。
5. 记录状态变化事件。
6. 统计派生的开机累计时长、运行累计时长。
7. 支持导出 CSV。
8. 提供本地 GUI 图形界面。

## 当前采集字段

1. 系统信息
2. 自动模式号
3. 运行模式号
4. 急停状态
5. 报警状态
6. 控制器模式文本
7. OEE 状态文本
8. 派生的开机累计时长
9. 派生的运行累计时长

## 目录说明

1. `run_collector.py`
   程序入口。
2. `config/`
   机床配置文件。
3. `scripts/`
   启动脚本、GUI 启动脚本和网络预检脚本。
4. `src/fanuc_collector/`
   采集核心代码。
5. `docs/`
   方案和操作文档。

## 运行前准备

### 1. 准备 Python

当前程序基于 `Python 3.11+`。

检查命令：

```powershell
python --version
```

### 2. 准备 FOCAS DLL

把 `fwlib64.dll` 放到下面目录：

```text
D:\Project\Codex\DataCollector\vendor\fwlib64.dll
```

注意：

1. 这版程序当前按 `64 位 Python + fwlib64.dll` 运行。
2. 如果你手里只有 `fwlib32.dll`，这版不能直接运行。
3. 如果需要，我下一步可以再给你做 32 位兼容版。
4. 我已经把 DLL 放置目录准备好了，但无法替你自动下载官方 DLL，因为供应方下载页面要求登录。

### 3. 检查机床网络

先确认笔记本能和机床正常通信：

```powershell
Test-NetConnection 192.168.91.46 -Port 8193
```

或者运行预检脚本：

```powershell
powershell -ExecutionPolicy Bypass -File D:\Project\Codex\DataCollector\scripts\Test-MachineEndpoint.ps1 -IpAddress 192.168.91.46 -Ports 8193 -OutputPath D:\Project\Codex\DataCollector\machine-precheck.json
```

## 配置文件

已经按你当前测试机床预填好一个配置：

[config/machine.192.168.91.46.json](D:/Project/Codex/DataCollector/config/machine.192.168.91.46.json)

关键参数如下：

1. `ip`
   当前机床 IP，默认 `192.168.91.46`
2. `port`
   默认 `8193`
3. `poll_interval_ms`
   轮询周期，默认 `500ms`
4. `snapshot_interval_ms`
   快照落盘周期，默认 `5000ms`
5. `running_operation_modes`
   当前先按 `[1, 2, 3]` 作为运行时间累计口径

## 怎么运行

### 方式 0：启动 GUI 图形界面

如果你希望尽量少敲命令，直接用这个：

```powershell
powershell -ExecutionPolicy Bypass -File D:\Project\Codex\DataCollector\scripts\Start-FanucCollectorGui.ps1
```

对应脚本文件：

[scripts/Start-FanucCollectorGui.ps1](D:/Project/Codex/DataCollector/scripts/Start-FanucCollectorGui.ps1)

### 方式 A：直接运行启动脚本

这是最简单的方式：

```powershell
powershell -ExecutionPolicy Bypass -File D:\Project\Codex\DataCollector\scripts\Start-FanucCollector.ps1
```

对应脚本文件：

[scripts/Start-FanucCollector.ps1](D:/Project/Codex/DataCollector/scripts/Start-FanucCollector.ps1)

### 方式 B：直接运行 Python 命令

```powershell
python D:\Project\Codex\DataCollector\run_collector.py run --config D:\Project\Codex\DataCollector\config\machine.192.168.91.46.json
```

## 运行后会生成什么

### 1. SQLite 数据库

```text
D:\Project\Codex\DataCollector\data\fanuc-poc-192.168.91.46.db
```

### 2. 运行日志

```text
D:\Project\Codex\DataCollector\logs\fanuc-poc-192.168.91.46.log
```

## 怎么看采集结果

### 1. 查看最新值

```powershell
python D:\Project\Codex\DataCollector\run_collector.py show-latest --db D:\Project\Codex\DataCollector\data\fanuc-poc-192.168.91.46.db
```

### 2. 导出快照 CSV

```powershell
python D:\Project\Codex\DataCollector\run_collector.py export-snapshots --db D:\Project\Codex\DataCollector\data\fanuc-poc-192.168.91.46.db --out D:\Project\Codex\DataCollector\data\fanuc-poc-192.168.91.46.csv
```

## 首次现场测试建议

第一次到现场，不要只看“有没有数据”，要同时看下面几点：

1. `alarm_state` 在报警和非报警时是否变化正确。
2. `emergency_state` 在急停和非急停时是否变化正确。
3. `operation_mode` 在空闲、自动运行、暂停时分别是多少。
4. `controller_mode_text` 是否与机床当前模式一致。
5. 日志里是否有重连、报错、超时。

特别注意：

`running_operation_modes` 当前默认是 `[1, 2, 3]`，这是第一版经验值，不一定和你的现场完全一致。

也就是说，程序现在会把 `operation_mode = 1/2/3` 先视为“运行时间累计”。

你第一次现场测试后，要把这几个状态对应关系告诉我：

1. 空闲时 `operation_mode` 是多少
2. 自动运行时 `operation_mode` 是多少
3. 暂停时 `operation_mode` 是多少

我拿到这三个值后，就能把“运行时间”的判断规则校准准确。

## 现在你最需要做的事

1. 放入 `fwlib64.dll`
2. 优先运行 GUI 启动脚本
3. 执行 `show-latest`
4. 把日志文件前几十行和 `show-latest` 输出发给我

## 相关文件

[总体方案](D:/Project/Codex/DataCollector/docs/architecture-proposal.md)

[单机验证计划](D:/Project/Codex/DataCollector/docs/single-machine-poc-plan.md)

[快速开始](D:/Project/Codex/DataCollector/docs/quick-start-zh.md)
