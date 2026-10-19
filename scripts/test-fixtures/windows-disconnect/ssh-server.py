import sys, pathlib, socket, threading, json, io, select, http.server
root = pathlib.Path(__file__).parent
import paramiko
from cryptography.hazmat.primitives.asymmetric import ed25519
from cryptography.hazmat.primitives import serialization
def ed_key():
    raw = ed25519.Ed25519PrivateKey.generate().private_bytes(serialization.Encoding.PEM, serialization.PrivateFormat.OpenSSH, serialization.NoEncryption())
    return paramiko.Ed25519Key.from_private_key(io.StringIO(raw.decode()))
keys = [paramiko.RSAKey.generate(2048), paramiko.ECDSAKey.generate(), ed_key()]
class Http(http.server.BaseHTTPRequestHandler):
    protocol_version = 'HTTP/1.1'
    def do_GET(self):
        if self.path.startswith('/stream'):
            import time
            self.send_response(200); self.send_header('Content-Type','text/event-stream'); self.send_header('Cache-Control','no-cache'); self.end_headers()
            try:
                while True:
                    self.wfile.write(b'data: heartbeat\n\n'); self.wfile.flush(); time.sleep(.2)
            except (OSError,ConnectionError): pass
            return
        body = b'<html><body style="font:24px sans-serif"><h1>Disconnect regression</h1><button onclick="this.textContent=Date.now()">Responsive page</button><div id="status"></div><script>for(let i=0;i<4;i++)new EventSource("/stream?"+i);setInterval(()=>fetch("/tick").catch(()=>{}),200);setInterval(()=>document.getElementById("status").textContent=Date.now(),200);</script></body></html>' if not self.path.startswith('/tick') else b'ok'
        self.send_response(200); self.send_header('Content-Type','text/html'); self.send_header('Content-Length',str(len(body))); self.end_headers(); self.wfile.write(body)
    def log_message(self, *_): pass
http = http.server.ThreadingHTTPServer(('127.0.0.1', 45891), Http)
threading.Thread(target=http.serve_forever, daemon=True).start()
class Server(paramiko.ServerInterface):
    def check_auth_password(self, username, password):
        return paramiko.AUTH_SUCCESSFUL if (username, password) == ('fixture', 'fixture-password') else paramiko.AUTH_FAILED
    def get_allowed_auths(self, username): return 'password'
    def check_channel_direct_tcpip_request(self, chanid, origin, destination):
        return paramiko.OPEN_SUCCEEDED if destination == ('127.0.0.1', 45891) else paramiko.OPEN_FAILED_ADMINISTRATIVELY_PROHIBITED
def relay(channel):
    try:
        with socket.create_connection(('127.0.0.1', 45891), timeout=5) as target:
            target.settimeout(None)
            while True:
                ready, _, _ = select.select([channel, target], [], [], 10)
                for src in ready:
                    data = src.recv(65536)
                    if not data: return
                    (target if src is channel else channel).sendall(data)
    finally: channel.close()
def serve(client, identities):
    transport = paramiko.Transport(client)
    try:
        for key in identities: transport.add_server_key(key)
        transport.start_server(server=Server())
        while transport.is_active():
            channel = transport.accept(1)
            if channel: threading.Thread(target=relay, args=(channel,), daemon=True).start()
    except (EOFError, OSError, paramiko.SSHException): pass
    finally: transport.close()
def listen(port, identities):
    listener = socket.socket(); listener.bind(('127.0.0.1', port)); listener.listen(8)
    while True:
        client, _ = listener.accept()
        threading.Thread(target=serve, args=(client, identities), daemon=True).start()
listen(22241, keys)
