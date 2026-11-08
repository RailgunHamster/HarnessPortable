"""Drive the real Android app exclusively through its public native API and ADB session setup."""
import argparse, base64, hashlib, http.server, json, pathlib, re, socket, subprocess, threading, time, uuid

parser = argparse.ArgumentParser()
parser.add_argument('--adb', default='adb'); parser.add_argument('--serial', required=True)
parser.add_argument('--package', default='com.harness.portable.syncqa'); parser.add_argument('--output', required=True)
options = parser.parse_args()
assert options.serial.startswith('emulator-') and options.package.endswith('.syncqa'), 'Only an isolated emulator QA package is allowed'
out = pathlib.Path(options.output); out.mkdir(parents=True, exist_ok=True)
checks = []; seen_ids = set(); secret_values = ['qa-ssh-credential', 'qa-web-credential', 'qa-dav-credential', 'qa-direct-credential']
class Checks(list):
    def append(self, value):
        super().append(value); print('PASS: '+value, flush=True)
        (out/'progress.json').write_text(json.dumps(self,ensure_ascii=False,indent=2),encoding='utf-8')
checks = Checks()

def adb(*args):
    return subprocess.check_output([options.adb, '-s', options.serial, *args], text=True, stderr=subprocess.STDOUT, timeout=25)

class Cloud(http.server.BaseHTTPRequestHandler):
    body = None; revision = 0; requests = []; hold = False; entered = threading.Event(); release = threading.Event()
    page = b'<!doctype html><html><head><title>Harness API fixture</title></head><body><h1>Harness API loopback</h1><input id="entry"><input id="upload" type="file" multiple accept="text/plain"></body></html>'
    def log_message(self, *_): pass
    def reply(self, code, body=b''):
        self.send_response(code); self.send_header('Content-Length', str(len(body))); self.send_header('ETag', '"v%d"' % Cloud.revision); self.end_headers()
        try: self.wfile.write(body)
        except (BrokenPipeError, ConnectionResetError): pass
    def do_PROPFIND(self): self.reply(207)
    def do_GET(self):
        if self.path.startswith('/dav/'):
            Cloud.requests.append(('GET', None)); Cloud.entered.set()
            if Cloud.hold: Cloud.release.wait(15)
            self.reply(404 if Cloud.body is None else 200, Cloud.body or b'')
        else: self.reply(200, self.page)
    def do_PUT(self):
        body = self.rfile.read(int(self.headers['Content-Length']))
        condition = self.headers.get('If-None-Match') == '*' if Cloud.body is None else self.headers.get('If-Match') == '"v%d"' % Cloud.revision
        if not condition: self.reply(412); return
        Cloud.requests.append(('PUT', body)); Cloud.body = body; Cloud.revision += 1; self.reply(204)

server = http.server.ThreadingHTTPServer(('127.0.0.1', 0), Cloud)
threading.Thread(target=server.serve_forever, daemon=True).start(); http_port = server.server_port
token = uuid.uuid4().hex + uuid.uuid4().hex; session = uuid.uuid4().hex; host_port = None

def request(method, params=None, auth=None):
    message = dict(jsonrpc='2.0', id=uuid.uuid4().hex, auth=auth if auth is not None else token, method=method, params=params or {})
    with socket.create_connection(('127.0.0.1', host_port), timeout=10) as client:
        client.settimeout(20); stream=client.makefile('rwb'); stream.write((json.dumps(message, separators=(',', ':'))+'\n').encode()); stream.flush()
        reply=json.loads(stream.readline(2*1024*1024))
    return reply

def call(method, params=None, wait=True):
    reply=request(method, params); assert 'error' not in reply, (method, reply.get('error'))
    result=reply['result']
    if wait and isinstance(result, dict) and 'operationId' in result:
        ident=result['operationId']; end=time.monotonic()+30
        while time.monotonic()<end:
            job=request('operations.get', {'operationId':ident})['result']
            if job['state']=='returned': return job.get('result')
            assert job['state']!='failed', (method,job.get('error'))
            time.sleep(.04)
        raise AssertionError('Operation timed out: '+method)
    return result

def until(fn, timeout=15):
    end=time.monotonic()+timeout
    while time.monotonic()<end:
        value=fn()
        if value: return value
        time.sleep(.08)
    raise AssertionError('Expected application state was not reached')

def available(method):
    until(lambda:method in [value['name'] for value in call('api.describe')['methods']])

def find(identity, key=None):
    selector={'automationId':identity}
    if key is not None: selector['dataKey']=key
    return call('ui.find', {'selector':selector})

def act(identity, action='invoke', key=None, **values):
    until(lambda:find(identity,key)); target={'automationId':identity}
    if key is not None: target['dataKey']=key
    return call('ui.act',dict(target=target, action=action, **values))

def reveal(identity, container, direction=1):
    for _ in range(12):
        if find(identity): return
        act(container,'scroll',vertical=direction*380); time.sleep(.12)
    raise AssertionError('Could not reveal '+identity)

def inspect():
    tree=call('ui.tree'); serialized=json.dumps(tree,ensure_ascii=False)
    for secret in secret_values: assert secret not in serialized, 'Credential present in UI snapshot'
    missing=[dict(id=n['id'],type=n['type'],actions=n['actions']) for n in tree if n['actions'] and not n.get('automationId')]
    assert not missing, ('Interactive controls without stable IDs',missing)
    for node in tree:
        if node.get('automationId'): seen_ids.add(node['automationId'])
    return tree

try:
    adb('shell','pm','clear',options.package)
    adb('shell','pm','grant',options.package,'android.permission.POST_NOTIFICATIONS')
    adb('shell','am','start','--activity-single-top','-n',options.package+'/com.harness.portable.MainActivity')
    time.sleep(1)
    reply=adb('shell','am','broadcast','--receiver-foreground','-n',options.package+'/com.harness.portable.automation.AutomationReceiver','--es','command','start','--es','token',token,'--es','sessionId',session,'--ei','port','0')
    match=re.search(r'result=0, data="(\{[^\r\n]+\})"',reply); assert match, 'Protected ADB session failed to start'
    descriptor=json.loads(match.group(1)); host_port=int(adb('forward','tcp:0','tcp:'+str(descriptor['port'])).strip())
    adb('reverse','tcp:'+str(http_port),'tcp:'+str(http_port))
    until(lambda:'app.state' in [m['name'] for m in call('api.describe')['methods']])
    assert request('api.describe',auth='0'*64)['error']['code']=='unauthorized'
    assert call('network.resolve',{'host':'127.0.0.1'})['address']=='127.0.0.1'
    inspect(); checks.append('protected session, authentication, discovery, and native name-resolution API')

    call('library.create',{'kind':'ssh'}); until(lambda:find('TunnelEditor.Name'))
    act('TunnelEditor.Name','setValue',value='API fixture SSH'); act('TunnelEditor.Host','setValue',value='127.0.0.1')
    if until(lambda:find('TunnelEditor.Host.Menu')): inspect(); act('TunnelEditor.Host.Menu','dismiss')
    act('TunnelEditor.SshPort','setValue',value='9'); act('TunnelEditor.User','setValue',value='fixture')
    act('TunnelEditor.Password','setValue',value=secret_values[0]); act('TunnelEditor.PasswordVisibility'); inspect()
    reveal('TunnelEditor.Mode.manual','TunnelEditor.Content'); act('TunnelEditor.Mode.manual','selectItem')
    reveal('TunnelEditor.AuthInput','TunnelEditor.Content'); act('TunnelEditor.AuthInput','setValue',value=secret_values[1]); inspect()
    act('TunnelEditor.Save'); until(lambda:len(call('library.list'))==1)
    ssh=call('library.list')[0]; assert ssh['hasPassword'] and ssh['hasAuthInput']
    call('library.favorite',{'key':ssh['key'],'value':True}); inspect()
    checks.append('actual SSH editor, stable IDs, unmasked password redaction, token redaction, save, and favorite')

    call('library.delete',{'key':ssh['key']}); until(lambda:find('Library.DeleteCancel')); inspect()
    assert len(call('library.list'))==1; act('Library.DeleteCancel'); assert len(call('library.list'))==1
    call('library.open',{'key':ssh['key']})
    until(lambda:call('app.state')['tunnel']['status']=='failed')
    if find('Battery.Later'): act('Battery.Later')
    assert not find('Connecting.Password'), 'Network failure incorrectly offers password modification'
    inspect(); call('connections.stopAll'); checks.append('normal deletion confirmation and loopback connection failure without credential guessing')

    call('app.settings'); call('settings.tab',{'value':'general'}); until(lambda:find('Settings.Fullscreen'))
    act('Settings.Fullscreen','setChecked',value=True); act('Settings.Fullscreen','setChecked',value=False); inspect()
    call('settings.tab',{'value':'sync'}); until(lambda:find('ConfigurationSync.Address')); inspect()
    act('ConfigurationSync.Address','setValue',value=f'http://127.0.0.1:{http_port}/dav/Harness/')
    act('ConfigurationSync.Username','setValue',value='fixture'); act('ConfigurationSync.Password','setValue',value=secret_values[2])
    reveal('ConfigurationSync.AllowHttp','Settings.Content'); act('ConfigurationSync.AllowHttp','setChecked',value=True)
    reveal('ConfigurationSync.Save','Settings.Content'); act('ConfigurationSync.Save'); assert call('sync.status')['hasPassword']; inspect()
    call('sync.test'); call('sync.run'); assert call('sync.status')['phase']=='success'
    assert Cloud.body and b'If-Match' not in Cloud.body
    for secret in secret_values: assert secret.encode() not in Cloud.body
    before=call('library.list'); Cloud.body=b'<html>invalid document</html>'; call('sync.run')
    assert call('sync.status')['phase']=='failed' and call('library.list')==before
    Cloud.hold=True; Cloud.entered.clear(); pending=call('sync.run',wait=False)
    assert Cloud.entered.wait(5); call('sync.cancel'); until(lambda:not call('sync.status')['busy']); Cloud.hold=False; Cloud.release.set()
    assert call('sync.status')['phase']=='cancelled'; checks.append('real conditional WebDAV requests, write-only saved credentials, corrupt-cloud protection, and cancellation')

    call('settings.tab',{'value':'security'}); until(lambda:find('Settings.Export')); inspect(); act('Settings.Export')
    dialog=until(lambda:call('dialogs.get') if call('dialogs.get').get('kind')=='create' else None)
    call('dialogs.choose',{'id':dialog['id']}); files=until(lambda:[f for f in call('files.list') if f['size']>0])
    exported=base64.b64decode(call('files.get',{'fileId':files[-1]['fileId']})['base64'])
    assert b'API fixture SSH' in exported
    for secret in secret_values: assert secret.encode() not in exported
    checks.append('export through the normal dialog callback without automating Android system UI')

    call('app.connections'); call('library.create',{'kind':'direct'}); until(lambda:find('DirectEditor.Address'))
    url=f'http://127.0.0.1:{http_port}/fixture?token='+secret_values[3]
    act('DirectEditor.Address','setValue',value=url); until(lambda:find('DirectEditor.Address.Menu')); inspect(); act('DirectEditor.Address.Menu','dismiss')
    inspect(); act('DirectEditor.Save'); until(lambda:len(call('library.list'))==2)
    direct=next(v for v in call('library.list') if v.get('kind')=='direct'); assert 'token' not in direct['url']
    call('config.export'); export_dialog=until(lambda:call('dialogs.get') if call('dialogs.get').get('kind')=='create' else None)
    call('dialogs.choose',{'id':export_dialog['id']})
    until(lambda:len([f for f in call('files.list') if f['size']>0])==2)
    for exported_file in call('files.list'):
        exported_bytes=base64.b64decode(call('files.get',{'fileId':exported_file['fileId']})['base64'])
        for secret in secret_values: assert secret.encode() not in exported_bytes
    inspect(); call('library.open',{'key':direct['key']})
    available('browser.state'); until(lambda:call('browser.state')['ready'])
    until(lambda:call('browser.evaluate',{'script':"document.body ? document.body.innerText.includes('Harness API loopback') : false"}))
    assert 'token' not in call('browser.state')['url']; act('Browser.Toolbar.Collapse'); until(lambda:find('Browser.Toolbar.Expand')); act('Browser.Toolbar.Expand')
    call('browser.authenticate'); until(lambda:find('Browser.AuthInput')); act('Browser.AuthInput','setValue',value=secret_values[1]); inspect(); act('Browser.AuthCancel')
    call('browser.evaluate',{'script':"document.getElementById('entry').value='retained-page-state'; true"})
    act('Browser.Settings'); until(lambda:find('Settings.Back')); inspect(); act('Settings.Back')
    assert call('browser.evaluate',{'script':"document.getElementById('entry').value"})=='retained-page-state'
    upload=call('files.put',{'name':'fixture.txt','base64':base64.b64encode(b'loopback upload fixture').decode()})
    assert request('files.get',{'fileId':upload['fileId']})['error']['code']=='write_only'
    call('browser.evaluate',{'script':"document.getElementById('upload').click(); true"})
    chooser=until(lambda:call('dialogs.get') if call('dialogs.get').get('kind')=='open' else None)
    call('dialogs.choose',{'id':chooser['id'],'fileIds':[upload['fileId']]})
    until(lambda:call('browser.evaluate',{'script':"document.getElementById('upload').files.length"})==1)
    checks.append('actual embedded page bridge, retained browser state, toolbar operations, token redaction, and native upload picker callback')

    call('app.connections'); available('library.delete'); call('library.delete',{'key':direct['key']}); until(lambda:find('Library.DeleteConfirm')); act('Library.DeleteConfirm')
    until(lambda:len(call('library.list'))==1); call('app.settings'); call('settings.tab',{'value':'about'}); until(lambda:find('Updates.Check')); inspect()
    act('Updates.Source.GITHUB','selectItem'); act('Updates.Source.CNB','selectItem'); assert call('updates.status')['source']=='CNB'
    call('settings.tab',{'value':'general'}); act('Settings.Appearance.dark','selectItem'); inspect(); act('Settings.Appearance.light','selectItem')
    checks.append('confirmed deletion, update-source controls, category tabs, and appearance callbacks')
    events=call('events.read'); assert any(event['name']=='operation.returned' for event in events)
    result=dict(passed=True,checks=checks,visitedAutomationIds=sorted(seen_ids),devicePackage=options.package,coordinateInput=False)
    (out/'verification.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps(dict(passed=True,checks=len(checks),stableControls=len(seen_ids))))
finally:
    Cloud.release.set(); server.shutdown(); server.server_close()
    if host_port is not None:
        try: adb('shell','am','broadcast','--receiver-foreground','-n',options.package+'/com.harness.portable.automation.AutomationReceiver','--es','command','stop','--es','sessionId',session)
        finally: adb('forward','--remove','tcp:'+str(host_port))
    try: adb('reverse','--remove','tcp:'+str(http_port))
    except Exception: pass
