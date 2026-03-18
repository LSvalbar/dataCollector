# dataCollector

用于 FANUC 机床单机数采验证的本地采集程序。

当前交付分成两条路线：

1. 源码开发路线：在开发电脑改代码、调试、打包。
2. 发布包测试路线：把打包后的目录拷到任意 Windows 电脑，直接双击 `dataCollector.exe`。

## 当前推荐

如果你的目标是去公司笔记本连机床测试，不要再运行源码里的 `bat` 或 `ps1`。

优先使用打包后的发布目录：

`dist\dataCollector`

把整个目录复制到笔记本或 U 盘里都可以，然后直接执行：

`dist\dataCollector\dataCollector.exe`

注意：

不是只拷贝一个 exe 文件，而是拷贝整个 `dataCollector` 目录。

## 一次打包

在开发电脑根目录执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Build-PortableExe.ps1
```

打包成功后，输出目录为：

`dist\dataCollector`

目录里会包含：

1. `dataCollector.exe`
2. Python 运行时依赖
3. GUI 运行依赖
4. `vendor\` 下的 FANUC FOCAS DLL
5. `config\machine.local.example.json`

## 发布包怎么用

### 1. 复制整个目录

把下面整个目录复制到目标电脑：

`dist\dataCollector`

目标电脑上的路径可以随意，例如：

`D:\dataCollector`

或：

`C:\Users\你的用户名\Desktop\dataCollector`

都可以。

### 2. 首次启动

双击：

`dataCollector.exe`

程序首次启动时会自动准备这些目录：

1. `config\`
2. `data\`
3. `logs\`
4. `vendor\`

如果 `config\machine.local.json` 不存在，会自动从模板复制一份。

说明：

首次启动会自动展开随包携带的 DLL 和模板文件，可能比后续启动稍慢，这是正常现象。

### 3. 你真正要改的配置文件

目标电脑上只改这个文件：

`config\machine.local.json`

重点字段如下：

1. `machine.name`
   机床名称
2. `machine.ip`
   机床 IP
3. `machine.port`
   一般先用 `8193`
4. `machine.running_operation_modes`
   第一版先保留 `[1, 2, 3]`
5. `storage.db_path`
   本地数据库文件
6. `runtime.log_path`
   本地日志文件

默认 DLL 路径已经是：

`vendor/Fwlib32.dll`

一般不需要改。

### 4. 现场测试顺序

1. 笔记本网线直连或接入机床网络。
2. 确认机床 IP 可达。
3. 双击 `dataCollector.exe`。
4. 在 GUI 中检查配置。
5. 点击 `Save Config`。
6. 点击 `Start Collector`。
7. 观察右侧日志和左侧最新值。

## 网络预检

在笔记本上可以先执行：

```powershell
Test-NetConnection 192.168.91.46 -Port 8193
```

或者：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Test-MachineEndpoint.ps1 -IpAddress 192.168.91.46 -Ports 8193 -OutputPath .\machine-precheck.json
```

## 输出文件

程序运行后默认会写入：

1. `data\*.db`
2. `logs\*.log`

这些都在 EXE 所在目录下，不依赖固定盘符。

## 源码模式

如果你还在开发电脑上直接跑源码，仍然可以使用：

`Start-GUI.bat`

它会调用：

`scripts\Start-FanucCollectorGui.ps1`

源码模式主要用于开发，不建议作为现场交付方式。

## 当前已解决的环境问题

这套发布目录已经把以下问题收敛到打包阶段处理：

1. 不依赖目标电脑预装 Python
2. 不依赖目标电脑单独配置项目路径
3. 不依赖手动拷贝 FOCAS 主 DLL
4. 相对路径统一以 EXE 所在目录为基准

## 仍然需要现场满足的条件

以下不是打包问题，现场仍然必须满足：

1. 目标电脑必须是 Windows
2. 目标电脑与机床网络可达
3. 机床端 FOCAS over Ethernet 必须可用
4. 机床端口必须正确，一般为 `8193`

## 首次现场校准重点

第一次不要只看“有没有数据”，还要重点记录：

1. 空闲时 `operation_mode`
2. 运行时 `operation_mode`
3. 暂停时 `operation_mode`
4. 报警时 `alarm_state`
5. 急停时 `emergency_state`

这些值会直接影响后面“运行时间”和状态判定规则。
