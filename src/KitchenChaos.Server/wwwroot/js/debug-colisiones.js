/*
 * Modo depuración de colisiones. Solo se carga si la URL lleva ?debug=1
 * (index.html lo pide con una sola línea al final). En juego normal NO se descarga ni se ejecuta.
 *
 * No modifica ninguna variable del juego: solo LEE las globales de index.html
 * (currentLevelWalls, myPosition, PLAYER_HALF_W, etc.) y dibuja encima del tablero.
 *
 * Qué se ve:
 *   rojo        celdas de pared de la cuadrícula 40x40 (lo que realmente bloquea)
 *   cian        caja de pies del jugador (la que se compara con las paredes)
 *   magenta     caja de cuerpo (la que se compara con otros jugadores)
 *   naranja     cuerpo de los otros jugadores
 *   amarillo    estaciones (hotspots); en verde la que se activaría con Espacio
 *   azul        puntos de aparición (spawn) del nivel
 *
 * Teclas:  F2 = mostrar/ocultar    Shift + clic en el tablero = ver coordenadas y celda
 */
(function () {
    'use strict';

    var visible = true;
    var lastClick = null;
    var canvas = null;
    var panel = null;

    function g(name) {
        // Las const/let de index.html son globales "léxicas": se leen por nombre, no por window.
        try { return (0, eval)(name); } catch (e) { return undefined; }
    }

    function board() { return document.querySelector('.game-board'); }

    function ensureElements() {
        var b = board();
        if (!b) return false;

        if (!canvas || canvas.parentNode !== b) {
            canvas = document.createElement('canvas');
            canvas.id = 'debugColisionesCanvas';
            canvas.style.cssText =
                'position:absolute;left:0;top:0;width:100%;height:100%;' +
                'pointer-events:none;z-index:99999;';
            b.appendChild(canvas);
        }

        if (!panel) {
            panel = document.createElement('pre');
            panel.id = 'debugColisionesPanel';
            panel.style.cssText =
                'position:fixed;left:8px;bottom:8px;z-index:100000;margin:0;padding:8px 10px;' +
                'background:rgba(0,0,0,.78);color:#9fffb0;font:12px/1.35 Consolas,monospace;' +
                'border-radius:6px;pointer-events:none;max-width:90vw;white-space:pre-wrap;';
            document.body.appendChild(panel);
        }
        return true;
    }

    function px(v, total) { return (v / 100) * total; }

    function drawBox(ctx, cx, cy, halfW, halfH, w, h, color, dashed) {
        ctx.save();
        ctx.strokeStyle = color;
        ctx.lineWidth = 2;
        if (dashed) ctx.setLineDash([5, 4]);
        ctx.strokeRect(px(cx - halfW, w), px(cy - halfH, h), px(halfW * 2, w), px(halfH * 2, h));
        ctx.restore();
    }

    function draw() {
        if (!ensureElements()) return;

        var b = board();
        var w = b.clientWidth, h = b.clientHeight;

        // Fuera de la partida (menú o sala de espera) el tablero está oculto: no hay nada que mostrar.
        var mostrar = visible && w > 0 && h > 0;
        canvas.style.display = mostrar ? 'block' : 'none';
        panel.style.display = mostrar ? 'block' : 'none';
        if (!mostrar) return;

        var dpr = window.devicePixelRatio || 1;
        if (canvas.width !== Math.round(w * dpr) || canvas.height !== Math.round(h * dpr)) {
            canvas.width = Math.round(w * dpr);
            canvas.height = Math.round(h * dpr);
        }
        var ctx = canvas.getContext('2d');
        ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
        ctx.clearRect(0, 0, w, h);

        var walls = g('currentLevelWalls');
        var cols = g('GRID_COLS'), rows = g('GRID_ROWS');
        if (!walls || !cols || !rows) return;

        // Paredes
        var cw = w / cols, ch = h / rows;
        ctx.fillStyle = 'rgba(255,40,40,0.32)';
        for (var r = 0; r < rows; r++) {
            for (var c = 0; c < cols; c++) {
                if (!walls[r] || walls[r][c] !== 0) ctx.fillRect(c * cw, r * ch, cw, ch);
            }
        }
        // Rejilla suave
        ctx.strokeStyle = 'rgba(255,255,255,0.18)';
        ctx.lineWidth = 1;
        ctx.beginPath();
        for (var i = 0; i <= cols; i++) { ctx.moveTo(i * cw, 0); ctx.lineTo(i * cw, h); }
        for (var j = 0; j <= rows; j++) { ctx.moveTo(0, j * ch); ctx.lineTo(w, j * ch); }
        ctx.stroke();

        // Puntos de aparición
        var level = g('currentLevel') || 1;
        var spawnsByLevel = g('SPAWN_POINTS_BY_LEVEL') || {};
        var spawns = spawnsByLevel[level] || spawnsByLevel[1] || [];
        ctx.font = 'bold 13px sans-serif';
        spawns.forEach(function (s, idx) {
            ctx.fillStyle = 'rgba(60,140,255,0.9)';
            ctx.beginPath(); ctx.arc(px(s.x, w), px(s.y, h), 7, 0, Math.PI * 2); ctx.fill();
            ctx.fillStyle = '#fff';
            ctx.fillText(String(idx + 1), px(s.x, w) - 4, px(s.y, h) + 5);
        });

        // Estaciones
        var targeted = null;
        try { targeted = typeof getTargetedStation === 'function' ? getTargetedStation() : null; } catch (e) { }
        var br = b.getBoundingClientRect();
        b.querySelectorAll('.station-hotspot').forEach(function (el) {
            if (el.offsetParent === null) return;
            var rc = el.getBoundingClientRect();
            var x = rc.left - br.left, y = rc.top - br.top;
            if (el === targeted) {
                ctx.fillStyle = 'rgba(40,220,90,0.35)';
                ctx.fillRect(x, y, rc.width, rc.height);
            }
            ctx.strokeStyle = el === targeted ? '#28dc5a' : '#ffd23c';
            ctx.lineWidth = 2;
            ctx.strokeRect(x, y, rc.width, rc.height);
        });

        // Otros jugadores (cuerpo)
        var bodyW = g('PLAYER_BODY_HALF_W'), bodyH = g('PLAYER_BODY_HALF_H');
        var others = g('otherPlayers') || {};
        Object.keys(others).forEach(function (id) {
            var p = others[id];
            drawBox(ctx, p.x, p.y, bodyW, bodyH, w, h, '#ff9a2e', false);
        });

        // Yo: pies (pared) y cuerpo (otros jugadores)
        var me = g('myPosition');
        var footW = g('PLAYER_HALF_W'), footH = g('PLAYER_HALF_H');
        if (me) {
            drawBox(ctx, me.x, me.y, bodyW, bodyH, w, h, '#ff4df0', true);
            drawBox(ctx, me.x, me.y, footW, footH, w, h, '#19e6ff', false);
            ctx.fillStyle = '#19e6ff';
            ctx.beginPath(); ctx.arc(px(me.x, w), px(me.y, h), 3, 0, Math.PI * 2); ctx.fill();
        }

        // Clic inspeccionado
        if (lastClick) {
            ctx.strokeStyle = '#fff'; ctx.lineWidth = 2;
            ctx.beginPath();
            ctx.arc(px(lastClick.x, w), px(lastClick.y, h), 6, 0, Math.PI * 2);
            ctx.stroke();
        }

        // Panel de texto
        var lines = [];
        lines.push('DEBUG COLISIONES   (F2 oculta  |  Shift+clic inspecciona)');
        lines.push('nivel: ' + level + '   tablero: ' + Math.round(w) + 'x' + Math.round(h) + ' px');
        if (me) {
            var hit = typeof wouldCollide === 'function' ? wouldCollide(me.x, me.y) : '?';
            lines.push('yo: x=' + me.x.toFixed(2) + '  y=' + me.y.toFixed(2) +
                '   celda [fila ' + Math.floor(me.y / 100 * rows) + ', col ' + Math.floor(me.x / 100 * cols) + ']' +
                '   choca ahora: ' + hit);
        }
        lines.push('mirando: ' + (g('myFacingDirection') || '?') +
            '   estación al pulsar Espacio: ' + (targeted ? describeStation(targeted) : '(ninguna)'));
        if (lastClick) {
            lines.push('clic: x=' + lastClick.x.toFixed(2) + '  y=' + lastClick.y.toFixed(2) +
                '   celda [fila ' + lastClick.row + ', col ' + lastClick.col + ']   ' +
                (lastClick.wall ? 'PARED' : 'piso libre'));
        }
        panel.textContent = lines.join('\n');
    }

    function describeStation(el) {
        var cls = Array.prototype.filter.call(el.classList, function (c) { return c !== 'station-hotspot'; });
        var ing = el.getAttribute('data-ingredient');
        return cls.join('.') + (ing ? ' (' + ing + ')' : '');
    }

    function onBoardClick(e) {
        if (!e.shiftKey) return;
        var b = board();
        if (!b) return;
        e.preventDefault();
        e.stopPropagation();
        var rc = b.getBoundingClientRect();
        var x = (e.clientX - rc.left) / rc.width * 100;
        var y = (e.clientY - rc.top) / rc.height * 100;
        var cols = g('GRID_COLS'), rows = g('GRID_ROWS');
        var col = Math.floor(x / 100 * cols), row = Math.floor(y / 100 * rows);
        var walls = g('currentLevelWalls');
        lastClick = { x: x, y: y, row: row, col: col, wall: !walls || !walls[row] || walls[row][col] !== 0 };
    }

    try {
        document.addEventListener('click', onBoardClick, true);
        window.addEventListener('keydown', function (e) {
            if (e.key === 'F2') { e.preventDefault(); visible = !visible; draw(); }
        });
        setInterval(function () {
            try { draw(); } catch (err) { /* la depuración nunca debe romper el juego */ }
        }, 80);
        console.info('[debug-colisiones] activo. F2 para ocultar, Shift+clic para inspeccionar.');
    } catch (err) {
        console.warn('[debug-colisiones] no se pudo iniciar:', err);
    }
})();
