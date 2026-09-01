// Botón "mostrar/ocultar" junto a un <input type="password">: alterna el atributo type y el
// ícono. data-target en el botón debe apuntar al id del input.
(function () {
    document.querySelectorAll('.pwd-toggle').forEach(function (btn) {
        var input = document.getElementById(btn.dataset.target);
        if (!input) return;
        btn.addEventListener('click', function () {
            var show = input.type === 'password';
            input.type = show ? 'text' : 'password';
            btn.setAttribute('aria-label', show ? 'Ocultar contraseña' : 'Mostrar contraseña');
            btn.textContent = show ? '🙈' : '👁';
        });
    });
})();
