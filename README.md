# dataCollector

用于 FANUC 机床单机数采验证的本地采集程序。

当前交付分成两条路线：

1. 源码开发：在开发电脑改代码、调试、打包。
2. 发布包测试：把打包后的目录复制到任意 Windows 电脑，直接双击 `dataCollector.exe`。

## 当前推荐

如果你的目标是去公司笔记本连机床测试，优先使用打包后的发布目录：

`dist\dataCollector`

复制整个目录到目标电脑后，直接运行：

`dist\dataCollector\dataCollector.exe`

注意：

不是只复制一个 exe 文件，而是复制整个 `dataCollector` 目录。

## 一次打包

在开发电脑根目录执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Build-PortableExe.ps1
```

打包成功后输出目录为：

`dist\dataCollector`

目录中会包含：

1. `dataCollector.exe`
2. Python 运行时依赖
3. GUI 运行依赖
4. `vendor\` 下的 FANUC FOCAS DLL
5. `config\machine.local.example.json`

## 发布包怎么用

### 1. 复制整个目录

把下面整个目录复制到目标电脑：

`dist\dataCollector`

目标电脑上的路径可以任意，例如：

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
5. 点击“保存配置”。
6. 点击“启动采集”。
7. 观察“最新采集值”和“日志尾部”。
8. 切到“时间统计”页，按日期查看机床当天每个时间段的状态。

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

## 当前已采集的主要数据

当前版本已经采集并落库这些数据：

1. 机床基础信息：系列、版本、轴数等
2. 在线状态：`machine_online`
3. 自动模式：`automatic_mode`
4. 运行模式：`operation_mode`
5. 报警状态：`alarm_state`
6. 急停状态：`emergency_state`
7. 控制器模式：`controller_mode_text`
8. OEE 状态：`oee_status_text`
9. 当日开机累计时间：`today_power_on_ms`
10. 当日加工累计时间：`today_processing_ms`
11. 观测开机时间：`observed_power_on_at`
12. 观测关机时间：`observed_power_off_at`

说明：

1. `today_processing_ms` 当前第一版是按 `operation_mode` 是否属于 `running_operation_modes` 推导的加工累计时间。
2. `observed_power_on_at` 和 `observed_power_off_at` 是采集程序观测到的在线/离线切换时间，不等同于 CNC 内部原始断电时间。

## 时间统计页

GUI 已新增“时间统计”页，可以按天查看机床时间轴。表格列为：

1. 状态
2. 时长
3. 开始时间
4. 截止时间

当前状态分类规则：

1. `加工`：机床在线，且 `operation_mode` 命中 `running_operation_modes`
2. `关机`：机床离线
3. `报警`：机床在线且报警中
4. `急停`：机床在线且急停中
5. `待机`：除以上以外的在线状态

这页的目的就是让你按时间段看出机床每天都干了什么。

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
3. 不依赖手动复制 FOCAS 主 DLL
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
## 常见连接错误排查

如果日志里出现：

`function=cnc_allclibhndl3 | error=EW_NODLL(-15): FOCAS 运行时缺少配套 DLL`

这通常不表示 `Fwlib32.dll` 主文件本身加载失败。更准确地说：

1. 主 DLL 已经被 Python 成功加载
2. FOCAS 在真正建立连接时，发现配套 DLL 缺失、无法加载，或者当前目录/搜索路径不对

当前版本的日志会额外输出这些诊断信息：

1. `dll_path`
2. `python`
3. `python_bits`
4. `cwd`
5. `target`
6. `tcp_probe`
7. `dependency_check`

其中：

1. `tcp_probe=ok`
   表示机床 IP 和端口至少可以建立 TCP 连接
2. `dependency_check`
   会检查这些常见 DLL 是否存在且可加载：
   `Fwlib32.dll`
   `fwlibe1.dll`
   `Fwlib0i.dll`
   `Fwlib0iB.dll`
   `fwlib0iD.dll`
   `fwlib0DN.dll`
   `fwlib30i.dll`

如果看到 `missing` 或 `load_failed(...)`，优先处理 DLL 目录问题；如果 `tcp_probe=failed(...)`，优先处理网络和端口问题。

## 当前界面显示的核心指标

最新版本的“采集控制”页会直接显示两组信息：

1. 当前状态
   机床名称、IP、端口、在线状态、当前机床状态、控制器模式、自动模式、运行模式、报警状态、急停状态、最近开机时间、最近关机时间
2. 当日统计
   当日开机累计时长、当日加工累计时长、当日待机累计时长、当日报警累计时长、当日急停累计时长、当日利用率

说明：

1. `当日加工累计时长`
   当前版本按照 `operation_mode` 是否命中 `running_operation_modes` 来累计
2. `当日待机累计时长`
   当前版本按“在线、非加工、非报警、非急停”来累计
3. `当日利用率`
   当前版本公式为：
   `当日加工累计时长 / 当日开机累计时长 * 100%`

## 实时刷新说明

当前版本已经做了两层优化：

1. 状态变化时立即落库，不再等大批量写入满了才刷
2. GUI 刷新频率已经提高，用于现场监控机床状态变化

因此像急停、报警、加工转待机这类状态切换，理论上会在很短时间内反映到界面，不需要等点击“停止采集”后才看到。
