# FANUC 单机采集快速开始

## 1. 先准备 DLL

把 `fwlib64.dll` 放到下面目录：

`D:\Project\Codex\DataCollector\vendor\fwlib64.dll`

如果你手里只有 `fwlib32.dll`，这版程序不能直接用，因为当前机器是 64 位 Python。

补充说明：

1. 我已经把 DLL 的目标目录准备好了。
2. 但我没法替你自动下载官方 DLL，因为供应方下载页面要求登录。
3. 所以这一步现在只能等你把合法取得的 `fwlib64.dll` 放进去。

## 2. 已经给你预填好的配置

配置文件已经按你当前机床 IP 预填好了：

`D:\Project\Codex\DataCollector\config\machine.192.168.91.46.json`

当前默认参数：

1. 机床 IP：`192.168.91.46`
2. 端口：`8193`
3. 轮询周期：`500ms`
4. 快照落盘周期：`5000ms`

## 3. 启动方式

### 方式 0：启动 GUI 图形界面

```powershell
powershell -ExecutionPolicy Bypass -File D:\Project\Codex\DataCollector\scripts\Start-FanucCollectorGui.ps1
```

### 方式 A：直接运行 PowerShell 脚本

```powershell
powershell -ExecutionPolicy Bypass -File D:\Project\Codex\DataCollector\scripts\Start-FanucCollector.ps1
```

### 方式 B：直接运行 Python

```powershell
python D:\Project\Codex\DataCollector\run_collector.py run --config D:\Project\Codex\DataCollector\config\machine.192.168.91.46.json
```

## 4. 运行后会生成什么

1. SQLite 数据库：
   `D:\Project\Codex\DataCollector\data\fanuc-poc-192.168.91.46.db`
2. 日志文件：
   `D:\Project\Codex\DataCollector\logs\fanuc-poc-192.168.91.46.log`

## 5. 查看最新值

```powershell
python D:\Project\Codex\DataCollector\run_collector.py show-latest --db D:\Project\Codex\DataCollector\data\fanuc-poc-192.168.91.46.db
```

## 6. 导出快照 CSV

```powershell
python D:\Project\Codex\DataCollector\run_collector.py export-snapshots --db D:\Project\Codex\DataCollector\data\fanuc-poc-192.168.91.46.db --out D:\Project\Codex\DataCollector\data\fanuc-poc-192.168.91.46.csv
```

## 7. 当前这版采集了什么

1. 系统信息
2. 自动模式号
3. 运行模式号
4. 急停状态
5. 报警状态
6. 控制器模式文本
7. OEE 状态文本
8. 派生的开机累计时长
9. 派生的运行累计时长

## 8. 当前最重要的现场说明

`running_operation_modes` 现在默认是 `[1, 2, 3]`，这是第一版经验值。

也就是说，程序当前会把 `operation_mode = 1/2/3` 视为“运行时间累计”。

你第一次现场测试时，要对照机床实际状态看一下：

1. 空闲时 `operation_mode` 是多少
2. 自动运行时 `operation_mode` 是多少
3. 暂停时 `operation_mode` 是多少

如果现场值和默认假设不一致，我下一版直接帮你把“运行时间”口径校准掉。
