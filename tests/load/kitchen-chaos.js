// Pruebas de carga de Kitchen Chaos con k6.
//
// Cada iteración simula una sala de dos jugadores hablando con el GameHub por SignalR
// (WebSocket + protocolo JSON): crean y se unen a una sala, arrancan el nivel 1, los dos
// intentan tomar la MISMA estación al mismo tiempo, se mueven un rato y se desconectan.
// Esa toma simultánea es la prueba de concurrencia: tiene que ganar exactamente uno.
//
// Escenario con la variable SCENARIO: humo | carga | estres | pico | duracion
// Ver tests/load/README.md para correrlo.

import http from 'k6/http';
import { WebSocket } from 'k6/experimental/websockets';
import { setTimeout, clearTimeout } from 'k6/timers';
import { check } from 'k6';
import { Trend, Rate, Counter } from 'k6/metrics';

const BASE_URL = __ENV.BASE_URL || 'http://host.docker.internal:5129';
const SCENARIO = __ENV.SCENARIO || 'humo';
const DURACION = __ENV.DURACION || '10m';

const RS = '\x1e'; // separador de mensajes de SignalR

// Métricas propias (aparecen en el resumen de k6)
const invocationMs = new Trend('kc_invocation_ms', true);
const invocationOk = new Rate('kc_invocation_ok');
const oneWinner = new Rate('kc_one_winner');
const sessionOk = new Rate('kc_session_ok');
const sessions = new Counter('kc_sessions');

const scenarios = {
  // Una sola sala para comprobar que el script funciona
  humo: {
    executor: 'per-vu-iterations', vus: 1, iterations: 1, maxDuration: '1m',
  },
  // Carga normal: 2 salas nuevas por segundo durante 1 minuto
  carga: {
    executor: 'constant-arrival-rate', rate: 2, timeUnit: '1s', duration: '1m',
    preAllocatedVUs: 20, maxVUs: 60,
  },
  // Estrés: sube la llegada de salas (hasta 12 por segundo) buscando el punto donde se degrada
  estres: {
    executor: 'ramping-arrival-rate', startRate: 2, timeUnit: '1s',
    preAllocatedVUs: 40, maxVUs: 400,
    stages: [
      { target: 4, duration: '30s' },
      { target: 8, duration: '30s' },
      { target: 12, duration: '30s' },
      { target: 12, duration: '30s' },
    ],
  },
  // Pico: tráfico bajo, subida brusca de 10 segundos y vuelta a lo normal
  pico: {
    executor: 'ramping-arrival-rate', startRate: 1, timeUnit: '1s',
    preAllocatedVUs: 20, maxVUs: 300,
    stages: [
      { target: 1, duration: '20s' },
      { target: 25, duration: '5s' },
      { target: 25, duration: '10s' },
      { target: 1, duration: '5s' },
      { target: 1, duration: '30s' },
    ],
  },
  // Duración: carga moderada y constante (por defecto 10 min, cambia con DURACION=30m)
  duracion: {
    executor: 'constant-arrival-rate', rate: 2, timeUnit: '1s', duration: DURACION,
    preAllocatedVUs: 20, maxVUs: 60,
  },
};

// Umbrales: los mismos del laboratorio (p95 < 500 ms, errores < 1 %) más la concurrencia.
const thresholds = {
  kc_invocation_ms: ['p(95)<500'],
  'kc_invocation_ms{method:TakeIngredient}': ['p(95)<500'],
  kc_invocation_ok: ['rate>0.99'],
  kc_one_winner: ['rate==1'],
  kc_session_ok: ['rate>0.99'],
  checks: ['rate>0.99'],
};

export const options = {
  scenarios: { [SCENARIO]: scenarios[SCENARIO] },
  thresholds,
};

// ---------------------------------------------------------------------------
// Cliente mínimo de SignalR
// ---------------------------------------------------------------------------

class Hub {
  constructor(name) {
    this.name = name;
    this.nextId = 1;
    this.pending = new Map();   // invocationId -> { resolve, target, started }
    this.handlers = new Map();  // evento del servidor -> función
    this.ws = null;
  }

  on(target, fn) { this.handlers.set(target, fn); }

  connect() {
    const res = http.post(`${BASE_URL}/gamehub/negotiate?negotiateVersion=1`, null, {
      tags: { name: 'negotiate' },
    });
    const ok = check(res, { 'negotiate 200': (r) => r.status === 200 });
    if (!ok) return Promise.reject(new Error(`negotiate ${res.status}`));

    const body = res.json();
    const token = body.connectionToken || body.connectionId;
    const url = `${BASE_URL.replace(/^http/, 'ws')}/gamehub?id=${token}`;

    return new Promise((resolve, reject) => {
      const ws = new WebSocket(url);
      this.ws = ws;
      let handshakeDone = false;

      ws.addEventListener('open', () => {
        ws.send(JSON.stringify({ protocol: 'json', version: 1 }) + RS);
      });
      ws.addEventListener('error', (e) => {
        clearTimeout(espera);
        const detail = e &&(e.message || (typeof e.error === 'string' ? e.error : '')) || 'sin detalle';
        reject(new Error(`ws error: ${detail}`));
      });
      // Si el servidor cierra antes de terminar el handshake, la sala cuenta como fallida
      ws.addEventListener('close', () => {
        clearTimeout(espera);
        if (!handshakeDone) reject(new Error('ws cerrado antes del handshake'));
        // Si se cae a mitad de partida, las llamadas pendientes cuentan como error y no se cuelgan
        for (const [id, p] of this.pending) {
          this.pending.delete(id);
          invocationOk.add(false, { method: p.target });
          p.resolve({ error: 'conexión cerrada' });
        }
      });
      const espera = setTimeout(() => reject(new Error('handshake sin respuesta en 10 s')), 10000);
      ws.addEventListener('message', (e) => {
        for (const raw of String(e.data).split(RS)) {
          if (!raw) continue;
          const msg = JSON.parse(raw);
          if (!handshakeDone) {
            handshakeDone = true;
            clearTimeout(espera);
            if (msg.error) reject(new Error(`handshake: ${msg.error}`));
            else resolve();
            continue;
          }
          this.dispatch(msg);
        }
      });
    });
  }

  dispatch(msg) {
    if (msg.type === 1) {                       // el servidor invoca un evento
      const fn = this.handlers.get(msg.target);
      if (fn) fn(...(msg.arguments || []));
    } else if (msg.type === 3) {                // fin de una invocación nuestra
      const p = this.pending.get(msg.invocationId);
      if (!p) return;
      this.pending.delete(msg.invocationId);
      const failed = !!msg.error;
      invocationMs.add(Date.now() - p.started, { method: p.target });
      invocationOk.add(!failed, { method: p.target });
      p.resolve(failed ? { error: msg.error } : { result: msg.result });
    } else if (msg.type === 6) {                // ping del servidor
      this.ws.send(JSON.stringify({ type: 6 }) + RS);
    }
  }

  invoke(target, ...args) {
    const id = String(this.nextId++);
    return new Promise((resolve) => {
      this.pending.set(id, { resolve, target, started: Date.now() });
      this.ws.send(JSON.stringify({ type: 1, invocationId: id, target, arguments: args }) + RS);
    });
  }

  close() { try { this.ws.close(); } catch (_) { /* ya cerrado */ } }
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

// ---------------------------------------------------------------------------
// Una sala de dos jugadores
// ---------------------------------------------------------------------------

export default async function () {
  const a = new Hub('A');
  const b = new Hub('B');
  let ok = false;
  let tope;

  try {
    // Tope de 40 s por sala: si algo se cuelga, la sala cuenta como fallida y k6 sigue
    const vencido = new Promise((_, reject) => {
      tope = setTimeout(() => reject(new Error('sala sin terminar en 40 s')), 40000);
    });
    ok = await Promise.race([play(a, b), vencido]);
  } catch (e) {
    console.error(`sesión fallida: ${e.message}`);
  } finally {
    clearTimeout(tope);
    a.close();
    b.close();
    sessionOk.add(ok);
    sessions.add(1);
  }
}

async function play(a, b) {
  const events = { winners: 0, conflicts: 0, stations: [], errors: [] };
  for (const h of [a, b]) {
    h.on('IngredientTaken', () => { events.winners++; });
    h.on('TakeIngredientError', () => { events.conflicts++; });
    h.on('JoinRoomError', (m) => events.errors.push(m));
    h.on('StartGameError', (m) => events.errors.push(m));
  }
  a.on('StationsUpdated', (s) => { events.stations = s; });

  await Promise.all([a.connect(), b.connect()]);

  const created = await a.invoke('CreateRoom', 'CargaA', 'avatar1');
  const code = created.result;
  check(code, { 'sala creada con código': (c) => typeof c === 'string' && c.length > 0 });

  await b.invoke('JoinRoom', code, 'CargaB', 'avatar2');
  await a.invoke('StartGame', code, 1);
  await a.invoke('GetStations', code);

  const station = events.stations.find((s) => s.isAvailable !== false) || events.stations[0];
  check(station, { 'hay estaciones': (s) => !!s });

  if (station) {
    // Los dos piden la misma estación a la vez: el lock debe dejar pasar solo a uno.
    await Promise.all([
      a.invoke('TakeIngredient', code, station.id),
      b.invoke('TakeIngredient', code, station.id),
    ]);
    const single = events.winners === 1 && events.conflicts === 1;
    oneWinner.add(single);
    check(null, { 'un solo ganador por estación': () => single });
  }

  // Un rato de movimiento y consultas, como una partida real
  for (let i = 0; i < 10; i++) {
    await Promise.all([
      a.invoke('MovePlayer', code, 100 + i * 10, 200, false),
      b.invoke('MovePlayer', code, 300 - i * 10, 200, true),
    ]);
    if (i === 4) await a.invoke('GetCurrentOrder', code);
    await sleep(250);
  }

  return events.errors.length === 0;
}
