"""Local SSH authentication fixture (Python + Paramiko).

Start this server, reverse tcp:22223 into an isolated Android emulator, then run
SshAuthenticationTests with the instrumentation argument ssh_auth_fixture=22223.
Only disposable test credentials are used. No user accounts are accessed.
"""
import json
import socket
import threading
import argparse
from pathlib import Path

import paramiko

key = paramiko.RSAKey.generate(2048)


class Server(paramiko.ServerInterface):
    def get_allowed_auths(self, username):
        return "publickey,password,keyboard-interactive"

    def check_auth_publickey(self, username, key):
        return paramiko.AUTH_SUCCESSFUL if username == "key" else paramiko.AUTH_FAILED

    def check_auth_password(self, username, password):
        correct = password == "fixture-password"
        print(json.dumps({"method": "password", "correct": correct}), flush=True)
        return paramiko.AUTH_SUCCESSFUL if username == "password" and correct else paramiko.AUTH_FAILED

    def check_auth_interactive(self, username, submethods):
        self.username = username
        label = "密码：" if username == "localized" else "One-time password:" if username == "otp" else "Password:"
        return paramiko.InteractiveQuery("Login", "", (label, False))

    def check_auth_interactive_response(self, responses):
        correct = responses == ["fixture-password"]
        print(json.dumps({"method": "keyboard-interactive", "correct": correct}), flush=True)
        return paramiko.AUTH_SUCCESSFUL if correct and self.username != "otp" else paramiko.AUTH_FAILED


def serve(client):
    transport = paramiko.Transport(client)
    try:
        transport.add_server_key(key)
        transport.start_server(server=Server())
        while transport.is_active():
            transport.accept(1)
    except (EOFError, OSError, paramiko.SSHException):
        pass
    finally:
        transport.close()


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--known-hosts", help="Optional known_hosts output for JVM probes")
    options = parser.parse_args()
    if options.known_hosts:
        Path(options.known_hosts).write_text("[127.0.0.1]:22223 " + key.get_name() + " " + key.get_base64() + "\n")
    with socket.socket() as listener:
        listener.bind(("127.0.0.1", 22223))
        listener.listen(8)
        print("Authentication fixture ready", flush=True)
        while True:
            client, _ = listener.accept()
            threading.Thread(target=serve, args=(client,), daemon=True).start()
