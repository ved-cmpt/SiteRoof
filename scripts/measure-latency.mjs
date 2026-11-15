// Measures request latency of a running SiteRoof instance from this machine.
// Usage: node scripts/measure-latency.mjs [baseUrl] [samples]
// Example: node scripts/measure-latency.mjs http://127.0.0.1:8080 30
// Results depend on the machine and database; report them with the environment they were measured in.

const base = process.argv[2] ?? 'http://127.0.0.1:8080';
const samples = Number(process.argv[3] ?? 30);
const jar = new Map();

function cookieHeader() {
  return [...jar].map(([k, v]) => `${k}=${v}`).join('; ');
}

async function call(method, path, body) {
  const headers = { cookie: cookieHeader() };
  if (method !== 'GET') {
    headers['content-type'] = 'application/json';
    headers['x-xsrf-token'] = decodeURIComponent(jar.get('XSRF-TOKEN') ?? '');
  }
  const started = performance.now();
  const res = await fetch(base + path, { method, headers, body: body ? JSON.stringify(body) : undefined });
  const text = await res.text();
  const ms = performance.now() - started;
  for (const c of res.headers.getSetCookie()) {
    const [pair] = c.split(';');
    const i = pair.indexOf('=');
    jar.set(pair.slice(0, i), pair.slice(i + 1));
  }
  if (!res.ok) throw new Error(`${method} ${path} -> ${res.status}: ${text.slice(0, 200)}`);
  return { ms, json: text ? JSON.parse(text) : null };
}

function stats(name, values) {
  const v = [...values].sort((a, b) => a - b);
  const pick = (q) => v[Math.min(v.length - 1, Math.ceil(q * v.length) - 1)];
  console.log(`${name.padEnd(40)} n=${v.length}  median=${pick(0.5).toFixed(0)} ms  p95=${pick(0.95).toFixed(0)} ms  max=${v.at(-1).toFixed(0)} ms`);
}

await call('GET', '/api/auth/me');
const provision = [];
for (let i = 0; i < 3; i++) {
  jar.delete('siteroof.auth');
  await call('GET', '/api/auth/me');
  provision.push((await call('POST', '/api/demo/start', { persona: 'manager' })).ms);
}
const projects = (await call('GET', '/api/projects')).json;
const rbl = projects.find((p) => p.code === 'RBL').id;

const endpoints = [
  ['GET /api/overview', '/api/overview'],
  ['GET /api/projects/{id}/dashboard', `/api/projects/${rbl}/dashboard`],
  ['GET /api/projects/{id}/work-orders', `/api/projects/${rbl}/work-orders?pageSize=100`],
  ['GET /api/projects/{id}/blocker-summary', `/api/projects/${rbl}/blocker-summary`],
];
console.log(`Base URL: ${base}`);
stats('POST /api/demo/start (provision + seed)', provision);
for (const [name, path] of endpoints) {
  await call('GET', path); // warm-up
  const times = [];
  for (let i = 0; i < samples; i++) times.push((await call('GET', path)).ms);
  stats(name, times);
}
