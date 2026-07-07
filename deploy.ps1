$ip = "91.107.251.112"
$port = 1530
$user = "root"
$pass = "135246Eac"

Write-Host "Building project for Linux..."
dotnet publish "e:\Projects\Youtube Downloader\src\YTDLHub.Web" -c Release -r linux-x64 --self-contained true -o "e:\Projects\Youtube Downloader\deploy"

Write-Host "Zipping..."
Compress-Archive -Path "e:\Projects\Youtube Downloader\deploy\*" -DestinationPath "e:\Projects\Youtube Downloader\deploy.zip" -Force

$secPass = ConvertTo-SecureString $pass -AsPlainText -Force
$cred = New-Object System.Management.Automation.PSCredential ($user, $secPass)

Write-Host "Connecting to SSH..."
$session = New-SSHSession -ComputerName $ip -Port $port -Credential $cred -AcceptKey
if (-not $session) {
    Write-Error "Failed to connect via SSH"
    exit
}

Write-Host "Installing dependencies on server..."
Invoke-SSHCommand -SSHSession $session -Command "apt-get update && apt-get install -y wget unzip python3-pip ffmpeg"
Invoke-SSHCommand -SSHSession $session -Command "wget https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp -O /usr/local/bin/yt-dlp && chmod a+rx /usr/local/bin/yt-dlp"

Write-Host "Uploading deploy.zip..."
Set-SCPItem -SSHSession $session -LocalFile "e:\Projects\Youtube Downloader\deploy.zip" -RemotePath "/root/"

Write-Host "Extracting and stopping old process..."
Invoke-SSHCommand -SSHSession $session -Command "mkdir -p /root/ytdlhub && unzip -o /root/deploy.zip -d /root/ytdlhub"
Invoke-SSHCommand -SSHSession $session -Command "pkill -f 'YTDLHub.Web'"

Write-Host "Starting app..."
Invoke-SSHCommand -SSHSession $session -Command "chmod +x /root/ytdlhub/YTDLHub.Web"
Invoke-SSHCommand -SSHSession $session -Command "cd /root/ytdlhub && export ASPNETCORE_URLS=http://0.0.0.0:5211 && nohup ./YTDLHub.Web > /root/ytdlhub.log 2>&1 &"

Write-Host "App is starting! Check http://$ip:5211"
Remove-SSHSession -SSHSession $session
