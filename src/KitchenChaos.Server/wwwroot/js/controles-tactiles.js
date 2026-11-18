/*
 * Controles táctiles para jugar desde el celular (joystick + botones de acción).
 *
 * IDEA CLAVE: esta capa NO toca la lógica del juego. Para moverse y actuar dispara los MISMOS
 * eventos de teclado que usa el PC (W A S D, Espacio, R, P, Escape), así que el celular se comporta
 * exactamente igual que el teclado: mismas colisiones, mismas estaciones, mismo servidor.
 *
 * Solo se activa en pantallas táctiles (o con ?tactil=1 para probarlo en PC). Con ?tactil=0 se apaga.
 * En un PC con mouse y teclado no crea nada.
 */
(function () {
    'use strict';

    var params = new URLSearchParams(location.search);
    var forzado = params.get('tactil');
    var esTactil = forzado === '1' ||
        (forzado !== '0' && window.matchMedia &&
            matchMedia('(pointer: coarse)').matches && matchMedia('(hover: none)').matches);

    if (!esTactil) return;

    document.body.classList.add('tactil');

    // ---------- Teclado simulado ----------
    function tecla(key, abajo) {
        window.dispatchEvent(new KeyboardEvent(abajo ? 'keydown' : 'keyup', {
            key: key, bubbles: true, cancelable: true
        }));
    }

    function soltarFoco() {
        // Si un campo de texto sigue con el foco, el juego ignoraría las teclas.
        var a = document.activeElement;
        if (a && a !== document.body && typeof a.blur === 'function') a.blur();
    }

    function vibrar(ms) {
        try { if (navigator.vibrate) navigator.vibrate(ms); } catch (e) { }
    }

    // ---------- Interfaz ----------
    var ICONOS = {
        tomar: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M3 10h18l-2 9.5a1 1 0 0 1-1 .5H6a1 1 0 0 1-1-.5z"/><path d="M8 10l3-6M16 10l-3-6"/></svg>',
        cortar: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M4.5 19.5 17 7c1.3-1.3 2.9-1.5 3.8-1 .5.9.3 2.5-1 3.8L6.5 21.5z"/><path d="M3.5 21l1-1"/></svg>',
        cocinar: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M5 10h14v7a3 3 0 0 1-3 3H8a3 3 0 0 1-3-3z"/><path d="M3 12.5h2M19 12.5h2M9 7c0-1.6 1-1.6 1-3.2M14 7c0-1.6 1-1.6 1-3.2"/></svg>',
        servir: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M3 19h18"/><path d="M5 19a7 7 0 0 1 14 0"/><path d="M12 12V9.5"/><circle cx="12" cy="8.6" r="1"/></svg>'
    };

    var ACCIONES = [
        { cat: 'tomar', texto: 'Tomar' },
        { cat: 'cortar', texto: 'Cortar' },
        { cat: 'cocinar', texto: 'Cocinar' },
        { cat: 'servir', texto: 'Servir' }
    ];

    var raiz = document.createElement('div');
    raiz.id = 'kcTactil';
    raiz.className = 'kc-tactil';
    raiz.hidden = true;
    raiz.innerHTML =
        '<div class="kc-aviso-giro" id="kcAvisoGiro" hidden>' +
        '<span>📱 Gira el celular para jugar mejor</span>' +
        '<button type="button" id="kcAvisoGiroOk" aria-label="Cerrar aviso">✕</button>' +
        '</div>' +
        '<div class="kc-joystick" id="kcJoystick" aria-label="Joystick para moverte">' +
        '<div class="kc-joy-base"><div class="kc-joy-knob" id="kcJoyKnob"></div></div>' +
        '</div>' +
        '<div class="kc-acciones">' +
        '<div class="kc-pista" id="kcPista" hidden></div>' +
        '<div class="kc-extras">' +
        '<button type="button" class="kc-mini" id="kcBtnInventario" aria-label="Inventario">' +
        '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><path d="M5 7h14M5 12h14M5 17h14"/></svg></button>' +
        '<button type="button" class="kc-mini" id="kcBtnPausa" aria-label="Pausa">' +
        '<svg viewBox="0 0 24 24" fill="currentColor"><rect x="6" y="5" width="4" height="14" rx="1"/><rect x="14" y="5" width="4" height="14" rx="1"/></svg></button>' +
        '</div>' +
        '<div class="kc-acc-grid">' +
        ACCIONES.map(function (a) {
            return '<button type="button" class="kc-accion" data-cat="' + a.cat + '" data-texto="' + a.texto + '">' +
                ICONOS[a.cat] + '<span>' + a.texto + '</span></button>';
        }).join('') +
        '</div>' +
        '</div>';
    document.body.appendChild(raiz);

    var joystick = document.getElementById('kcJoystick');
    var knob = document.getElementById('kcJoyKnob');
    var pista = document.getElementById('kcPista');
    var avisoGiro = document.getElementById('kcAvisoGiro');
    var botones = Array.prototype.slice.call(raiz.querySelectorAll('.kc-accion'));

    // ---------- Joystick ----------
    var dirs = { w: false, a: false, s: false, d: false };
    var punteroJoystick = null;

    function ponerDir(key, activa) {
        if (dirs[key] === activa) return;
        dirs[key] = activa;
        tecla(key, activa);
    }

    function soltarJoystick() {
        punteroJoystick = null;
        knob.style.transform = 'translate(0px, 0px)';
        ['w', 'a', 's', 'd'].forEach(function (k) { ponerDir(k, false); });
    }

    function moverJoystick(e) {
        var base = joystick.querySelector('.kc-joy-base').getBoundingClientRect();
        var cx = base.left + base.width / 2;
        var cy = base.top + base.height / 2;
        var dx = e.clientX - cx;
        var dy = e.clientY - cy;
        var max = base.width / 2;
        var largo = Math.hypot(dx, dy);

        if (largo > max) { dx *= max / largo; dy *= max / largo; largo = max; }
        knob.style.transform = 'translate(' + dx + 'px, ' + dy + 'px)';

        if (largo < max * 0.3) { // zona muerta al centro
            ['w', 'a', 's', 'd'].forEach(function (k) { ponerDir(k, false); });
            return;
        }

        // 8 direcciones: 0° = derecha, 90° = abajo
        var ang = Math.atan2(dy, dx) * 180 / Math.PI;
        ponerDir('d', ang > -67.5 && ang < 67.5);
        ponerDir('s', ang > 22.5 && ang < 157.5);
        ponerDir('a', ang > 112.5 || ang < -112.5);
        ponerDir('w', ang < -22.5 && ang > -157.5);
    }

    joystick.addEventListener('pointerdown', function (e) {
        if (punteroJoystick !== null) return;
        e.preventDefault();
        soltarFoco();
        punteroJoystick = e.pointerId;
        try { joystick.setPointerCapture(e.pointerId); } catch (err) { }
        moverJoystick(e);
    });
    joystick.addEventListener('pointermove', function (e) {
        if (e.pointerId === punteroJoystick) { e.preventDefault(); moverJoystick(e); }
    });
    ['pointerup', 'pointercancel', 'lostpointercapture'].forEach(function (ev) {
        joystick.addEventListener(ev, function (e) {
            if (e.pointerId === punteroJoystick || ev === 'lostpointercapture') soltarJoystick();
        });
    });

    // Si la pantalla se apaga o se cambia de app, se suelta todo para que el personaje no siga caminando solo.
    document.addEventListener('visibilitychange', function () { if (document.hidden) soltarJoystick(); });
    window.addEventListener('blur', soltarJoystick);

    // ---------- Botones de acción ----------
    function categoria(el) {
        if (!el) return null;
        var c = el.classList;
        if (c.contains('ingredient-station') || c.contains('take-plate-station')) return 'tomar';
        if (c.contains('chop-station')) return 'cortar';
        if (c.contains('cook-station')) return 'cocinar';
        if (c.contains('plate-station') || c.contains('delivery-station') ||
            c.contains('wash-station') || c.contains('trash-station')) return 'servir';
        return null;
    }

    // Texto más específico según la estación que se tiene delante
    function etiqueta(el, cat) {
        var c = el.classList;
        if (cat === 'tomar') return c.contains('take-plate-station') ? 'Plato' : 'Tomar';
        if (cat === 'cortar') return 'Cortar';
        if (cat === 'cocinar') {
            if (c.contains('fry-station') || c.contains('fry-station-2')) return 'Freír';
            if (c.contains('boil-station')) return 'Hervir';
            return 'Cocinar';
        }
        if (c.contains('plate-station')) return 'Emplatar';
        if (c.contains('delivery-station')) return 'Entregar';
        if (c.contains('wash-station')) return 'Lavar';
        if (c.contains('trash-station')) return 'Tirar';
        return 'Servir';
    }

    function objetivo() {
        try { return typeof getTargetedStation === 'function' ? getTargetedStation() : null; }
        catch (e) { return null; }
    }

    var temporizadorPista = null;
    function mostrarPista(texto) {
        pista.textContent = texto;
        pista.hidden = false;
        clearTimeout(temporizadorPista);
        temporizadorPista = setTimeout(function () { pista.hidden = true; }, 1400);
    }

    function sacudir(btn) {
        btn.classList.remove('kc-sacudir');
        void btn.offsetWidth; // reinicia la animación
        btn.classList.add('kc-sacudir');
    }

    function pulsarAccion(btn) {
        soltarFoco();
        var el = objetivo();
        var cat = categoria(el);

        if (!el) {
            sacudir(btn);
            mostrarPista('Acércate a una estación');
            return;
        }
        if (cat !== btn.getAttribute('data-cat')) {
            sacudir(btn);
            var correcto = botones.filter(function (b) { return b.getAttribute('data-cat') === cat; })[0];
            mostrarPista('Aquí se usa «' + etiqueta(el, cat) + '»');
            if (correcto) { correcto.classList.add('kc-aviso'); setTimeout(function () { correcto.classList.remove('kc-aviso'); }, 900); }
            return;
        }
        vibrar(18);
        tecla(' ', true);   // Espacio = "usar lo que tengo más cerca"
        tecla(' ', false);
    }

    botones.forEach(function (b) {
        b.addEventListener('pointerdown', function (e) { e.preventDefault(); pulsarAccion(b); });
        b.addEventListener('contextmenu', function (e) { e.preventDefault(); });
    });

    function pulsarTecla(id, key) {
        document.getElementById(id).addEventListener('pointerdown', function (e) {
            e.preventDefault();
            soltarFoco();
            vibrar(12);
            tecla(key, true);
            tecla(key, false);
        });
    }
    pulsarTecla('kcBtnInventario', 'r');
    pulsarTecla('kcBtnPausa', 'p');

    // En el celular no hay tecla Esc: los textos "Esc · Cerrar" y "R · Cerrar" pasan a ser botones.
    function activarCierres() {
        document.querySelectorAll('.station-ingredient-header span, .desktop-inventory-header span').forEach(function (s) {
            if (s.getAttribute('data-tactil')) return;
            s.setAttribute('data-tactil', '1');
            s.textContent = '✕ Cerrar';
            s.setAttribute('role', 'button');
            s.addEventListener('pointerdown', function (e) {
                e.preventDefault();
                tecla('escape', true);
                tecla('escape', false);
            });
        });
        var ayuda = document.querySelector('.desktop-inventory-help');
        if (ayuda) ayuda.textContent = 'Toca un ingrediente para seleccionarlo.';
    }
    activarCierres();

    // "¿Cómo jugar?": en el celular los controles son el joystick y los botones, no el teclado.
    var listaControles = document.querySelector('.instructions .controls-list');
    if (listaControles) {
        listaControles.innerHTML =
            '<li><strong>Joystick</strong> (abajo a la izquierda) — moverte por la cocina</li>' +
            '<li><strong>Tomar / Cortar / Cocinar / Servir</strong> — el botón naranja es el que sirve para la estación que tienes delante</li>' +
            '<li><strong>Inventario</strong> y <strong>Pausa</strong> — los dos botoncitos redondos</li>';
    }

    // ---------- Aviso de girar el celular ----------
    var avisoDescartado = false;
    try { avisoDescartado = sessionStorage.getItem('kc.giro') === '1'; } catch (e) { }
    document.getElementById('kcAvisoGiroOk').addEventListener('click', function () {
        avisoDescartado = true;
        try { sessionStorage.setItem('kc.giro', '1'); } catch (e) { }
        avisoGiro.hidden = true;
    });

    // ---------- Actualización periódica ----------
    function juegoVisible() {
        var gs = document.getElementById('gameScreen');
        return !!gs && document.body.classList.contains('in-game') && getComputedStyle(gs).display !== 'none';
    }

    var habiaJuego = false;
    function actualizar() {
        var visible = juegoVisible();
        raiz.hidden = !visible;

        if (!visible) {
            if (habiaJuego) soltarJoystick();
            habiaJuego = false;
            return;
        }
        habiaJuego = true;

        var vertical = window.matchMedia('(orientation: portrait)').matches;
        avisoGiro.hidden = !(vertical && !avisoDescartado);

        var el = objetivo();
        var cat = categoria(el);
        botones.forEach(function (b) {
            var esa = !!cat && b.getAttribute('data-cat') === cat;
            b.classList.toggle('kc-sugerido', esa);
            b.classList.toggle('kc-atenuado', !!cat && !esa);
            var span = b.querySelector('span');
            var texto = esa ? etiqueta(el, cat) : b.getAttribute('data-texto');
            if (span.textContent !== texto) span.textContent = texto;
        });
    }

    setInterval(actualizar, 120);
    actualizar();

    // ---------- Modo "usar el celular como control de un PC" ----------
    var howTo = document.getElementById('howToPlayButton');
    if (howTo && !document.getElementById('kcLinkControl')) {
        var enlace = document.createElement('a');
        enlace.id = 'kcLinkControl';
        enlace.className = 'kc-link-control';
        enlace.href = 'control.html';
        enlace.textContent = '🎮 Usar mi celular como control de un PC';
        howTo.insertAdjacentElement('afterend', enlace);
    }
})();
