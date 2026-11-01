import { test, expect } from '../../_harness/fixtures';
import { configurarPacSandbox } from '../_pac/pac-sandbox';

test.use({ role: 'super-admin' });
test.use({ viewport: { width: 1600, height: 900 } });

test('PAC-01 [SETUP] sandbox PAC + CSD configured through the API and "Probar conexión" is green', async ({ page, guard }) => {
  const r = await configurarPacSandbox(page, guard);
  expect(r.conexionExitosa).toBe(true);
  expect(r.csdConfigurado).toBe(true);
  expect(r.baseUrl).toMatch(/test\.fiscalapi\.com/);
});
