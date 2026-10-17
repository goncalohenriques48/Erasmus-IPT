// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.

(function () {
    'use strict';

    const toggle = document.getElementById('theme-toggle');
    const switchEl = document.querySelector('#theme-toggle .theme-switch') || document.getElementById('theme-toggle');
    const sunIcon = document.getElementById('theme-sun');
    const moonIcon = document.getElementById('theme-moon');
    const storageKey = 'theme';

    function setTheme(theme) {
        if (theme) {
            document.documentElement.setAttribute('data-bs-theme', theme);
            try {
                localStorage.setItem(storageKey, theme);
            } catch (e) { }
            // update small icon visibility if present
            if (sunIcon && moonIcon) {
                if (theme === 'dark') {
                    sunIcon.style.opacity = '0.3';
                    moonIcon.style.opacity = '1';
                } else {
                    sunIcon.style.opacity = '1';
                    moonIcon.style.opacity = '0.6';
                }
            }
        }
    }

    function getPreferredTheme() {
        try {
            const stored = localStorage.getItem(storageKey);
            if (stored) return stored;
        } catch (e) { }
        if (window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches) {
            return 'dark';
        }
        return 'light';
    }

    // Apply initial theme early if possible
    const initial = getPreferredTheme();
    setTheme(initial);

    // set initial icon states
    if (sunIcon && moonIcon) {
        if (initial === 'dark') {
            sunIcon.style.opacity = '0.3';
            moonIcon.style.opacity = '1';
        } else {
            sunIcon.style.opacity = '1';
            moonIcon.style.opacity = '0.6';
        }
    }

    // Attach click to the inner switch element so only clicks on the switch toggle the theme
    if (switchEl) {
        switchEl.addEventListener('click', function (e) {
            // prevent outer button or other handlers from also processing
            e.stopPropagation();
            const current = document.documentElement.getAttribute('data-bs-theme') === 'dark' ? 'dark' : 'light';
            const next = current === 'dark' ? 'light' : 'dark';
            setTheme(next);
        });
    }
})();

// --- Lógica do Botão Scroll to Top ---
const mybutton = document.getElementById("btn-back-to-top");

if (mybutton) {
    // Escuta o evento de scroll na página
    window.addEventListener('scroll', () => {
        // Se a página descer mais de 100px, mostra o botão (como flex para centrar o ícone)
        if (document.body.scrollTop > 100 || document.documentElement.scrollTop > 100) {
            mybutton.style.display = "flex";
        } else {
            mybutton.style.display = "none";
        }
    });

    // Quando clica no botão, sobe de forma suave (smooth)
    mybutton.addEventListener('click', () => {
        window.scrollTo({
            top: 0,
            behavior: 'smooth' // É isto que faz o efeito de deslizar em vez de saltar!
        });
    });
}
