/**
 * Reusable: configures the FiscalAPI SANDBOX PAC (+ sandbox identities + CSD) for the test company through the same endpoints the
 * "Integraciones fiscales" screen uses, then runs "Probar conexión". The test DB is reset every run, so call it in beforeAll.
 *
 * Secrets (never written to files, logs, titles or screenshots) are read at runtime from dotnet user-secrets of Millet.Api:
 *   E2E:FiscalApiSandboxKey (sk_test...), E2E:CsdDir (folder with the .cer/.key), E2E:CsdPassword.
 * The PUT goes through Node's fetch, NOT page.request, so the key/CSD never land in the Playwright trace or the harness ledger.
 */
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import type { Page } from '@playwright/test';
import { pickRenewedBearer } from '../../_harness/bearer';
import { apiBaseURL, env } from '../../_harness/session';
import type { Guard } from '../../_harness/fixtures';

const SANDBOX_URL = 'https://test.fiscalapi.com';
const PROVEEDOR_FISCALAPI = 1; // ProveedorPac.FiscalApi (same value the UI sends)
// Persona moral de prueba del SAT (mismo prellenado que ConfiguracionPacForm.tsx)
const IDENTIDAD_PRUEBA = { rfc: 'EKU9003173C9', razonSocial: 'ESCUELA KEMPER URGATE', regimenFiscal: '601', codigoPostal: '42501' };

function userSecrets(): Record<string, string> {
  const out = execFileSync('dotnet', ['user-secrets', 'list', '--project', 'backend/src/Api/Millet.Api.csproj'], { encoding: 'utf8' });
  const map: Record<string, string> = {};
  for (const line of out.split('\n')) {
    const i = line.indexOf(' = ');
    if (i > 0) map[line.slice(0, i).trim()] = line.slice(i + 3).trim();
  }
  return map;
}

export type PacSandboxResult = { empresaId: string; conexionExitosa: boolean; csdConfigurado: boolean; baseUrl: string };

export async function configurarPacSandbox(page: Page, guard: Guard): Promise<PacSandboxResult> {
  if (env !== 'test') throw new Error('[pac-sandbox] test env only');
  const s = userSecrets();
  const key = s['E2E:FiscalApiSandboxKey'], dir = s['E2E:CsdDir'], pass = s['E2E:CsdPassword'];
  if (!key || !dir || !pass) throw new Error('[pac-sandbox] missing E2E:FiscalApiSandboxKey / E2E:CsdDir / E2E:CsdPassword in dotnet user-secrets');
  if (!key.startsWith('sk_test')) throw new Error('[pac-sandbox] the key is not a sandbox key (sk_test...): refusing to continue');
  const files = fs.readdirSync(dir);
  const cer = files.find((f) => f.toLowerCase().endsWith('.cer')), llave = files.find((f) => f.toLowerCase().endsWith('.key'));
  if (!cer || !llave) throw new Error('[pac-sandbox] the CSD folder needs a .cer and a .key');

  // bearer: the one the app itself renewed in sessionStorage (never printed)
  // Same as guard.api: the Authorization header of a real request the app sends to OUR API (not a Graph token).
  let capturado: string | undefined;
  const escuchar = (r: import('@playwright/test').Request) => {
    if (r.url().startsWith(apiBaseURL)) r.headerValue('authorization').then((h) => { if (h) capturado = h; }).catch(() => {});
  };
  page.on('request', escuchar);
  // a light page that calls our API (the home page loads dashboard widgets the seed may not cover)
  await page.goto('/facturacion/repp/pendientes');
  await page.waitForLoadState('networkidle');
  page.off('request', escuchar);
  const entries = await page.evaluate(() => Object.keys(sessionStorage).map((k) => ({ key: k, value: sessionStorage.getItem(k) ?? '' })));
  const bearer = capturado ? (pickRenewedBearer(entries, capturado, Date.now() / 1000, 60) ?? capturado) : undefined;
  if (!bearer) throw new Error('[pac-sandbox] the app sent no API request with a session token');
  const me = await (await guard.api.get('/api/auth/me')).json();
  const empresaId: string = me.currentEmpresaId;

  const base = `${apiBaseURL}/api/v1/integraciones/fiscal/configuracion/${empresaId}/${PROVEEDOR_FISCALAPI}`;
  const headers = { authorization: bearer, 'content-type': 'application/json' };
  // If the config already exists (another spec in this run created it), update it with its version.
  const actual = await fetch(base, { headers });
  const version = actual.ok ? (await actual.json())?.version : undefined;
  const put = await fetch(base, {
    method: 'PUT',
    headers: { ...headers, 'Idempotency-Key': crypto.randomUUID(), ...(version != null ? { 'X-Expected-Version': String(version) } : {}) },
    body: JSON.stringify({
      baseUrl: SANDBOX_URL, apiKey: key, activo: true,
      emisorSandbox: IDENTIDAD_PRUEBA, receptorSandbox: IDENTIDAD_PRUEBA,
      csd: {
        certificadoBase64: fs.readFileSync(path.join(dir, cer)).toString('base64'),
        llavePrivadaBase64: fs.readFileSync(path.join(dir, llave)).toString('base64'),
        password: pass,
      },
    }),
  });
  if (!put.ok) throw new Error(`[pac-sandbox] PUT configuracion -> ${put.status} ${(await put.text()).replaceAll(key, '***').replaceAll(pass, '***').slice(0, 300)}`);
  const cfg = await put.json();
  if (!/^https:\/\/test\.fiscalapi\.com\/?$/.test(cfg.baseUrl)) throw new Error('[pac-sandbox] the saved BaseUrl is not the sandbox: refusing to continue');

  const test = await fetch(`${base}/test`, { method: 'POST', headers, body: '{}' });
  if (!test.ok) throw new Error(`[pac-sandbox] POST test -> ${test.status}`);
  const t = await test.json();
  if (!t.exitosa) throw new Error(`[pac-sandbox] Probar conexión failed: ${String(t.mensaje).replaceAll(key, '***').slice(0, 200)}`);
  const after = await (await guard.api.get(`/api/v1/integraciones/fiscal/configuracion/${empresaId}/${PROVEEDOR_FISCALAPI}`)).json();
  return { empresaId, conexionExitosa: true, csdConfigurado: !!after.csdConfigurado, baseUrl: after.baseUrl };
}
