/*
 * Interfaz del menú: botón de sonido, ajustes de volumen e indicador de conexión.
 *
 * No toca la lógica del juego. Solo expone window.kcAudio (volumen y silencio), que
 * playLevelMusic() de index.html lee al crear la música de cada nivel, y aplica los cambios
 * a la música que ya esté sonando (currentMusic).
 */
(function () {
    'use strict';

    var CLAVE = 'kc.audio';
    var estado = { volume: 0.5, muted: false };

    // localStorage puede fallar (modo privado, datos bloqueados): el juego funciona igual sin él.
    try {
        var guardado = JSON.parse(localStorage.getItem(CLAVE) || 'null');
        if (guardado && typeof guardado.volume === 'number') {
            estado.volume = Math.min(1, Math.max(0, guardado.volume));
            estado.muted = guardado.muted === true;
        }
    } catch (e) { /* se usan los valores por defecto */ }

    window.kcAudio = estado;

    function guardar() {
        try { localStorage.setItem(CLAVE, JSON.stringify(estado)); } catch (e) { }
    }

    function aplicarAMusica() {
        try {
            if (typeof currentMusic !== 'undefined' && currentMusic) {
                currentMusic.volume = estado.volume;
                currentMusic.muted = estado.muted;
            }
        } catch (e) { }
    }

    var btnSonido = document.getElementById('kcBtnSonido');
    var btnAjustes = document.getElementById('kcBtnAjustes');
    var panel = document.getElementById('kcPanelAjustes');
    var slider = document.getElementById('kcVolumen');

    function pintarSonido() {
        if (!btnSonido) return;
        btnSonido.classList.toggle('silenciado', estado.muted);
        btnSonido.setAttribute('aria-pressed', String(estado.muted));
        btnSonido.setAttribute('aria-label', estado.muted ? 'Activar música' : 'Silenciar música');
    }

    if (btnSonido) {
        btnSonido.addEventListener('click', function () {
            estado.muted = !estado.muted;
            pintarSonido();
            aplicarAMusica();
            guardar();
            btnSonido.blur(); // para que la barra espaciadora no lo vuelva a activar en el juego
        });
    }

    if (slider) {
        slider.value = String(Math.round(estado.volume * 100));
        slider.addEventListener('input', function () {
            estado.volume = Number(slider.value) / 100;
            if (estado.volume > 0 && estado.muted) { estado.muted = false; pintarSonido(); }
            aplicarAMusica();
            guardar();
        });
    }

    function abrirPanel(abrir) {
        if (!panel || !btnAjustes) return;
        panel.hidden = !abrir;
        btnAjustes.setAttribute('aria-expanded', String(abrir));
    }

    if (btnAjustes) {
        btnAjustes.addEventListener('click', function () {
            abrirPanel(panel.hidden);
            btnAjustes.blur();
        });
        document.addEventListener('click', function (e) {
            if (!panel || panel.hidden) return;
            var contenedor = document.getElementById('kcAjustes');
            if (contenedor && !contenedor.contains(e.target)) abrirPanel(false);
        });
        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape' && panel && !panel.hidden) abrirPanel(false);
        });
    }

    pintarSonido();

    // Indicador de conexión: el juego solo cambia el texto de #connectionStatus.
    // Aquí se traduce ese texto a un estado (ok / aviso / error) para pintar el puntito de color.
    var estadoConexion = document.getElementById('connectionStatus');
    function clasificarConexion() {
        if (!estadoConexion) return;
        var t = (estadoConexion.textContent || '').toLowerCase();
        var s = 'aviso';
        if (t.indexOf('servidor conectado') !== -1) s = 'ok';
        else if (t.indexOf('no se pudo') !== -1 || t.indexOf('perdi') !== -1 || t.indexOf('error') !== -1) s = 'error';
        estadoConexion.setAttribute('data-estado', s);
    }
    if (estadoConexion) {
        clasificarConexion();
        new MutationObserver(clasificarConexion)
            .observe(estadoConexion, { childList: true, characterData: true, subtree: true });
    }
})();
