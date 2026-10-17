document.querySelectorAll('[data-account-menu]').forEach(menu => {
    document.addEventListener('click', event => {
        if (!menu.contains(event.target)) menu.open = false;
    });
    document.addEventListener('keydown', event => {
        if (event.key === 'Escape' && menu.open) {
            menu.open = false;
            menu.querySelector('summary').focus();
        }
    });
});

const accountNavigation = document.querySelector('.account-settings-nav');
const currentFunction = accountNavigation?.querySelector('[aria-current="page"]');
function revealCurrentAccountFunction() {
    if (!currentFunction || accountNavigation.scrollWidth <= accountNavigation.clientWidth) return;
    accountNavigation.scrollLeft += currentFunction.getBoundingClientRect().left - accountNavigation.getBoundingClientRect().left
        - (accountNavigation.clientWidth - currentFunction.clientWidth) / 2;
}
revealCurrentAccountFunction();
window.matchMedia('(max-width: 767px)').addEventListener('change', revealCurrentAccountFunction);
