# 加载 Windows Forms 用于文件夹选择对话框
Add-Type -AssemblyName System.Windows.Forms

# 弹出文件夹选择对话框
$folderDialog = New-Object System.Windows.Forms.FolderBrowserDialog
$folderDialog.Description = "请选择你的项目文件夹"
$folderDialog.ShowNewFolderButton = $true
$result = $folderDialog.ShowDialog()

# 如果用户点了确定，就进入那个文件夹
if ($result -eq [System.Windows.Forms.DialogResult]::OK) {
    $selectedPath = $folderDialog.SelectedPath
    Set-Location -Path $selectedPath
    Write-Host "已进入文件夹: $selectedPath"
} else {
    Write-Host "未选择文件夹，脚本退出。"
    pause
    exit
}

# 设置 DeepSeek 环境变量
$env:ANTHROPIC_BASE_URL = "https://api.deepseek.com/anthropic"
$env:ANTHROPIC_AUTH_TOKEN = "sk-bac1e0905b2947458b5011ac827cd853"
$env:ANTHROPIC_MODEL = "deepseek-v4-pro"
claude

# 启动 Claude Code
Write-Host "正在启动 Claude Code (DeepSeek) ..."
claude