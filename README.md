# dataCollector

用于 FANUC 机床单机数采验证的本地采集程序。

当前使用方式已经收敛成：

`在本机开发，在公司笔记本上只改配置、双击启动、连机测试。`

## 现在的推荐流程

1. 在你自己的电脑上开发代码
2. 把整个工程目录复制到公司笔记本
3. 在笔记本上只改 `config/machine.local.json`
4. 双击 `Start-GUI.bat`
5. 连接机床后看 GUI 和日志

## 复制到笔记本时必须一起带上的目录

1. `config/`
2. `scripts/`
3. `src/`
4. `vendor/`
5. `runtime/`

其中：

1. `vendor/` 里是当前默认使用的 `32 位 FOCAS DLL`
2. `runtime/` 里是工程内置的 `32 位 Python 3.11`

只要这两个目录也在，笔记本一般不需要再额外装 Python 和 DLL。

## 当前默认 DLL

当前默认读取：

`vendor/Fwlib32.dll`

也就是工程根目录下的：

`vendor/Fwlib32.dll`

并且我已经把相关依赖 DLL 一起整理进 `vendor/` 了。

## 当前默认启动方式

优先使用 GUI。

直接双击根目录：

`Start-GUI.bat`

它会调用：

`scripts/Start-FanucCollectorGui.ps1`

并优先使用工程自带的：

`runtime/python311-win32/python.exe`

## 配置文件怎么用

### 1. 测试时只改这个文件

笔记本测试时，优先使用：

`config/machine.local.json`

这个文件不会提交到 Git。

### 2. 第一次启动时会自动生成

如果 `config/machine.local.json` 不存在，启动脚本会自动从下面这个模板复制一份：

`config/machine.local.example.json`

### 3. 你真正要改的字段

只要重点改下面几个：

1. `machine.name`
   机床名称
2. `machine.ip`
   机床 IP
3. `machine.port`
   一般先用 `8193`
4. `machine.running_operation_modes`
   先保持 `[1, 2, 3]`
5. `storage.db_path`
   本地数据库文件路径
6. `runtime.log_path`
   本地日志文件路径

DLL 路径默认已经是：

`vendor/Fwlib32.dll`

一般不用改。

## 笔记本上的最简操作

### 第一步：复制整个工程目录

例如复制到：

`D:\dataCollector`

或者：

`C:\Users\你的用户名\Desktop\dataCollector`

都可以。

启动脚本现在已经改成相对路径，不依赖固定盘符。

### 第二步：修改本地配置

打开：

`config/machine.local.json`

如果没有这个文件，就先双击一次 `Start-GUI.bat`，脚本会自动生成。

### 第三步：确认机床网络

笔记本连到机床网络后，可以先测：

```powershell
Test-NetConnection 192.168.91.46 -Port 8193
```

或者运行工程自带脚本：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Test-MachineEndpoint.ps1 -IpAddress 192.168.91.46 -Ports 8193 -OutputPath .\machine-precheck.json
```

### 第四步：双击启动

双击：

`Start-GUI.bat`

### 第五步：在 GUI 里操作

1. 检查 IP、端口、DLL 路径
2. 点 `Save Config`
3. 点 `Start Collector`
4. 看日志窗口
5. 看最新值窗口

### 第六步：看输出文件

默认会生成：

1. `data/*.db`
2. `logs/*.log`

## 如果不用 GUI

也可以直接运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Start-FanucCollector.ps1
```

## 首次现场测试重点看什么

第一次不要只看“有没有数据”，要重点看：

1. `alarm_state` 是否跟报警一致
2. `emergency_state` 是否跟急停一致
3. `operation_mode` 在空闲、运行、暂停时分别是多少
4. `controller_mode_text` 是否和现场模式一致

`running_operation_modes` 现在先按 `[1, 2, 3]` 处理，只是第一版默认值。

等你测试完，把这 3 个状态告诉我：

1. 空闲时 `operation_mode`
2. 运行时 `operation_mode`
3. 暂停时 `operation_mode`

我再继续把“运行时间”的判断规则校准掉。

## 当前结论

你后面到笔记本上：

1. 不需要固定工程路径
2. 不需要再自己装 Python
3. 不需要再自己找 `fwlib64.dll`
4. 只要把整个工程目录复制过去
5. 修改 `config/machine.local.json`
6. 双击 `Start-GUI.bat`

就可以开始测。
