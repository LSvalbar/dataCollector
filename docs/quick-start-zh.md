# quick-start

本文件只保留最短操作路径。

完整说明以根目录 `README.md` 为准。

## 最短步骤

1. 把整个工程目录复制到公司笔记本
2. 确保这两个目录也一起带上：
   `vendor/`
   `runtime/`
3. 双击根目录：
   `Start-GUI.bat`
4. 如果 `config/machine.local.json` 不存在，脚本会自动从模板生成
5. 修改以下字段：
   `machine.name`
   `machine.ip`
   `machine.port`
   `machine.running_operation_modes`
6. 在 GUI 里点：
   `Save Config`
   `Start Collector`

## 关键文件

1. 本地配置模板：
   `config/machine.local.example.json`
2. 本地实际配置：
   `config/machine.local.json`
3. GUI 启动脚本：
   `scripts/Start-FanucCollectorGui.ps1`
4. 双击入口：
   `Start-GUI.bat`

## 当前默认 DLL

`vendor/Fwlib32.dll`

## 当前默认端口

`8193`
