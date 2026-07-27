(() => {
    const storageKey = "ghos-color-theme";
    const cookieName = "ghos-color-theme";
    const root = document.documentElement;
    const media = globalThis.matchMedia("(prefers-color-scheme: dark)");

    const normalizeTheme = (value) =>
        value === "light" || value === "dark" ? value : null;

    const getCookieTheme = () => {
        try {
            const prefix = `${cookieName}=`;
            const item = document.cookie
                .split(";")
                .map((value) => value.trim())
                .find((value) => value.startsWith(prefix));
            return normalizeTheme(item
                ? decodeURIComponent(item.slice(prefix.length))
                : null);
        } catch {
            return null;
        }
    };

    const getStoredTheme = () => {
        try {
            return normalizeTheme(globalThis.localStorage.getItem(storageKey))
                ?? getCookieTheme();
        } catch {
            return getCookieTheme();
        }
    };

    const getTheme = () => getStoredTheme() ?? (media.matches ? "dark" : "light");

    const saveTheme = (theme) => {
        try {
            globalThis.localStorage.setItem(storageKey, theme);
        } catch {
            // A durable cookie remains available when browser storage is blocked.
        }

        try {
            document.cookie = `${cookieName}=${encodeURIComponent(theme)}; ` +
                "Max-Age=31536000; Path=/; SameSite=Lax";
        } catch {
            // The selected theme still applies to the active page.
        }
    };

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
        root.style.colorScheme = theme;
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
        saveTheme(nextTheme);
        applyTheme(nextTheme);
    });

    const restoreTheme = () => applyTheme(getTheme());
    const refreshButton = () => updateButton(root.dataset.theme || getTheme());
    document.addEventListener("DOMContentLoaded", refreshButton);
    globalThis.addEventListener("pageshow", restoreTheme);
    globalThis.addEventListener("storage", (event) => {
        if (event.key === storageKey) {
            restoreTheme();
        }
    });

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

    /*
     * Enhanced Blazor navigation does not reload the document. Reassert the
     * persisted choice after navigation so every page and the app shell remain
     * synchronized.
     */
    document.addEventListener("enhancedload", restoreTheme);
    const connectBlazorThemeSync = () =>
        globalThis.Blazor?.addEventListener?.("enhancedload", restoreTheme);
    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", connectBlazorThemeSync, {
            once: true
        });
    } else {
        connectBlazorThemeSync();
    }
})();
