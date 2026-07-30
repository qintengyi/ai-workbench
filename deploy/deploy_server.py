"""
部署 AI Workbench 服务端到 192.168.1.8

用法：
    python deploy/deploy_server.py

流程：
1. SSH 连接 192.168.1.8:22 (root/CHANGEME)
2. 创建 /www/wwwroot/ai-workbench/server/
3. 上传 server/ 全部文件
4. 服务器创建 venv 并 pip install -r requirements.txt
5. 安装 systemd unit，enable + start
6. 打印 AGENT_TOKEN（首次启动生成）

依赖：paramiko（仅本机部署用，不入 server/requirements.txt）
"""

import os
import sys
import time

try:
    import paramiko
except ImportError:
    print("缺少 paramiko，请先执行: pip install paramiko")
    sys.exit(1)

SSH_HOST = "192.168.1.8"
SSH_PORT = 22
SSH_USER = "root"
SSH_PASS = "CHANGEME"
REMOTE_BASE = "/www/wwwroot/ai-workbench"
REMOTE_SERVER = f"{REMOTE_BASE}/server"

LOCAL_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
LOCAL_SERVER = os.path.join(LOCAL_ROOT, "server")
LOCAL_DEPLOY = os.path.join(LOCAL_ROOT, "deploy")


def ssh_exec(ssh, cmd, timeout=180):
    print(f"\n$ {cmd}")
    stdin, stdout, stderr = ssh.exec_command(cmd, timeout=timeout)
    out = stdout.read().decode("utf-8", errors="replace")
    err = stderr.read().decode("utf-8", errors="replace")
    rc = stdout.channel.recv_exit_status()
    if out.strip():
        print(out.rstrip())
    if err.strip():
        print(f"[stderr] {err.rstrip()}")
    print(f"[exit={rc}]")
    return rc, out, err


def sftp_mkdirs(sftp, remote_dir):
    parts = remote_dir.strip("/").split("/")
    cur = ""
    for p in parts:
        cur = f"{cur}/{p}"
        try:
            sftp.stat(cur)
        except FileNotFoundError:
            sftp.mkdir(cur)
            print(f"  mkdir {cur}")


def sftp_put_dir(sftp, local_dir, remote_dir, exclude=("__pycache__", ".pyc", "data.db", "config.json")):
    for item in os.listdir(local_dir):
        if any(ex in item for ex in exclude):
            continue
        local_path = os.path.join(local_dir, item)
        remote_path = f"{remote_dir}/{item}"
        if os.path.isdir(local_path):
            try:
                sftp.stat(remote_path)
            except FileNotFoundError:
                sftp.mkdir(remote_path)
            sftp_put_dir(sftp, local_path, remote_path, exclude)
        else:
            print(f"  upload {local_path} -> {remote_path}")
            sftp.put(local_path, remote_path)


def main():
    print(f"=== AI Workbench Server Deployment ===")
    print(f"Target: {SSH_USER}@{SSH_HOST}:{SSH_PORT} -> {REMOTE_BASE}")

    ssh = paramiko.SSHClient()
    ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    print(f"\nConnecting to {SSH_HOST}:{SSH_PORT}...")
    ssh.connect(SSH_HOST, port=SSH_PORT, username=SSH_USER, password=SSH_PASS, timeout=15)
    print("Connected.")

    # 1. 检查 python3
    rc, _, _ = ssh_exec(ssh, "python3 --version")
    if rc != 0:
        print("ERROR: python3 not found on server")
        sys.exit(1)

    # 2. 创建目录
    sftp = ssh.open_sftp()
    sftp_mkdirs(sftp, REMOTE_SERVER)

    # 3. 上传 server/ 文件
    print(f"\n=== Uploading server/ files ===")
    sftp_put_dir(sftp, LOCAL_SERVER, REMOTE_SERVER)

    # 4. 上传 systemd unit
    print(f"\n=== Uploading systemd unit ===")
    local_unit = os.path.join(LOCAL_DEPLOY, "ai-workbench.service")
    remote_unit = "/etc/systemd/system/ai-workbench.service"
    sftp.put(local_unit, remote_unit)
    sftp.chmod(remote_unit, 0o644)
    sftp.close()

    # 5. 创建 venv + 装依赖
    print(f"\n=== Setting up Python venv + dependencies ===")
    ssh_exec(ssh, f"python3 -m venv {REMOTE_BASE}/venv")
    ssh_exec(ssh, f"{REMOTE_BASE}/venv/bin/pip install --upgrade pip")
    ssh_exec(ssh, f"{REMOTE_BASE}/venv/bin/pip install -r {REMOTE_SERVER}/requirements.txt", timeout=240)

    # 6. 修正 systemd unit 里的 python 路径
    fix_cmd = (
        f"sed -i 's|ExecStart=/usr/bin/python3 {REMOTE_SERVER}/main.py|"
        f"ExecStart={REMOTE_BASE}/venv/bin/python {REMOTE_SERVER}/main.py|' "
        "/etc/systemd/system/ai-workbench.service"
    )
    ssh_exec(ssh, fix_cmd)

    # 7. 停旧 + reload + enable + start
    print(f"\n=== Installing + starting systemd service ===")
    ssh_exec(ssh, "systemctl daemon-reload")
    ssh_exec(ssh, "systemctl stop ai-workbench.service 2>/dev/null; true")
    ssh_exec(ssh, "systemctl enable ai-workbench.service")
    ssh_exec(ssh, "systemctl start ai-workbench.service")

    # 8. 检查状态
    time.sleep(3)
    print(f"\n=== Service status ===")
    ssh_exec(ssh, "systemctl is-active ai-workbench.service")
    ssh_exec(ssh, "systemctl status ai-workbench.service --no-pager -l | head -20")

    # 9. 读取 AGENT_TOKEN
    print(f"\n=== Reading AGENT_TOKEN ===")
    ssh_exec(ssh, f"cat {REMOTE_SERVER}/config.json 2>/dev/null || echo 'config.json not yet created'")

    # 10. 端口监听检查
    print(f"\n=== Port 10370 check ===")
    ssh_exec(ssh, "ss -tlnp | grep 10370 || echo 'port 10370 not listening yet'")

    ssh.close()
    print(f"\n=== Deployment complete ===")
    print(f"Server: http://{SSH_HOST}:10370")
    print(f"Login: admin / CHANGEME")
    print(f"Check AGENT_TOKEN above and fill it into Windows agent config.")


if __name__ == "__main__":
    main()
