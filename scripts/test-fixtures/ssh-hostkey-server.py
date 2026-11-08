"""Loopback-only host-key migration, changed-key rejection and forwarding fixture."""
import http.server
import io
import select
import socket
import threading

import paramiko
from cryptography.hazmat.primitives.asymmetric import ed25519
from cryptography.hazmat.primitives import serialization


def keys():
    private = ed25519.Ed25519PrivateKey.generate().private_bytes(
        serialization.Encoding.PEM, serialization.PrivateFormat.OpenSSH, serialization.NoEncryption())
    return [paramiko.RSAKey.generate(2048), paramiko.ECDSAKey.generate(),
            paramiko.Ed25519Key.from_private_key(io.StringIO(private.decode()))]


class Page(http.server.BaseHTTPRequestHandler):
    def do_GET(self):
        body = b'ssh-forward-regression-ok'
        self.send_response(200); self.send_header('Content-Length', str(len(body))); self.end_headers(); self.wfile.write(body)
    def log_message(self, *_): pass


class Login(paramiko.ServerInterface):
    def get_allowed_auths(self, _): return 'password'
    def check_auth_password(self, username, password):
        return paramiko.AUTH_SUCCESSFUL if (username, password) == ('fixture', 'fixture-password') else paramiko.AUTH_FAILED
    def check_channel_direct_tcpip_request(self, chanid, origin, destination):
        return paramiko.OPEN_SUCCEEDED if destination == ('127.0.0.1', 45871) else paramiko.OPEN_FAILED_ADMINISTRATIVELY_PROHIBITED


def relay(channel):
    try:
        with socket.create_connection(('127.0.0.1', 45871), timeout=5) as target:
            target.settimeout(None)
            while True:
                ready, _, _ = select.select([channel, target], [], [], 10)
                for source in ready:
                    data = source.recv(65536)
                    if not data: return
                    (target if source is channel else channel).sendall(data)
    finally: channel.close()


def serve(client, identities):
    transport = paramiko.Transport(client)
    try:
        for key in identities: transport.add_server_key(key)
        transport.start_server(server=Login())
        while transport.is_active():
            channel = transport.accept(1)
            if channel: threading.Thread(target=relay, args=(channel,), daemon=True).start()
    except (EOFError, OSError, paramiko.SSHException): pass
    finally: transport.close()


if __name__ == '__main__':
    http = http.server.ThreadingHTTPServer(('127.0.0.1', 45871), Page)
    threading.Thread(target=http.serve_forever, daemon=True).start()
    listeners = []
    for port in (22221, 22222):
        listener = socket.socket(); listener.bind(('127.0.0.1', port)); listener.listen(8)
        listeners.append((listener, keys()))
    print('Host-key fixture ready', flush=True)
    while True:
        for listener in select.select([item[0] for item in listeners], [], [])[0]:
            identities = next(value for item, value in listeners if item is listener)
            client, _ = listener.accept()
            threading.Thread(target=serve, args=(client, identities), daemon=True).start()
