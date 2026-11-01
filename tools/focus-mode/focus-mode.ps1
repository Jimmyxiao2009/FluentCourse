<#
.SYNOPSIS
    专注模式 (Focus Mode) —— 定时关闭指定的游戏进程,帮助自己/课堂保持专注。

.DESCRIPTION
    这是一个【透明、可控、可随时停止】的小工具:
      - 每隔 N 分钟检查一次正在运行的进程;
      - 如果发现命中关闭名单里的游戏,先礼貌地请它关闭主窗口,
        若 5 秒内没退出再强制结束;
      - 所有动作都打印到屏幕并写入日志文件,你随时知道它做了什么;
      - 想停就按 Ctrl+C,或直接关掉这个窗口即可。它【不会】把自己藏起来,
        也【不会】把自己设成系统关键进程(那会导致蓝屏),你拥有完全控制权。

.NOTES
    用法:
        1) 直接前台运行(推荐,看得见它在干嘛):
             pwsh -ExecutionPolicy Bypass -File .\focus-mode.ps1
           或在 Windows PowerShell:
             powershell -ExecutionPolicy Bypass -File .\focus-mode.ps1

        2) 试运行(只报告、不真的关进程),先确认名单匹配对了:
             pwsh -File .\focus-mode.ps1 -DryRun

        3) 自定义间隔(分钟):
             pwsh -File .\focus-mode.ps1 -IntervalMinutes 5

    想开机自动运行?见文件末尾"注册为计划任务"的说明 —— 用一个名字清清楚楚、
    在任务计划程序里随时能看到和删除的计划任务,而不是任何隐藏/防删手段。
#>

param(
    [int]    $IntervalMinutes = 5,
    [switch] $DryRun
)

# ── 关闭名单 ────────────────────────────────────────────────────────────────
# 这里填【进程名】(不含 .exe)。匹配不区分大小写,按"包含"匹配,方便覆盖变体。
# 注意:这几个名字按常见情况预填,但你机器上的实际进程名可能不同 ——
#       请先用 -DryRun 跑一遍,看日志里"正在运行的进程"确认匹配对了再正式用。
$Targets = @(
    # Minecraft:
    #   - 基岩版(微软商店)进程名通常是 Minecraft.Windows
    #   - Java 版跑在 javaw 里,但 javaw 也会命中"其他所有 Java 程序",
    #     默认【不】加 javaw,以免误杀;确实需要再自行解开下面那行注释。
    'Minecraft.Windows',
    'Minecraft',
    # 'javaw',            # ← Java 版 MC,谨慎启用(可能误杀别的 Java 程序)

    # CS 1.6(起源引擎经典版,主程序一般是 hl.exe):
    'hl',

    # 死神vs火影 / 火影忍者格斗(进程名因版本而异,先放几个常见猜测,
    # 请用 -DryRun 核对后改成你机器上真实的名字):
    '死神vs火影',
    'BvN',
    'Naruto'
)

# ── 日志 ────────────────────────────────────────────────────────────────────
$LogFile = Join-Path $PSScriptRoot 'focus-mode.log'

function Write-Log {
    param([string] $Message, [string] $Level = 'INFO')
    # 不用 Date.now 风格的隐式时间也能拿到时间戳:
    $ts   = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
    $line = "[$ts] [$Level] $Message"
    Write-Host $line
    Add-Content -Path $LogFile -Value $line -Encoding UTF8
}

# ── 主循环 ──────────────────────────────────────────────────────────────────
Write-Log "专注模式启动。间隔 = $IntervalMinutes 分钟。试运行 = $DryRun。"
Write-Log "关闭名单: $($Targets -join ', ')"
Write-Log "随时按 Ctrl+C 或关闭本窗口即可停止。"

try {
    while ($true) {
        $running = Get-Process -ErrorAction SilentlyContinue

        foreach ($name in $Targets) {
            $hits = $running | Where-Object { $_.ProcessName -like "*$name*" }

            foreach ($p in $hits) {
                if ($DryRun) {
                    Write-Log "命中 [$($p.ProcessName)] PID=$($p.Id) —— 试运行模式,未实际关闭。" 'WOULD-KILL'
                    continue
                }

                Write-Log "命中 [$($p.ProcessName)] PID=$($p.Id),尝试关闭…" 'ACTION'

                # 先礼貌地请它关主窗口(给它机会保存)
                try { $p.CloseMainWindow() | Out-Null } catch {}

                # 等最多 5 秒看它是否自己退出
                $exited = $p.WaitForExit(5000)
                if (-not $exited) {
                    try {
                        Stop-Process -Id $p.Id -Force -ErrorAction Stop
                        Write-Log "已强制结束 [$($p.ProcessName)] PID=$($p.Id)。" 'ACTION'
                    } catch {
                        Write-Log "无法结束 [$($p.ProcessName)] PID=$($p.Id):$($_.Exception.Message)" 'WARN'
                    }
                } else {
                    Write-Log "[$($p.ProcessName)] PID=$($p.Id) 已自行退出。" 'ACTION'
                }
            }
        }

        Start-Sleep -Seconds ($IntervalMinutes * 60)
    }
}
finally {
    Write-Log "专注模式已停止。" 'INFO'
}

# ── 开机自启(可选,完全可见可删)────────────────────────────────────────────
# 在【管理员】PowerShell 里运行下面这段,把它注册成一个名字明确的计划任务,
# 登录时自动启动。要取消随时可删,不存在任何隐藏:
#
#   $action  = New-ScheduledTaskAction -Execute 'pwsh.exe' `
#       -Argument "-ExecutionPolicy Bypass -File `"$PSScriptRoot\focus-mode.ps1`""
#   $trigger = New-ScheduledTaskTrigger -AtLogOn
#   Register-ScheduledTask -TaskName 'FocusMode-GameBlocker' `
#       -Action $action -Trigger $trigger -Description '专注模式:定时关闭指定游戏'
#
# 删除它:
#   Unregister-ScheduledTask -TaskName 'FocusMode-GameBlocker' -Confirm:$false
