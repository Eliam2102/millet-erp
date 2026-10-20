import { strict as assert } from 'node:assert';
import { test } from 'node:test';
import { ESLint } from 'eslint';

const eslint = new ESLint();
const lint = async (code, filePath = 'src/features/facturacion/F0Probe.tsx') =>
  (await eslint.lintText(code, { filePath }))[0].messages;

test('detecta paleta en literales y templates; hex y dark en className; texto interno', async () => {
  const messages = await lint(
    'export const Probe = () => <div className={`bg-blue-500 dark:text-[#abc]`}>El backend valida (D03)</div>;',
  );
  for (const rule of ['no-paleta-default', 'no-hex', 'no-dark', 'no-texto-interno']) {
    assert(messages.some((m) => m.ruleId === `ds/${rule}` && m.severity === 1));
  }
  const literals = await lint(
    'export const Probe = () => <div className="border-red-500">{"archivo.md"}</div>;',
  );
  assert(literals.some((m) => m.ruleId === 'ds/no-paleta-default'));
  assert(literals.some((m) => m.ruleId === 'ds/no-texto-interno'));
});
test('acepta tokens y limita hex/dark al className', async () => {
  const messages = await lint(
    'export const Probe = () => <div className="bg-surface-card text-ink">{"dark: text-[#fff]"}</div>;',
  );
  assert(!messages.some((m) => m.ruleId?.startsWith('ds/')));
});
test('respeta todas las exclusiones', async () => {
  for (const filePath of [
    'src/features/demo/Probe.test.tsx',
    'src/components/erp/reportes/exporters/Probe.tsx',
    'src/features/almacen/components/impresion/Probe.tsx',
    'src/components/auth/DevUserSelector.tsx',
  ]) {
    const messages = await lint(
      'export const Probe = () => <div className="bg-red-500">backend</div>;',
      filePath,
    );
    assert(!messages.some((m) => m.ruleId?.startsWith('ds/')), filePath);
  }
});
test('conserva restricciones de fechas UTC, centros de costo e idempotencia como error', async () => {
  const messages = await lint(
    'export const Probe = () => <div>{new Date().toISOString().slice(0, 10)}{"Dimensión 1"}{JSON.stringify({ idempotencyKey: `key-${1}` })}</div>;',
  );
  assert.equal(
    messages.filter((m) => m.ruleId === 'no-restricted-syntax' && m.severity === 2).length,
    3,
  );
});
