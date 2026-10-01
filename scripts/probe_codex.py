"""Read-only capability probe. Never reads or prints login secrets."""
import json, subprocess, threading, queue, time, shutil

p = subprocess.Popen([shutil.which('codex'), 'app-server', '--listen', 'stdio://'], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, text=True, encoding='utf-8', creationflags=subprocess.CREATE_NO_WINDOW)
q = queue.Queue()
def reader():
    for line in p.stdout:
        try: q.put(json.loads(line))
        except ValueError: pass
threading.Thread(target=reader, daemon=True).start()
def send(obj):
    p.stdin.write(json.dumps(obj)+'\n'); p.stdin.flush()
def request(id, method, params):
    send(dict(id=id, method=method, params=params))
    end=time.monotonic()+30
    while time.monotonic()<end:
        obj=q.get(timeout=max(.1,end-time.monotonic()))
        if obj.get('id')==id: return obj
    raise TimeoutError(method)
try:
    initialized=request(1,'initialize',{'clientInfo':{'name':'pulse_sidebar','title':'Pulse Sidebar','version':'1.0.0'}})
    if 'error' in initialized:
        raise RuntimeError('Codex initialization failed; raw response omitted')
    send({'method':'initialized','params':{}})
    quota=request(2,'account/rateLimits/read',{})
    buckets=quota.get('result',{}).get('rateLimitsByLimitId') or {}
    print(json.dumps({'quota_read_ok':'error' not in quota,'quota_bucket_count':len(buckets)},ensure_ascii=False))
    usage=request(3,'account/usage/read',{})
    result=usage.get('result',{})
    print(json.dumps({'usage_read_ok':'error' not in usage,'has_summary':result.get('summary') is not None,'has_daily_buckets':result.get('dailyUsageBuckets') is not None},ensure_ascii=False))
finally:
    p.terminate()
    p.wait(timeout=10)
