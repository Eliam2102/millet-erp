#!/usr/bin/env bash
# DEMO local: requiere API y token con activos_fijos.depreciacion.correr.
# No capitaliza ni configura cuentas: usa el montacargas DEMO ya capitalizado.
set -euo pipefail
python3 - <<'PY'
import json, os, urllib.request, urllib.parse, uuid
base=os.environ.get('MILLET_API_URL','http://localhost:5000').rstrip('/')
if urllib.parse.urlparse(base).hostname not in ('localhost','127.0.0.1','::1'):
    raise SystemExit('Esta demostración sólo admite un API local.')
token=os.environ.get('MILLET_API_TOKEN')
if not token: raise SystemExit('Defina MILLET_API_TOKEN en el entorno, sin guardarlo en el repositorio.')
def request(path, body=None):
    headers={'Authorization':'Bearer '+token}
    if body is not None: headers.update({'Content-Type':'application/json','Idempotency-Key':str(uuid.uuid4())})
    req=urllib.request.Request(base+'/api/v1/activos-fijos/'+path,data=None if body is None else json.dumps(body).encode(),headers=headers)
    with urllib.request.urlopen(req) as r: return json.load(r)
filas=request('depreciacion/previa?periodo=2026-02-01')
montacargas=next((f for f in filas if f['numero']=='DEMO-AF-MAQ'),None)
if montacargas is None: raise SystemExit('Capitalice primero el montacargas DEMO con cuentas, centro y periodo abiertos.')
if montacargas['importe'] != 4166.67: raise SystemExit('La cuota del montacargas no coincide con $4,166.67; revise base y tasa antes de confirmar.')
corrida=request('depreciacion',{'periodo':'2026-02-01'})
reintento=request('depreciacion',{'periodo':'2026-02-01'})
assert corrida['id']==reintento['id'] and corrida['polizaId']==reintento['polizaId']
print('DEMO febrero: cuota $4,166.67; corrida y póliza recuperadas sin duplicar en el reintento.')
print('Corrida:',corrida['id'],'Póliza:',corrida['polizaId'])
PY
