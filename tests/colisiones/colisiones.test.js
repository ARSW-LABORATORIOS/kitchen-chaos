// Pruebas de la red de seguridad de colisiones.
//
// No se toca index.html: la prueba LEE el bloque de colisiones del propio archivo
// (constantes, cuadrícula, puntos de aparición, isWallCell y wouldCollide), lo ejecuta en una
// "caja" de Node y comprueba cosas que no deberían romperse nunca al cambiar el front.
//
//   node --test tests/colisiones/colisiones.test.js
//   UPDATE_GOLDEN=1 node --test tests/colisiones/colisiones.test.js   (solo si cambiaste el mapa A PROPÓSITO)

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const crypto = require('node:crypto');

const INDEX = path.join(__dirname, '..', '..', 'src', 'KitchenChaos.Server', 'wwwroot', 'index.html');
const GOLDEN = path.join(__dirname, 'golden.json');
const html = fs.readFileSync(INDEX, 'utf8');

function sliceBetween(startMarker, endMarker) {
    const start = html.indexOf(startMarker);
    const end = html.indexOf(endMarker, start);
    assert.ok(start >= 0, `No encontré "${startMarker}" en index.html: ¿cambió el código de colisiones?`);
    assert.ok(end > start, `No encontré "${endMarker}" en index.html: ¿cambió el código de colisiones?`);
    return html.slice(start, end);
}

// 1) constantes + cuadrículas + spawns + pickSpawnPoint   2) isWallCell, wouldCollide, bodySeparation
const datos = sliceBetween('const GRID_COLS', 'let myPosition');
const funciones = sliceBetween('function isWallCell', 'function wouldCollideWithPlayers');

const sandbox = vm.createContext({});
const juego = vm.runInContext(`
    ${datos}
    let currentLevelWalls = WALLS_BY_LEVEL[1];
    ${funciones}
    ({
        GRID_COLS, GRID_ROWS, PLAYER_SPEED, PLAYER_HALF_W, PLAYER_HALF_H,
        PLAYER_BODY_HALF_W, PLAYER_BODY_HALF_H,
        WALLS_BY_LEVEL, SPAWN_POINTS_BY_LEVEL,
        setLevel: function (n) { currentLevelWalls = WALLS_BY_LEVEL[n]; },
        wouldCollide, bodySeparation
    })
`, sandbox);

const NIVELES = [1, 2, 3, 4];

test('la cuadrícula de cada nivel es 40x40 y solo tiene 0 (piso) o 1 (pared)', () => {
    for (const n of NIVELES) {
        const grid = juego.WALLS_BY_LEVEL[n];
        assert.equal(grid.length, juego.GRID_ROWS, `nivel ${n}: filas`);
        for (const fila of grid) {
            assert.equal(fila.length, juego.GRID_COLS, `nivel ${n}: columnas`);
            for (const celda of fila) assert.ok(celda === 0 || celda === 1, `nivel ${n}: celda inválida ${celda}`);
        }
    }
});

test('cada nivel tiene al menos 4 puntos de aparición, todos en piso libre', () => {
    for (const n of NIVELES) {
        const spawns = juego.SPAWN_POINTS_BY_LEVEL[n];
        assert.ok(spawns && spawns.length >= 4, `nivel ${n}: faltan puntos de aparición`);
        juego.setLevel(n);
        spawns.forEach((s, i) => {
            assert.equal(juego.wouldCollide(s.x, s.y), false,
                `nivel ${n}: el spawn ${i + 1} (${s.x}, ${s.y}) cae dentro de una pared`);
        });
    }
});

test('los puntos de aparición de un nivel no se encaraman entre sí', () => {
    for (const n of NIVELES) {
        const spawns = juego.SPAWN_POINTS_BY_LEVEL[n];
        for (let i = 0; i < spawns.length; i++) {
            for (let j = i + 1; j < spawns.length; j++) {
                const sep = juego.bodySeparation(spawns[i].x, spawns[i].y, spawns[j].x, spawns[j].y);
                assert.ok(sep >= 1, `nivel ${n}: los spawns ${i + 1} y ${j + 1} quedan encimados`);
            }
        }
    }
});

// Camina como el juego (por ejes, pasos de 0.5 %) desde el primer spawn y mira a dónde llega.
function zonaAlcanzable(nivel) {
    juego.setLevel(nivel);
    const STEP = 0.5;
    const spawns = juego.SPAWN_POINTS_BY_LEVEL[nivel];
    const clave = (x, y) => `${Math.round(x / STEP)},${Math.round(y / STEP)}`;
    const visitados = new Map();
    const cola = [{ x: spawns[0].x, y: spawns[0].y }];
    visitados.set(clave(cola[0].x, cola[0].y), cola[0]);
    while (cola.length) {
        const p = cola.pop();
        for (const [dx, dy] of [[STEP, 0], [-STEP, 0], [0, STEP], [0, -STEP]]) {
            const nx = Math.max(2, Math.min(98, p.x + dx));
            const ny = Math.max(2, Math.min(98, p.y + dy));
            const k = clave(nx, ny);
            if (visitados.has(k) || juego.wouldCollide(nx, ny)) continue;
            const q = { x: nx, y: ny };
            visitados.set(k, q);
            cola.push(q);
        }
    }
    return visitados;
}

test('desde un spawn se puede caminar hasta todos los demás (nadie queda encerrado)', () => {
    for (const n of NIVELES) {
        const alcanzables = Array.from(zonaAlcanzable(n).values());
        juego.SPAWN_POINTS_BY_LEVEL[n].forEach((s, i) => {
            const cerca = alcanzables.some(p => Math.hypot(p.x - s.x, p.y - s.y) <= 0.75);
            assert.ok(cerca, `nivel ${n}: desde el spawn 1 no se puede llegar al spawn ${i + 1}`);
        });
    }
});

test('el área caminable de cada nivel es razonable (ni vacía ni casi todo el tablero)', () => {
    for (const n of NIVELES) {
        const total = zonaAlcanzable(n).size;
        const maximo = (100 / 0.5) * (100 / 0.5);
        assert.ok(total > maximo * 0.05, `nivel ${n}: casi no se puede caminar (${total} puntos)`);
        assert.ok(total < maximo * 0.9, `nivel ${n}: casi todo es caminable (${total} puntos), ¿se borraron paredes?`);
    }
});

// "Foto" del mapa: si alguien cambia paredes o constantes (a propósito o sin querer), esta prueba avisa.
function foto() {
    const niveles = {};
    for (const n of NIVELES) {
        const grid = juego.WALLS_BY_LEVEL[n];
        niveles[n] = {
            celdasCaminables: grid.flat().filter(c => c === 0).length,
            sha1: crypto.createHash('sha1').update(JSON.stringify(grid)).digest('hex'),
            spawns: juego.SPAWN_POINTS_BY_LEVEL[n]
        };
    }
    return {
        constantes: {
            GRID_COLS: juego.GRID_COLS, GRID_ROWS: juego.GRID_ROWS, PLAYER_SPEED: juego.PLAYER_SPEED,
            PLAYER_HALF_W: juego.PLAYER_HALF_W, PLAYER_HALF_H: juego.PLAYER_HALF_H,
            PLAYER_BODY_HALF_W: juego.PLAYER_BODY_HALF_W, PLAYER_BODY_HALF_H: juego.PLAYER_BODY_HALF_H
        },
        niveles
    };
}

test('el mapa y las constantes de colisión no cambiaron respecto a la foto guardada', () => {
    const actual = JSON.parse(JSON.stringify(foto()));
    if (process.env.UPDATE_GOLDEN === '1' || !fs.existsSync(GOLDEN)) {
        fs.writeFileSync(GOLDEN, JSON.stringify(actual, null, 2) + '\n');
        console.log('golden.json actualizado');
        return;
    }
    const guardada = JSON.parse(fs.readFileSync(GOLDEN, 'utf8'));
    assert.deepEqual(actual, guardada,
        'Cambió la cuadrícula, los spawns o las constantes de colisión. ' +
        'Si fue a propósito, corre: UPDATE_GOLDEN=1 node --test tests/colisiones/colisiones.test.js y sube golden.json.');
});
