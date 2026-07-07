$ip = "91.107.251.112"
$port = 1530
$user = "root"
$pass = "135246Eac"

$secPass = ConvertTo-SecureString $pass -AsPlainText -Force
$cred = New-Object System.Management.Automation.PSCredential ($user, $secPass)

Write-Host "Uploading deploy.zip directly..."
Set-SCPItem -ComputerName $ip -Port $port -Credential $cred -Path "e:\Projects\Youtube Downloader\deploy.zip" -Destination "/root/" -AcceptKey

Write-Host "Connecting to SSH..."
$session = New-SSHSession -ComputerName $ip -Port $port -Credential $cred -AcceptKey
if (-not $session) {
    Write-Error "Failed to connect via SSH"
    exit
}

Write-Host "Extracting and stopping old process..."
Invoke-SSHCommand -SSHSession $session -Command "mkdir -p /root/ytdlhub && unzip -o /root/deploy.zip -d /root/ytdlhub"
Invoke-SSHCommand -SSHSession $session -Command "pkill -f 'YTDLHub.Web'"

Write-Host "Starting app..."
Invoke-SSHCommand -SSHSession $session -Command "chmod +x /root/ytdlhub/YTDLHub.Web"
Invoke-SSHCommand -SSHSession $session -Command "cd /root/ytdlhub && export ASPNETCORE_URLS=http://0.0.0.0:5211 && nohup ./YTDLHub.Web > /root/ytdlhub.log 2>&1 &"

Write-Host "App is starting! Check http://$ip:5211"
Remove-SSHSession -SSHSession $session
