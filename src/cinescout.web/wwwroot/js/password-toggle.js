// Plain JS/CSS, not a Blazor interactive island — issue #72's islands research ruled that out
// (any one island on a static-SSR page pulls the whole WASM runtime onto it). Event delegation on
// document means this works for every .password-toggle button on the page (Setup has two) without
// per-button wiring, and needs no DOMContentLoaded gate since delegation doesn't require the
// target elements to exist yet when the listener is attached.
document.addEventListener('click', (event) => {
    const button = event.target.closest('.password-toggle');
    if (!button) {
        return;
    }

    const input = document.getElementById(button.dataset.target);
    const icon = button.querySelector('i');
    if (!input || !icon) {
        return;
    }

    const willShow = input.type === 'password';
    input.type = willShow ? 'text' : 'password';
    icon.classList.toggle('bi-eye', !willShow);
    icon.classList.toggle('bi-eye-slash', willShow);
    button.setAttribute('aria-label', willShow ? 'Hide password' : 'Show password');
});
