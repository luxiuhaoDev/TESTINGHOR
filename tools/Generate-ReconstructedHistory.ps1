param(
    [Parameter(Mandatory = $true)]
    [string]$EvidenceRoot,
    [Parameter(Mandatory = $true)]
    [string]$RepositoryRoot,
    [string]$LegacyRoot = ''
)

$ErrorActionPreference = 'Stop'
$historyRoot = Join-Path $RepositoryRoot 'docs\history'
New-Item -ItemType Directory -Path $historyRoot -Force | Out-Null

$excluded = '\\bin\\|\\obj\\|\\artifacts\\|\\备份\\|\\源码与成品\\|\\.idea\\'
$sources = @(Get-ChildItem $EvidenceRoot -Recurse -File -Filter *.cs | Where-Object { $_.FullName -notmatch $excluded } | Sort-Object LastWriteTime, FullName)
$artifacts = @(Get-ChildItem (Join-Path $EvidenceRoot 'artifacts') -Directory | Sort-Object LastWriteTime, Name)
$backups = @(Get-ChildItem (Join-Path $EvidenceRoot '备份') -Directory | Sort-Object LastWriteTime, Name)
$legacySources = @()
$legacyPublishes = @()
if (-not [string]::IsNullOrWhiteSpace($LegacyRoot) -and (Test-Path $LegacyRoot)) {
    $legacySources = @(Get-ChildItem $LegacyRoot -Recurse -File -Filter *.cs | Where-Object {
        $_.FullName -notmatch '\\bin\\|\\obj\\|\\.idea\\|\\成品\\' -and $_.LastWriteTime -lt [datetime]'2026-11-03'
    } | Sort-Object LastWriteTime, FullName)
    $legacyPublishRoot = Join-Path $LegacyRoot 'obj\Release\net8.0-windows\win-x64'
    if (Test-Path $legacyPublishRoot) {
        $legacyPublishes = @(Get-ChildItem $legacyPublishRoot -File -Filter 'PublishOutputs*.txt' | Where-Object {
            $_.LastWriteTime -lt [datetime]'2026-11-03'
        } | Sort-Object LastWriteTime, Name)
    }
}

$descriptions = @{
    'publish-hud-20261107' = '建立 HUD 编辑与显示基础。'
    'publish-hud-style-20261107' = '完善 HUD 字体、颜色、背景和样式。'
    'publish-equipment-multitarget-20261107' = '增加装备显示与多目标表现。'
    'publish-target-layout-animation-20261107' = '增加目标卡片布局与进入动画。'
    'publish-nearby-range-20261107' = '增加附近目标范围配置。'
    'publish-esp-regression-fix-20261107' = '修复 ESP 回归问题。'
    'publish-esp-diagnostics-20261107' = '增加 ESP 诊断信息。'
    'publish-esp-candidates-20261107' = '增加实体候选诊断。'
    'publish-esp-class-diagnostics-20261107' = '增加 JVM 实体类诊断。'
    'publish-player-classification-fix-20261107' = '修复玩家实体分类。'
    'publish-player-cache-fix-20261107' = '修复玩家缓存更新。'
    'publish-player-diagnostic-20261107' = '增加玩家读取诊断。'
    'publish-hidden-filter-fix-20261107' = '修复隐藏名称过滤。'
    'publish-esp-final-20261107' = '形成当日 ESP 稳定整合版本。'
    'publish-startup-animation-20261108' = '加入启动动画。'
    'publish-focus-restore-20261108-003023' = '修复启动动画后的游戏焦点恢复。'
    'publish-chest-stealer-20261108-004640' = '加入箱子物品选择与自动操作基础。'
    'publish-chest-stealer-settings-20261108-005223' = '加入箱子管理设置。'
    'publish-chest-confirm-20261108-005942' = '加入操作后读取确认。'
    'publish-chest-two-pass-20261108-010113' = '加入两遍扫描流程。'
    'publish-chest-controls-20261108-010934' = '完善箱子管理控制项。'
    'publish-chest-auto-close-20261108-011326' = '加入自动关闭容器。'
    'publish-chest-dual-close-20261108-011435' = '修复/兼容双路径关闭。'
    'publish-inventory-v14' = '加入背包管理 v14。'
    'publish-inventory-v14-fix' = '修复背包管理 v14。'
    'publish-bed-esp-v15' = '加入床 ESP v15。'
    'publish-bed-esp-v15-final' = '完成床 ESP v15 稳定版。'
    'publish-bed-esp-hotkey' = '加入床 ESP 热键。'
    'publish-bed-custom-hotkey-v16' = '加入可自定义床 ESP 热键 v16。'
    'publish-unified-hotkeys-v17' = '统一模块热键系统 v17。'
    'publish-nearest-stable' = '形成邻近目标/实体读取稳定节点。'
    'publish-held-fireball-preview' = '加入手持火球轨迹预览。'
    'publish-world-fireball-trails' = '加入世界火球历史轨迹。'
    'publish-fireball-trajectory-toggle-owner' = '加入火球轨迹开关与所有者处理。'
    'publish-bow-prediction-toggle' = '加入弓箭预测开关。'
    'publish-lunar-stability-fix' = '修复 Lunar 环境下的稳定性问题。'
    'publish-autoaim-cadence-fix' = '修复自动瞄准更新节奏。'
    'publish-split-aim-quick-items' = '拆分瞄准与快捷物品处理。'
    'publish-lunar-fast-mouse-hotkeys' = '优化 Lunar 环境鼠标/热键响应。'
    'publish-no-stale-mouse-input' = '清理过期鼠标输入。'
    'publish-mouse-input-isolation' = '隔离不同输入通道状态。'
    'publish-lunar-fast-turn-v2' = '迭代快速转向 v2。'
    'publish-lunar-freeze-worker-main' = '尝试将冻结相关处理移入工作线程。'
    'publish-lunar-freeze-worker-clean' = '清理工作线程实现。'
    'publish-lunar-freeze-worker-final' = '形成工作线程最终候选。'
    'publish-rollback-lunar-freeze-worker' = '因稳定性原因回滚该工作线程方案。'
    'publish-pre-lunar-aim-with-quick-items' = '保留回滚前瞄准与快捷物品整合基线。'
    'publish-ui-motion' = '加入 UI 运动与切换动画。'
    'publish-ambient-effects' = '加入设置界面环境特效。'
    'test-ambient-layer' = '测试环境特效层级。'
    'test-ambient-zorder' = '测试环境特效 Z 顺序。'
    'publish-ambient-effects-fixed' = '修复环境特效合成。'
    'test-ambient-dedicated-layer' = '测试独立环境特效层。'
    'publish-ambient-visible' = '修复环境特效可见性。'
    'publish-ambient-card-clipped' = '处理卡片裁剪问题。'
    'publish-ambient-outside-ui' = '允许特效在设置卡片外绘制。'
    'publish-ambient-size' = '加入环境特效尺寸调整。'
    'bundle-integration-verify' = '验证整合包内容和运行。'
    'bundle-integration-stage' = '形成最终整合阶段包。'
}

$daySummary = @{
    '2026-11-01' = '现存最早工程目录和项目文件创建，宠物原始素材已存在。项目实际起源可能更早，但更早材料目前遗失。'
    '2026-11-02' = '形成早期“不注入游戏、直接只读内存”的完整链路：进程读取、HotSpot、ESP、Overlay、设置、输入和多次发布尝试。'
    '2026-11-03' = '整理并拆分早期源码，建立更清晰的 PE/内存/JVM 数据结构模型和瀑布窗口。'
    '2026-11-04' = '加入主题、BlockHit/击退控制和实体类型基础。'
    '2026-11-05' = '建立输入控制器、左右键逻辑、自动潜行和自动瞄准策略。'
    '2026-11-06' = '没有可核验的独立源码、发布目录或备份时间点；为保持逐日记录完整，本日不推测开发内容。'
    '2026-11-07' = '集中完成诊断日志、装备/HUD、目标布局与 ESP 分类、缓存和过滤修复。'
    '2026-11-08' = '高强度扩展启动动画、箱子/背包、床 ESP、火球/弓箭预测、统一热键和 Lunar 兼容稳定性。'
    '2026-11-09' = '完成 UI 动画、环境特效、三套 UI、自由工作台、磁吸缩放、正式发布与回滚备份。'
}

$versionLines = New-Object System.Collections.Generic.List[string]
$versionLines.Add('# 逐版本开发日志（证据重建）')
$versionLines.Add('')
$versionLines.Add('> 本记录由本地 `artifacts` 目录名称和最后修改时间重建，不是原始 Git 提交历史。编号 `Rxxx` 仅为本次整理生成的稳定索引。')
$versionLines.Add('')
$versionLines.Add('| 重建编号 | 时间 | 类型 | 证据目录 | 说明 |')
$versionLines.Add('| --- | --- | --- | --- | --- |')

$versionIndex = @{}
for ($i = 0; $i -lt $legacyPublishes.Count; $i++) {
    $publish = $legacyPublishes[$i]
    $id = 'P{0:D3}' -f ($i + 1)
    $versionLines.Add("| $id | $($publish.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss')) | 早期发布痕迹 | ``$($publish.Name)`` | MSBuild 生成的 PublishOutputs 清单；只能证明当时执行过发布流程，无法恢复对应版本说明。 |")
}
for ($i = 0; $i -lt $artifacts.Count; $i++) {
    $artifact = $artifacts[$i]
    $id = 'R{0:D3}' -f ($i + 1)
    $versionIndex[$artifact.FullName] = $id
    $kind = if ($artifact.Name.StartsWith('publish-')) { '发布' } elseif ($artifact.Name.StartsWith('test-')) { '测试' } else { '整合' }
    $description = $descriptions[$artifact.Name]
    if ([string]::IsNullOrWhiteSpace($description)) { $description = '由目录名称保留的阶段性构建节点。' }
    $versionLines.Add("| $id | $($artifact.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss')) | $kind | ``$($artifact.Name)`` | $description |")
}
$versionLines.Add('')
$versionLines.Add("共收录 **$($legacyPublishes.Count)** 个早期发布痕迹和 **$($artifacts.Count)** 个命名构建、测试或整合节点。")
$versionLines | Set-Content -Encoding utf8 (Join-Path $historyRoot 'VERSION_HISTORY.md')

$start = [datetime]'2026-11-01'
$end = [datetime]'2026-11-09'
$dailyLines = New-Object System.Collections.Generic.List[string]
$dailyLines.Add('# 逐日开发日志（证据重建）')
$dailyLines.Add('')
$dailyLines.Add('> 文件时间只能证明本地材料在相应时间存在或被修改，不能等价为公开提交时间。缺少证据的日期明确留空，不补写推测。')

for ($date = $start; $date -le $end; $date = $date.AddDays(1)) {
    $key = $date.ToString('yyyy-MM-dd')
    $dailyLines.Add('')
    $dailyLines.Add("## $key")
    $dailyLines.Add('')
    $dailyLines.Add($daySummary[$key])

    $daySources = if ($date -lt [datetime]'2026-11-03') {
        @($legacySources | Where-Object { $_.LastWriteTime.Date -eq $date.Date })
    } else {
        @($sources | Where-Object { $_.LastWriteTime.Date -eq $date.Date })
    }
    $dayArtifacts = @($artifacts | Where-Object { $_.LastWriteTime.Date -eq $date.Date })
    $dayLegacyPublishes = @($legacyPublishes | Where-Object { $_.LastWriteTime.Date -eq $date.Date })
    $dayBackups = @($backups | Where-Object { $_.LastWriteTime.Date -eq $date.Date })

    $dailyLines.Add('')
    $dailyLines.Add('**源码证据**')
    if ($daySources.Count -eq 0) {
        $dailyLines.Add('- 无独立 `.cs` 文件时间证据。')
    } else {
        foreach ($file in $daySources) {
            $baseRoot = if ($date -lt [datetime]'2026-11-03') { $LegacyRoot } else { $EvidenceRoot }
            $relative = $file.FullName.Substring($baseRoot.Length + 1).Replace('\', '/')
            $dailyLines.Add("- $($file.LastWriteTime.ToString('HH:mm:ss')) — ``$relative``")
        }
    }

    if ($key -eq '2026-11-01') {
        $dailyLines.Add('')
        $dailyLines.Add('**其他工程证据**')
        $dailyLines.Add('- 23:28 左右 — 遗留工程目录创建。')
        $dailyLines.Add('- 23:36:53 — `assets/pet-source.jpg` 的现存修改时间。')
        $dailyLines.Add('- 23:46:28 — 早期 `McPetOverlay.csproj` 与 `README.md` 创建时间。')
        $dailyLines.Add('- 现存最早工程已采用 .NET 8 WinForms、自包含 win-x64 与单文件发布目标。')
    }

    $dailyLines.Add('')
    $dailyLines.Add('**构建/测试节点**')
    if ($dayArtifacts.Count -eq 0 -and $dayLegacyPublishes.Count -eq 0) {
        $dailyLines.Add('- 无独立 `artifacts` 节点。')
    } else {
        for ($i = 0; $i -lt $dayLegacyPublishes.Count; $i++) {
            $publish = $dayLegacyPublishes[$i]
            $globalIndex = [array]::IndexOf($legacyPublishes, $publish) + 1
            $dailyLines.Add("- $($publish.LastWriteTime.ToString('HH:mm:ss')) — **P$('{0:D3}' -f $globalIndex)** ``$($publish.Name)``：早期 MSBuild 发布输出清单，具体功能说明已遗失。")
        }
        foreach ($artifact in $dayArtifacts) {
            $id = $versionIndex[$artifact.FullName]
            $description = $descriptions[$artifact.Name]
            if ([string]::IsNullOrWhiteSpace($description)) { $description = '阶段性构建节点。' }
            $dailyLines.Add("- $($artifact.LastWriteTime.ToString('HH:mm:ss')) — **$id** ``$($artifact.Name)``：$description")
        }
    }

    $dailyLines.Add('')
    $dailyLines.Add('**备份证据**')
    if ($dayBackups.Count -eq 0) {
        $dailyLines.Add('- 无独立备份目录。')
    } else {
        foreach ($backup in $dayBackups) {
            $dailyLines.Add("- $($backup.LastWriteTime.ToString('HH:mm:ss')) — ``$($backup.Name)``")
        }
    }
}
$dailyLines | Set-Content -Encoding utf8 (Join-Path $historyRoot 'DAILY_LOG.md')

$evidenceLines = New-Object System.Collections.Generic.List[string]
$evidenceLines.Add('# 原创性证据索引')
$evidenceLines.Add('')
$evidenceLines.Add("- 源码文件时间记录：**$($sources.Count)** 个 `.cs` 文件。")
$evidenceLines.Add("- 11 月 3 日以前的遗留源码时间记录：**$($legacySources.Count)** 个 `.cs` 文件。")
$evidenceLines.Add("- 11 月 2 日早期发布输出记录：**$($legacyPublishes.Count)** 个。")
$evidenceLines.Add("- 发布/测试/整合目录：**$($artifacts.Count)** 个。")
$evidenceLines.Add("- 阶段备份目录：**$($backups.Count)** 个。")
$evidenceLines.Add('- UI 与动画素材：见 `docs/images/` 和 `docs/media/`。')
$evidenceLines.Add('- 当前可构建源码：仓库根目录。')
$evidenceLines.Add('')
$evidenceLines.Add('## 最早现存工程证据')
$evidenceLines.Add('')
$evidenceLines.Add('- 2026-11-01 23:28 左右：遗留工程目录创建；')
$evidenceLines.Add('- 2026-11-01 23:36：`assets/pet-source.jpg` 修改时间；')
$evidenceLines.Add('- 2026-11-01 23:46：早期 `McPetOverlay.csproj` 与 `README.md` 创建；')
$evidenceLines.Add('- 2026-11-02：早期只读内存、HotSpot、ESP、Overlay、设置和输入源码；')
$evidenceLines.Add('- 2026-11-02 21:28–22:16：7 次 MSBuild 发布输出记录。')
$evidenceLines.Add('')
$evidenceLines.Add('这些是“现存最早证据”，不是对项目最初创建日期的断言。详见 `PREHISTORY.md`。')
$evidenceLines.Add('')
$evidenceLines.Add('## 备份时间点')
$evidenceLines.Add('')
foreach ($backup in $backups) {
    $evidenceLines.Add("- $($backup.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss')) — ``$($backup.Name)``")
}
$evidenceLines.Add('')
$evidenceLines.Add('## 证据限制')
$evidenceLines.Add('')
$evidenceLines.Add('- 本地时间戳可被复制工具保留或改变，因此应与源码内容、产物目录、截图和哈希组合判断。')
$evidenceLines.Add('- 重建记录不冒充原始 Git 提交、平台审核记录或封禁通知。')
$evidenceLines.Add('- 两个历史被封禁账号、准确日期和通知材料仍待原作者补充。')
$evidenceLines.Add('- 现存最早材料来自 2026-11-01，但原作者确认项目起源可能更早；早于该日期的开发材料目前未找到。')
$evidenceLines | Set-Content -Encoding utf8 (Join-Path $historyRoot 'EVIDENCE_INDEX.md')

Write-Output "Generated $($legacyPublishes.Count) legacy publish traces, $($artifacts.Count) named versions, $($sources.Count + $legacySources.Count) source records and $($backups.Count) backups."
