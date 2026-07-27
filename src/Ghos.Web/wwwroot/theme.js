(() => {
    const storageKey = "ghos-color-theme";
    const root = document.documentElement;
    const media = globalThis.matchMedia("(prefers-color-scheme: dark)");

    const getStoredTheme = () => {
        try {
            const value = globalThis.localStorage.getItem(storageKey);
            return value === "light" || value === "dark" ? value : null;
        } catch {
            return null;
        }
    };

    const getTheme = () => getStoredTheme() ?? (media.matches ? "dark" : "light");

    const updateThemeColor = (theme) => {
        const meta = document.querySelector('meta[name="theme-color"]');
        if (meta) {
            meta.content = theme === "dark" ? "#0d1410" : "#142116";
        }
    };

    const updateButton = (theme) => {
        const button = document.getElementById("ghos-theme-toggle");
        if (!button) {
            return;
        }

        const nextTheme = theme === "dark" ? "light" : "dark";
        const label = button.querySelector("[data-theme-label]");
        const icon = button.querySelector(".theme-toggle-icon");
        button.setAttribute("aria-label", `Switch to ${nextTheme} mode`);
        button.title = `Switch to ${nextTheme} mode`;
        button.dataset.nextTheme = nextTheme;
        if (label) {
            label.textContent = `${nextTheme[0].toUpperCase()}${nextTheme.slice(1)} mode`;
        }
        if (icon) {
            icon.textContent = theme === "dark" ? "☀" : "☾";
        }
    };

    const applyTheme = (theme) => {
        root.dataset.theme = theme;
        updateThemeColor(theme);
        updateButton(theme);
    };

    applyTheme(getTheme());

    /*
     * Use event delegation instead of binding directly to the button.
     * Blazor can replace layout DOM while reconnecting or navigating; a
     * document-level listener survives that replacement in every browser.
     */
    document.addEventListener("click", (event) => {
        const target = event.target;
        const button = target instanceof Element
            ? target.closest("#ghos-theme-toggle")
            : null;
        if (!button) {
            return;
        }

        event.preventDefault();
        const nextTheme = root.dataset.theme === "dark" ? "light" : "dark";
        try {
            globalThis.localStorage.setItem(storageKey, nextTheme);
        } catch {
            // The theme still changes for this page when storage is unavailable.
        }
        applyTheme(nextTheme);
    });

    const refreshButton = () => updateButton(root.dataset.theme || getTheme());
    document.addEventListener("DOMContentLoaded", refreshButton);

    /*
     * Refresh the label/icon when Blazor inserts a new header button. The
     * observer never binds click listeners and therefore cannot duplicate them.
     */
    const buttonObserver = new MutationObserver((mutations) => {
        if (mutations.some((mutation) =>
            Array.from(mutation.addedNodes).some((node) =>
                node instanceof Element &&
                (node.id === "ghos-theme-toggle" ||
                    node.querySelector?.("#ghos-theme-toggle"))))) {
            refreshButton();
        }
    });
    buttonObserver.observe(document.documentElement, {
        childList: true,
        subtree: true
    });

    media.addEventListener?.("change", () => {
        if (!getStoredTheme()) {
            applyTheme(getTheme());
        }
    });
})();
