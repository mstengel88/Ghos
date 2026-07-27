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

    const connectButton = () => {
        const button = document.getElementById("ghos-theme-toggle");
        if (!button || button.dataset.themeConnected === "true") {
            updateButton(getTheme());
            return;
        }

        button.dataset.themeConnected = "true";
        button.addEventListener("click", () => {
            const nextTheme = root.dataset.theme === "dark" ? "light" : "dark";
            try {
                globalThis.localStorage.setItem(storageKey, nextTheme);
            } catch {
                // The theme still changes for this page when storage is unavailable.
            }
            applyTheme(nextTheme);
        });
        updateButton(getTheme());
    };

    applyTheme(getTheme());
    document.addEventListener("DOMContentLoaded", connectButton);
    document.addEventListener("enhancedload", connectButton);

    media.addEventListener?.("change", () => {
        if (!getStoredTheme()) {
            applyTheme(getTheme());
        }
    });
})();
